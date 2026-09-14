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

    public static readonly DiagnosticDescriptor UnsupportedModelProperty = new(
        id: "TW1005",
        title: "Unsupported domain model property",
        messageFormat: "The property '{0}' on [DomainModel] '{1}' is not a domain value, a domain model, or a supported collection of them; it cannot appear in the DTO shape",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static DiagnosticDescriptor ById(string id) => id switch
    {
        "TW1001" => InvalidDomainValueShape,
        "TW1002" => RuleNotApplicable,
        "TW1003" => NotComparable,
        "TW1004" => InvalidDomainModelShape,
        "TW1005" => UnsupportedModelProperty,
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
