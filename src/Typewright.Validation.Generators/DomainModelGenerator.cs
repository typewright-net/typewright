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
            static (node, _) => node is TypeDeclarationSyntax,
            static (ctx, ct) => Parse(ctx, ct));

        var combined = results.Combine(AspNetSupport.Available(context));
        context.RegisterSourceOutput(combined, static (productionContext, pair) =>
        {
            var (result, aspNetAvailable) = pair;
            foreach (var diagnostic in result.Diagnostics)
            {
                productionContext.ReportDiagnostic(diagnostic.ToDiagnostic());
            }

            if (result.Spec is { } spec)
            {
                spec = spec with { EmitBindAsync = aspNetAvailable && !spec.UserDeclaredBindAsync };
                productionContext.AddSource(spec.HintName, DomainModelEmitter.Emit(spec));
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
            || symbol.Arity != 0
            || symbol.ContainingType is not null)
        {
            diagnostics.Add(DiagnosticSpec.Create(
                DiagnosticDescriptors.InvalidDomainModelShape, location, symbol.Name));
            return new ParseResult(null, new EquatableArray<DiagnosticSpec>(diagnostics.ToArray()));
        }

        if (!isPartial)
        {
            // Analyzer TW0003 reports this; just skip generation.
            return new ParseResult(null, new EquatableArray<DiagnosticSpec>(diagnostics.ToArray()));
        }

        var properties = new List<PropertySpec>();
        foreach (var member in symbol.GetMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (member is not IPropertySymbol property
                || !DomainTypeFacts.IsCandidateModelProperty(property))
            {
                continue;
            }

            properties.Add(ClassifyProperty(property));
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
            HasValidateHook = DomainTypeFacts.HasValidateHook(symbol),
            UserDeclaredBindAsync = symbol.GetMembers("BindAsync").Any(member => member.IsStatic),
            Properties = new EquatableArray<PropertySpec>(properties.ToArray()),
            ErrorTypes = new EquatableArray<ErrorTypeSpec>(DomainTypeFacts.ComputeErrorTypes(symbol).ToArray()),
        };

        return new ParseResult(model, new EquatableArray<DiagnosticSpec>(diagnostics.ToArray()));
    }

    private static PropertySpec ClassifyProperty(IPropertySymbol property)
    {
        var type = DomainTypeFacts.Unwrap(property.Type, out var optional);

        if (DomainTypeFacts.CollectionElement(type) is { } element
            && DomainTypeFacts.TryGetRawShape(element, out var elementIsModel) is { } elementRaw)
        {
            var collectionDisplay = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return Build(
                property,
                elementIsModel ? PropertyKind.ModelCollection : PropertyKind.ValueCollection,
                element,
                elementRaw,
                collectionDisplay,
                optional);
        }

        if (DomainTypeFacts.TryGetRawShape(type, out var isModel) is { } rawShape)
        {
            return Build(
                property,
                isModel ? PropertyKind.Model : PropertyKind.Value,
                type,
                rawShape,
                type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                optional);
        }

        // Not a domain type: passthrough. Analyzer TW0004 warns unless the property is
        // explicitly marked [Raw].
        return Build(property, PropertyKind.Raw, type,
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            optional);
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
            PropertyKind.Value or PropertyKind.Model or PropertyKind.Raw => rawOrDto + "?",
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
