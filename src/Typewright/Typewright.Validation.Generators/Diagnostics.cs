using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Typewright.Validation.Generators;

internal static class DiagnosticDescriptors
{
    private const string Category = "Typewright";

    public static readonly DiagnosticDescriptor InvalidDomainValueShape = new(
        id: "TW1001",
        title: "Invalid [DomainValue] type shape",
        messageFormat: "The [DomainValue] type '{0}' must be a non-generic, non-nested, partial readonly record struct",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RuleNotApplicable = new(
        id: "TW1002",
        title: "Rule attribute not applicable to raw type",
        messageFormat: "The rule '{0}' cannot be applied to a domain value with raw type '{1}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NotComparable = new(
        id: "TW1003",
        title: "Raw type is not comparable",
        messageFormat: "Comparable = true requires the raw type '{0}' to implement IComparable<{0}>",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidDomainModelShape = new(
        id: "TW1004",
        title: "Invalid [DomainModel] type shape",
        messageFormat: "The [DomainModel] type '{0}' must be a non-generic, non-nested, partial record class",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // TW0001-TW0004 are reported by TypewrightAnalyzer, not the generators.

    public static readonly DiagnosticDescriptor UnvalidatedConstruction = new(
        id: "TW0001",
        title: "Domain value constructed without validation",
        messageFormat: "'{0}' bypasses validation for domain value '{1}'; use {1}.Create or {1}.TryCreate instead",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor WithBypassesCrossFieldValidation = new(
        id: "TW0002",
        title: "'with' bypasses cross-field validation",
        messageFormat: "A 'with' expression on '{0}' bypasses its cross-field Validate rule; rebuild the model through {0}.Create or {0}.TryCreate",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DomainTypeNotPartial = new(
        id: "TW0003",
        title: "Domain type is not partial",
        messageFormat: "The domain type '{0}' must be declared partial so the Typewright generator can extend it",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor PropertyNotDomainTyped = new(
        id: "TW0004",
        title: "Domain model property is not domain-typed",
        messageFormat: "The property '{0}' on domain model '{1}' is not a domain value, domain model, or collection of them; it is copied to the DTO unvalidated (mark it [Raw] to make that explicit)",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UndeclaredErrorReported = new(
        id: "TW0005",
        title: "Hook reports an undeclared error type",
        messageFormat: "The Validate hook on '{1}' reports '{0}', which is not in the type's error set; declare it with [Reports<{0}>] so the set stays finite and discoverable",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ReportsWithoutHook = new(
        id: "TW0006",
        title: "[Reports] without a Validate hook",
        messageFormat: "'{1}' declares [Reports<{0}>] but has no Validate hook that could report it",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static DiagnosticDescriptor ById(string id) => id switch
    {
        "TW1001" => InvalidDomainValueShape,
        "TW1002" => RuleNotApplicable,
        "TW1003" => NotComparable,
        "TW1004" => InvalidDomainModelShape,
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown diagnostic id."),
    };
}

/// <summary>A <see cref="Location"/> in equatable form, so pipeline records cache correctly.</summary>
internal readonly record struct LocationSpec(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)
{
    public static LocationSpec From(Location location)
    {
        var lineSpan = location.GetLineSpan();
        return new LocationSpec(lineSpan.Path, location.SourceSpan, lineSpan.Span);
    }

    public Location ToLocation() => Location.Create(FilePath, TextSpan, LineSpan);
}

/// <summary>A diagnostic to report, in equatable form.</summary>
internal sealed record DiagnosticSpec(string Id, LocationSpec Location, EquatableArray<string> Args)
{
    public Diagnostic ToDiagnostic()
    {
        var args = new object[Args.Count];
        for (var i = 0; i < Args.Count; i++)
        {
            args[i] = Args[i];
        }

        return Diagnostic.Create(DiagnosticDescriptors.ById(Id), Location.ToLocation(), args);
    }

    public static DiagnosticSpec Create(DiagnosticDescriptor descriptor, Location location, params string[] args)
        => new(descriptor.Id, LocationSpec.From(location), new EquatableArray<string>(args));
}
