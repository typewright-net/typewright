using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Typewright.Validation.Generators;

/// <summary>
/// Symbol classification shared by the generators and the analyzers. Types still being
/// generated in the current compilation are recognized by their attributes; compiled
/// types fall back to the contract interfaces.
/// </summary>
internal static class DomainTypeFacts
{
    public const string DomainValueAttributeDisplay = "Typewright.Validation.DomainValueAttribute<TRaw>";
    public const string DomainModelAttributeDisplay = "Typewright.Validation.DomainModelAttribute";
    public const string RawAttributeDisplay = "Typewright.Validation.RawAttribute";
    public const string ReportsAttributeDisplay = "Typewright.Validation.ReportsAttribute<TError>";

    public static bool IsDomainValue(ITypeSymbol type)
        => TryGetRawShape(type, out var isModel) is not null && !isModel;

    public static bool IsDomainModel(ITypeSymbol type)
        => TryGetRawShape(type, out var isModel) is not null && isModel;

    public static bool HasDomainValueAttribute(ITypeSymbol type)
        => type.GetAttributes().Any(attribute =>
            attribute.AttributeClass?.OriginalDefinition.ToDisplayString() == DomainValueAttributeDisplay);

    public static bool HasDomainModelAttribute(ITypeSymbol type)
        => type.GetAttributes().Any(attribute =>
            attribute.AttributeClass?.ToDisplayString() == DomainModelAttributeDisplay);

    public static bool HasRawAttribute(IPropertySymbol property)
        => property.GetAttributes().Any(attribute =>
            attribute.AttributeClass?.ToDisplayString() == RawAttributeDisplay);

    /// <summary>
    /// Resolves the raw shape backing a domain type: the raw type of a domain value, or
    /// the DTO type of a domain model (by the nested-Dto convention for co-generated
    /// models). Returns null for non-domain types.
    /// </summary>
    public static string? TryGetRawShape(ITypeSymbol type, out bool isModel)
    {
        isModel = false;
        if (type is not INamedTypeSymbol named)
        {
            return null;
        }

        foreach (var attribute in named.GetAttributes())
        {
            var attributeName = attribute.AttributeClass?.OriginalDefinition.ToDisplayString();
            if (attributeName == DomainValueAttributeDisplay
                && attribute.AttributeClass is { TypeArguments.Length: 1 } valueAttribute)
            {
                return valueAttribute.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }

            if (attributeName == DomainModelAttributeDisplay)
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

    /// <summary>
    /// Whether the model has an implemented cross-field hook. A partial method with only
    /// its generated defining declaration is not a hook, so this works both before
    /// generation (only the user's implementing declaration exists) and after (the
    /// defining declaration exists for every model).
    /// </summary>
    public static bool HasValidateHook(INamedTypeSymbol symbol)
        => symbol.GetMembers("Validate").OfType<IMethodSymbol>().Any(method =>
            IsImplementedHookShape(method)
            && SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, symbol));

    /// <summary>
    /// Whether the type has any implemented Validate hook — a value's
    /// <c>Validate(TRaw, ValidationErrors)</c> or a model's
    /// <c>Validate(TSelf, ValidationErrors)</c>.
    /// </summary>
    public static bool HasAnyValidateHook(INamedTypeSymbol symbol)
        => symbol.GetMembers("Validate").OfType<IMethodSymbol>().Any(IsImplementedHookShape);

    private static bool IsImplementedHookShape(IMethodSymbol method)
        => method.IsStatic
            && method.ReturnsVoid
            && method.Parameters.Length == 2
            && method.Parameters[1].Type.ToDisplayString() == "Typewright.Validation.ValidationErrors"
            && (!method.IsPartialDefinition || method.PartialImplementationPart is not null);

    /// <summary>Candidate model properties: public instance get/init properties.</summary>
    public static bool IsCandidateModelProperty(IPropertySymbol property)
        => !property.IsStatic
            && !property.IsIndexer
            && property.DeclaredAccessibility == Accessibility.Public
            && property.GetMethod is not null
            && property.SetMethod is not null;

    /// <summary>
    /// Strips optionality: unwraps <c>Nullable&lt;T&gt;</c> and removes a nullable
    /// reference annotation.
    /// </summary>
    public static ITypeSymbol Unwrap(ITypeSymbol type, out bool optional)
    {
        if (type is INamedTypeSymbol nullable
            && nullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            optional = true;
            return nullable.TypeArguments[0];
        }

        if (type.NullableAnnotation == NullableAnnotation.Annotated)
        {
            optional = true;
            return type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
        }

        optional = false;
        return type;
    }

    /// <summary>The element type when <paramref name="type"/> is a supported collection shape.</summary>
    public static ITypeSymbol? CollectionElement(ITypeSymbol type)
        => type is INamedTypeSymbol collection
            && collection.OriginalDefinition.ToDisplayString()
                is "System.Collections.Generic.IReadOnlyList<T>" or "System.Collections.Generic.List<T>"
            ? collection.TypeArguments[0].WithNullableAnnotation(NullableAnnotation.NotAnnotated)
            : null;

    /// <summary>Whether a model property is a domain value, model, or supported collection of them.</summary>
    public static bool IsSupportedModelPropertyShape(IPropertySymbol property)
    {
        var type = Unwrap(property.Type, out _);
        if (CollectionElement(type) is { } element)
        {
            return TryGetRawShape(element, out _) is not null;
        }

        return TryGetRawShape(type, out _) is not null;
    }

    /// <summary>The error types declared with <c>[Reports&lt;TError&gt;]</c> on a domain type.</summary>
    public static IEnumerable<ITypeSymbol> ReportedErrorTypes(ITypeSymbol symbol)
        => symbol.GetAttributes()
            .Where(attribute => attribute.AttributeClass?.OriginalDefinition.ToDisplayString()
                == ReportsAttributeDisplay)
            .Select(attribute => attribute.AttributeClass!.TypeArguments[0]);

    /// <summary>
    /// Computes the finite error set for a domain type from its attributes: binding
    /// errors, rule-implied errors, declared <c>[Reports]</c> types, and for models the
    /// union over composed fields (recursive, cycle-safe). Works for co-generated and
    /// compiled types alike, since both carry the attributes in metadata.
    /// </summary>
    public static List<ErrorTypeSpec> ComputeErrorTypes(INamedTypeSymbol symbol)
    {
        var results = new Dictionary<string, ErrorTypeSpec>();
        var visited = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        Collect(symbol, results, visited);
        var list = results.Values.ToList();
        list.Sort(static (left, right) => string.CompareOrdinal(left.SimpleName, right.SimpleName));
        return list;
    }

    private static void Collect(
        INamedTypeSymbol symbol,
        Dictionary<string, ErrorTypeSpec> results,
        HashSet<INamedTypeSymbol> visited)
    {
        if (!visited.Add(symbol))
        {
            return;
        }

        foreach (var reported in ReportedErrorTypes(symbol))
        {
            AddError(results, reported.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), reported.Name);
        }

        var rawShape = TryGetRawShape(symbol, out var isModel);
        if (rawShape is null)
        {
            return;
        }

        AddBuiltin(results, "RequiredError");
        if (isModel)
        {
            AddBuiltin(results, "JsonInvalidError");
            foreach (var member in symbol.GetMembers())
            {
                if (member is not IPropertySymbol property || !IsCandidateModelProperty(property))
                {
                    continue;
                }

                var type = Unwrap(property.Type, out _);
                if (CollectionElement(type) is { } element)
                {
                    type = element;
                }

                if (type is INamedTypeSymbol domain && TryGetRawShape(domain, out _) is not null)
                {
                    Collect(domain, results, visited);
                }
            }

            return;
        }

        var rawIsString = false;
        var rawIsParsable = false;
        foreach (var attribute in symbol.GetAttributes())
        {
            var attributeName = attribute.AttributeClass?.OriginalDefinition.ToDisplayString();
            switch (attributeName)
            {
                case DomainValueAttributeDisplay
                    when attribute.AttributeClass is { TypeArguments.Length: 1 } valueAttribute:
                    var raw = valueAttribute.TypeArguments[0];
                    rawIsString = raw.SpecialType == SpecialType.System_String;
                    rawIsParsable = rawIsString || raw.AllInterfaces.Any(candidate =>
                        candidate.OriginalDefinition.ToDisplayString() == "System.IParsable<TSelf>"
                        && candidate.TypeArguments.Length == 1
                        && SymbolEqualityComparer.Default.Equals(candidate.TypeArguments[0], raw));
                    break;

                case "Typewright.Validation.PatternAttribute":
                    AddBuiltin(results, "PatternError");
                    break;

                case "Typewright.Validation.LengthAttribute":
                    AddBuiltin(results, "LengthError");
                    break;

                case "Typewright.Validation.NotEmptyAttribute":
                    AddBuiltin(results, "NotEmptyError");
                    break;

                case "Typewright.Validation.RangeAttribute":
                    AddBuiltin(results, "RangeError");
                    break;
            }
        }

        if (rawIsParsable && !rawIsString)
        {
            AddBuiltin(results, "FormatError");
        }
    }

    private static void AddBuiltin(Dictionary<string, ErrorTypeSpec> results, string simpleName)
        => AddError(results, "global::Typewright.Validation." + simpleName, simpleName);

    private static void AddError(Dictionary<string, ErrorTypeSpec> results, string display, string simpleName)
    {
        if (!results.ContainsKey(display))
        {
            results[display] = new ErrorTypeSpec(display, simpleName);
        }
    }
}

/// <summary>One member of a domain type's finite error set.</summary>
/// <param name="Display">Fully qualified display of the error type.</param>
/// <param name="SimpleName">The simple type name, used for sorting and handler naming.</param>
internal sealed record ErrorTypeSpec(string Display, string SimpleName);
