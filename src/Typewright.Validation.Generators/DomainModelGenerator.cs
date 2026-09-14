using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Typewright.Validation.Generators;

/// <summary>
/// Emits the implementation of every <c>[DomainModel]</c> declaration: the nested raw
/// <c>Dto</c> record, <c>TryCreate</c>/<c>Create</c>/<c>ToDto</c>, JSON routing through
/// the DTO, constructor rules driven by the cross-field hook, and the descriptor.
/// </summary>
[Generator]
public sealed class DomainModelGenerator : IIncrementalGenerator
{
    private const string DomainModelAttributeMetadataName = "Typewright.Validation.DomainModelAttribute";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var results = context.SyntaxProvider.ForAttributeWithMetadataName(
            DomainModelAttributeMetadataName,
            static (node, _) => node is RecordDeclarationSyntax,
            static (ctx, ct) => Parse(ctx, ct));

        context.RegisterSourceOutput(results, static (productionContext, result) =>
        {
            foreach (var diagnostic in result.Diagnostics)
            {
                productionContext.ReportDiagnostic(diagnostic.ToDiagnostic());
            }

            if (result.Spec is not null)
            {
                productionContext.AddSource(result.Spec.HintName, DomainModelEmitter.Emit(result.Spec));
            }
        });
    }

    internal sealed record ParseResult(ModelSpec? Spec, EquatableArray<DiagnosticSpec> Diagnostics);

    private static ParseResult Parse(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        var diagnostics = new List<DiagnosticSpec>();
        var symbol = (INamedTypeSymbol)context.TargetSymbol;
        var location = context.TargetNode.GetLocation();

        var isPartial = symbol.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax(cancellationToken))
            .OfType<TypeDeclarationSyntax>()
            .Any(declaration => declaration.Modifiers.Any(SyntaxKind.PartialKeyword));

        if (!symbol.IsRecord
            || symbol.TypeKind != TypeKind.Class
            || !isPartial
            || symbol.Arity != 0
            || symbol.ContainingType is not null)
        {
            diagnostics.Add(DiagnosticSpec.Create(
                DiagnosticDescriptors.InvalidDomainModelShape, location, symbol.Name));
            return new ParseResult(null, new EquatableArray<DiagnosticSpec>(diagnostics.ToArray()));
        }

        var properties = new List<PropertySpec>();
        foreach (var member in symbol.GetMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (member is not IPropertySymbol property
                || property.IsStatic
                || property.IsIndexer
                || property.DeclaredAccessibility != Accessibility.Public
                || property.GetMethod is null
                || property.SetMethod is null)
            {
                continue;
            }

            var spec = ClassifyProperty(property);
            if (spec is null)
            {
                diagnostics.Add(DiagnosticSpec.Create(
                    DiagnosticDescriptors.UnsupportedModelProperty,
                    property.Locations.FirstOrDefault() ?? location,
                    property.Name,
                    symbol.Name));
                continue;
            }

            properties.Add(spec);
        }

        var model = new ModelSpec
        {
            Namespace = symbol.ContainingNamespace.IsGlobalNamespace
                ? ""
                : symbol.ContainingNamespace.ToDisplayString(),
            Name = symbol.Name,
            HintName = (symbol.ContainingNamespace.IsGlobalNamespace
                ? symbol.Name
                : symbol.ContainingNamespace.ToDisplayString() + "." + symbol.Name) + ".g.cs",
            HasValidateHook = HasValidateHook(symbol),
            Properties = new EquatableArray<PropertySpec>(properties.ToArray()),
        };

        return new ParseResult(model, new EquatableArray<DiagnosticSpec>(diagnostics.ToArray()));
    }

    private static bool HasValidateHook(INamedTypeSymbol symbol)
        => symbol.GetMembers("Validate").OfType<IMethodSymbol>().Any(method =>
            method.IsStatic
            && method.ReturnsVoid
            && method.Parameters.Length == 2
            && SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, symbol)
            && method.Parameters[1].Type.ToDisplayString() == "Typewright.Validation.ValidationErrors");

    private static PropertySpec? ClassifyProperty(IPropertySymbol property)
    {
        var type = property.Type;
        var optional = false;

        if (type is INamedTypeSymbol nullable
            && nullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            optional = true;
            type = nullable.TypeArguments[0];
        }
        else if (type.NullableAnnotation == NullableAnnotation.Annotated)
        {
            optional = true;
            type = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
        }

        PropertyKind kind;
        ITypeSymbol domain;
        string declaredDisplay;

        if (type is INamedTypeSymbol collection
            && collection.OriginalDefinition.ToDisplayString()
                is "System.Collections.Generic.IReadOnlyList<T>" or "System.Collections.Generic.List<T>")
        {
            var element = collection.TypeArguments[0].WithNullableAnnotation(NullableAnnotation.NotAnnotated);
            if (TryGetRawShape(element, out var elementIsModel) is not { } elementRaw)
            {
                return null;
            }

            kind = elementIsModel ? PropertyKind.ModelCollection : PropertyKind.ValueCollection;
            domain = element;
            declaredDisplay = collection.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
                .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return Build(property, kind, domain, elementRaw, declaredDisplay, optional);
        }

        if (TryGetRawShape(type, out var isModel) is not { } rawShape)
        {
            return null;
        }

        kind = isModel ? PropertyKind.Model : PropertyKind.Value;
        domain = type;
        declaredDisplay = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return Build(property, kind, domain, rawShape, declaredDisplay, optional);
    }

    private static PropertySpec Build(
        IPropertySymbol property,
        PropertyKind kind,
        ITypeSymbol domain,
        string rawOrDto,
        string declaredDisplay,
        bool optional)
    {
        var explicitJsonName = property.GetAttributes()
            .FirstOrDefault(attribute => attribute.AttributeClass?.ToDisplayString()
                == "System.Text.Json.Serialization.JsonPropertyNameAttribute")
            ?.ConstructorArguments.FirstOrDefault().Value as string;

        var domainDisplay = domain.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var dtoType = kind switch
        {
            PropertyKind.Value or PropertyKind.Model => rawOrDto + "?",
            _ => $"global::System.Collections.Generic.List<{rawOrDto}?>?",
        };

        return new PropertySpec
        {
            Name = property.Name,
            CamelName = CamelCase(property.Name),
            JsonName = explicitJsonName ?? CamelCase(property.Name).TrimStart('@'),
            ExplicitJsonName = explicitJsonName,
            Kind = kind,
            Optional = optional,
            DomainDisplay = domainDisplay,
            DomainIsValueType = domain.IsValueType,
            RawOrDtoDisplay = rawOrDto,
            DtoTypeDisplay = dtoType,
            DeclaredDisplay = declaredDisplay,
        };
    }

    /// <summary>
    /// Resolves the raw shape backing a domain type: the raw type of a domain value, or the
    /// DTO type of a domain model. Types still being generated in this compilation are
    /// resolved from their attributes (by convention for model DTOs); already-compiled types
    /// fall back to their contract interfaces.
    /// </summary>
    private static string? TryGetRawShape(ITypeSymbol type, out bool isModel)
    {
        isModel = false;
        if (type is not INamedTypeSymbol named)
        {
            return null;
        }

        foreach (var attribute in named.GetAttributes())
        {
            var attributeName = attribute.AttributeClass?.OriginalDefinition.ToDisplayString();
            if (attributeName == "Typewright.Validation.DomainValueAttribute<TRaw>"
                && attribute.AttributeClass is { TypeArguments.Length: 1 } valueAttribute)
            {
                return valueAttribute.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }

            if (attributeName == "Typewright.Validation.DomainModelAttribute")
            {
                isModel = true;
                return named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ".Dto";
            }
        }

        foreach (var contract in named.AllInterfaces)
        {
            var contractName = contract.OriginalDefinition.ToDisplayString();
            if (contractName == "Typewright.Validation.IDomainValue<TSelf, TRaw>")
            {
                return contract.TypeArguments[1].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }

            if (contractName == "Typewright.Validation.IDomainModel<TSelf, TDto>")
            {
                isModel = true;
                return contract.TypeArguments[1].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }
        }

        return null;
    }

    private static string CamelCase(string name)
    {
        if (name.Length == 0 || !char.IsUpper(name[0]))
        {
            return Escape(name);
        }

        return Escape(char.ToLowerInvariant(name[0]) + name.Substring(1));

        static string Escape(string identifier)
            => SyntaxFacts.GetKeywordKind(identifier) != SyntaxKind.None
                || SyntaxFacts.GetContextualKeywordKind(identifier) == SyntaxKind.VarKeyword
                ? "@" + identifier
                : identifier;
    }
}
