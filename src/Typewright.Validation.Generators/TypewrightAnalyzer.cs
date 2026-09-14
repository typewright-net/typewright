using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Typewright.Validation.Generators;

/// <summary>
/// Closes the construction holes the type system cannot: TW0001 (<c>new</c>/<c>default</c>
/// of a domain value bypasses validation), TW0002 (<c>with</c> on a model with cross-field
/// validation), TW0003 (domain type not partial), and TW0004 (model property that is not
/// domain-typed, silenced by <c>[Raw]</c>).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TypewrightAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
    [
        DiagnosticDescriptors.UnvalidatedConstruction,
        DiagnosticDescriptors.WithBypassesCrossFieldValidation,
        DiagnosticDescriptors.DomainTypeNotPartial,
        DiagnosticDescriptors.PropertyNotDomainTyped,
        DiagnosticDescriptors.UndeclaredErrorReported,
        DiagnosticDescriptors.ReportsWithoutHook,
    ];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterOperationAction(AnalyzeObjectCreation, OperationKind.ObjectCreation);
        context.RegisterOperationAction(AnalyzeDefaultValue, OperationKind.DefaultValue);
        context.RegisterOperationAction(AnalyzeWith, OperationKind.With);
        context.RegisterOperationAction(AnalyzeErrorReport, OperationKind.Invocation);
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeObjectCreation(OperationAnalysisContext context)
    {
        var operation = (IObjectCreationOperation)context.Operation;
        if (operation.Type is INamedTypeSymbol type
            && operation.Constructor is { Parameters.IsEmpty: true }
            && DomainTypeFacts.HasDomainValueAttribute(type))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.UnvalidatedConstruction,
                operation.Syntax.GetLocation(),
                $"new {type.Name}()",
                type.Name));
        }
    }

    private static void AnalyzeDefaultValue(OperationAnalysisContext context)
    {
        var operation = (IDefaultValueOperation)context.Operation;
        if (operation.Type is INamedTypeSymbol type
            && type.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T
            && DomainTypeFacts.HasDomainValueAttribute(type))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.UnvalidatedConstruction,
                operation.Syntax.GetLocation(),
                "default",
                type.Name));
        }
    }

    private static void AnalyzeWith(OperationAnalysisContext context)
    {
        var operation = (IWithOperation)context.Operation;
        if (operation.Operand.Type is INamedTypeSymbol type
            && DomainTypeFacts.HasDomainModelAttribute(type)
            && DomainTypeFacts.HasValidateHook(type))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.WithBypassesCrossFieldValidation,
                operation.Syntax.GetLocation(),
                type.Name));
        }
    }

    private static void AnalyzeErrorReport(OperationAnalysisContext context)
    {
        var operation = (IInvocationOperation)context.Operation;
        if (operation.TargetMethod.Name is not ("Add" or "AddAt")
            || operation.TargetMethod.ContainingType?.ToDisplayString() != "Typewright.Validation.ValidationErrors")
        {
            return;
        }

        // Only hooks are constrained: walk out of lambdas/local functions to the
        // containing method and require the static Validate hook of a domain type.
        var containing = context.ContainingSymbol;
        while (containing is IMethodSymbol { MethodKind: MethodKind.LambdaMethod or MethodKind.LocalFunction })
        {
            containing = containing.ContainingSymbol;
        }

        if (containing is not IMethodSymbol { Name: "Validate", IsStatic: true } hook
            || hook.ContainingType is not { } domainType
            || (!DomainTypeFacts.HasDomainValueAttribute(domainType)
                && !DomainTypeFacts.HasDomainModelAttribute(domainType)))
        {
            return;
        }

        var errorArgument = operation.Arguments.FirstOrDefault(argument =>
            argument.Parameter?.Type.ToDisplayString() == "Typewright.Validation.IDomainError");
        if (errorArgument is null)
        {
            return;
        }

        // The argument is implicitly converted to IDomainError; the interesting type is
        // the operand's.
        var value = errorArgument.Value;
        while (value is IConversionOperation conversion)
        {
            value = conversion.Operand;
        }

        var errorType = value.Type;
        var declared = DomainTypeFacts.ComputeErrorTypes(domainType);
        var display = errorType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (errorType is null
            || errorType.TypeKind == TypeKind.Interface
            || !declared.Any(candidate => candidate.Display == display))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.UndeclaredErrorReported,
                value.Syntax.GetLocation(),
                errorType?.Name ?? "IDomainError",
                domainType.Name));
        }
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        var symbol = (INamedTypeSymbol)context.Symbol;
        var isDomainValue = DomainTypeFacts.HasDomainValueAttribute(symbol);
        var isDomainModel = !isDomainValue && DomainTypeFacts.HasDomainModelAttribute(symbol);
        if (!isDomainValue && !isDomainModel)
        {
            return;
        }

        var isPartial = symbol.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax(context.CancellationToken))
            .OfType<TypeDeclarationSyntax>()
            .Any(declaration => declaration.Modifiers.Any(SyntaxKind.PartialKeyword));
        if (!isPartial)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.DomainTypeNotPartial,
                symbol.Locations.FirstOrDefault() ?? Location.None,
                symbol.Name));
        }

        if (!DomainTypeFacts.HasAnyValidateHook(symbol))
        {
            foreach (var attribute in symbol.GetAttributes())
            {
                if (attribute.AttributeClass?.OriginalDefinition.ToDisplayString()
                        == DomainTypeFacts.ReportsAttributeDisplay
                    && attribute.AttributeClass is { TypeArguments.Length: 1 } reports)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.ReportsWithoutHook,
                        attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
                            ?? symbol.Locations.FirstOrDefault() ?? Location.None,
                        reports.TypeArguments[0].Name,
                        symbol.Name));
                }
            }
        }

        if (!isDomainModel)
        {
            return;
        }

        foreach (var member in symbol.GetMembers())
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (member is IPropertySymbol property
                && DomainTypeFacts.IsCandidateModelProperty(property)
                && !DomainTypeFacts.IsSupportedModelPropertyShape(property)
                && !DomainTypeFacts.HasRawAttribute(property))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.PropertyNotDomainTyped,
                    property.Locations.FirstOrDefault() ?? Location.None,
                    property.Name,
                    symbol.Name));
            }
        }
    }
}
