using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Typewright.Validation.Generators;

/// <summary>
/// Emits the implementation of every <c>[DomainValue&lt;TRaw&gt;]</c> declaration: storage,
/// factories, parsing, comparison, JSON and TypeConverter support, and the
/// <c>DomainTypeInfo</c> descriptor.
/// </summary>
[Generator]
public sealed class DomainValueGenerator : IIncrementalGenerator
{
    private const string DomainValueAttributeMetadataName = "Typewright.Validation.DomainValueAttribute`1";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var results = context.SyntaxProvider.ForAttributeWithMetadataName(
            DomainValueAttributeMetadataName,
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
                spec = spec with
                {
                    EmitBindAsync = aspNetAvailable && spec.RawIsParsable && !spec.UserDeclaredBindAsync,
                };
                productionContext.AddSource(spec.HintName, DomainValueEmitter.Emit(spec));
            }
        });
    }

    internal sealed record ParseResult(ValueSpec? Spec, EquatableArray<DiagnosticSpec> Diagnostics);

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
            || symbol.TypeKind != TypeKind.Struct
            || !symbol.IsReadOnly
            || symbol.Arity != 0
            || symbol.ContainingType is not null)
        {
            diagnostics.Add(DiagnosticSpec.Create(
                DiagnosticDescriptors.InvalidDomainValueShape, location, symbol.Name));
            return Failed(diagnostics);
        }

        if (!isPartial)
        {
            // Analyzer TW0003 reports this; just skip generation.
            return Failed(diagnostics);
        }

        var domainValueAttribute = context.Attributes[0];
        if (domainValueAttribute.AttributeClass?.TypeArguments.FirstOrDefault() is not ITypeSymbol raw)
        {
            diagnostics.Add(DiagnosticSpec.Create(
                DiagnosticDescriptors.InvalidDomainValueShape, location, symbol.Name));
            return Failed(diagnostics);
        }

        var comparable = domainValueAttribute.NamedArguments
            .FirstOrDefault(argument => argument.Key == "Comparable").Value.Value is true;

        var rawDisplay = raw.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var rawShortDisplay = raw.ToDisplayString();
        var rawIsString = raw.SpecialType == SpecialType.System_String;

        if (comparable && !ImplementsInterface(raw, "System.IComparable<T>", raw))
        {
            diagnostics.Add(DiagnosticSpec.Create(
                DiagnosticDescriptors.NotComparable, location, rawShortDisplay));
            comparable = false;
        }

        var spec = new ValueSpec
        {
            Namespace = symbol.ContainingNamespace.IsGlobalNamespace
                ? ""
                : symbol.ContainingNamespace.ToDisplayString(),
            Name = symbol.Name,
            HintName = (symbol.ContainingNamespace.IsGlobalNamespace
                ? symbol.Name
                : symbol.ContainingNamespace.ToDisplayString() + "." + symbol.Name) + ".g.cs",
            RawDisplay = rawDisplay,
            RawIsString = rawIsString,
            RawIsReference = raw.IsReferenceType,
            RawIsParsable = rawIsString || ImplementsInterface(raw, "System.IParsable<TSelf>", raw),
            RawIsSpanParsable = rawIsString || ImplementsInterface(raw, "System.ISpanParsable<TSelf>", raw),
            Comparable = comparable,
            ToStringKind = ClassifyToString(raw),
            UserDeclaredBindAsync = symbol.GetMembers("BindAsync").Any(member => member.IsStatic),
        };

        foreach (var attribute in symbol.GetAttributes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attributeName = attribute.AttributeClass?.ToDisplayString();
            switch (attributeName)
            {
                case "Typewright.Validation.PatternAttribute":
                    if (RequireStringRaw(attributeName)
                        && attribute.ConstructorArguments.FirstOrDefault().Value is string pattern)
                    {
                        spec = spec with { Pattern = pattern };
                    }

                    break;

                case "Typewright.Validation.LengthAttribute":
                    if (RequireStringRaw(attributeName)
                        && attribute.ConstructorArguments.Length == 2)
                    {
                        spec = spec with
                        {
                            LengthMin = attribute.ConstructorArguments[0].Value as int?,
                            LengthMax = attribute.ConstructorArguments[1].Value as int?,
                        };
                    }

                    break;

                case "Typewright.Validation.NotEmptyAttribute":
                    if (RequireStringRaw(attributeName))
                    {
                        spec = spec with { NotEmpty = true };
                    }

                    break;

                case "Typewright.Validation.NormalizeAttribute":
                    if (RequireStringRaw(attributeName))
                    {
                        spec = spec with
                        {
                            NormalizeTrim = attribute.NamedArguments
                                .FirstOrDefault(argument => argument.Key == "Trim").Value.Value is true,
                            NormalizeCase = attribute.NamedArguments
                                .FirstOrDefault(argument => argument.Key == "Case").Value.Value as int? ?? 0,
                        };
                    }

                    break;

                case "Typewright.Validation.RangeAttribute":
                    spec = ParseRange(spec, attribute, attributeName);
                    break;
            }
        }

        spec = spec with
        {
            ErrorTypes = new EquatableArray<ErrorTypeSpec>(DomainTypeFacts.ComputeErrorTypes(symbol).ToArray()),
        };

        return new ParseResult(spec, new EquatableArray<DiagnosticSpec>(diagnostics.ToArray()));

        bool RequireStringRaw(string attributeName)
        {
            if (rawIsString)
            {
                return true;
            }

            diagnostics.Add(DiagnosticSpec.Create(
                DiagnosticDescriptors.RuleNotApplicable, location, ShortRuleName(attributeName), rawShortDisplay));
            return false;
        }

        ValueSpec ParseRange(ValueSpec current, AttributeData attribute, string attributeName)
        {
            if (attribute.ConstructorArguments.Length != 2)
            {
                return current;
            }

            var minConstant = attribute.ConstructorArguments[0].Value;
            var maxConstant = attribute.ConstructorArguments[1].Value;

            if (minConstant is string minText && maxConstant is string maxText)
            {
                if (rawIsString || raw.IsReferenceType || !ImplementsInterface(raw, "System.IParsable<TSelf>", raw))
                {
                    diagnostics.Add(DiagnosticSpec.Create(
                        DiagnosticDescriptors.RuleNotApplicable, location, ShortRuleName(attributeName), rawShortDisplay));
                    return current;
                }

                return current with
                {
                    HasRange = true,
                    RangeMinExpr = "s_rangeMin",
                    RangeMaxExpr = "s_rangeMax",
                    RangeMinDisplay = minText,
                    RangeMaxDisplay = maxText,
                    RangeMinParseText = minText,
                    RangeMaxParseText = maxText,
                };
            }

            var min = FormatBound(minConstant);
            var max = FormatBound(maxConstant);
            if (min is null || max is null)
            {
                diagnostics.Add(DiagnosticSpec.Create(
                    DiagnosticDescriptors.RuleNotApplicable, location, ShortRuleName(attributeName), rawShortDisplay));
                return current;
            }

            return current with
            {
                HasRange = true,
                RangeMinExpr = min.Value.Expr,
                RangeMaxExpr = max.Value.Expr,
                RangeMinDisplay = min.Value.Display,
                RangeMaxDisplay = max.Value.Display,
                RangeMinSchema = min.Value.Schema,
                RangeMaxSchema = max.Value.Schema,
            };
        }

        (string Expr, string Display, string Schema)? FormatBound(object? constant)
        {
            switch (constant)
            {
                case int intValue:
                {
                    var display = intValue.ToString(CultureInfo.InvariantCulture);
                    var expr = raw.SpecialType switch
                    {
                        SpecialType.System_Int32 => display,
                        SpecialType.System_Int64 => display + "L",
                        SpecialType.System_Decimal => display + "m",
                        SpecialType.System_Double => display + "d",
                        SpecialType.System_Single => display + "f",
                        SpecialType.System_Int16 or SpecialType.System_Byte
                            or SpecialType.System_SByte or SpecialType.System_UInt16
                            or SpecialType.System_UInt32 or SpecialType.System_UInt64
                            => "(" + rawDisplay + ")" + display,
                        _ => null,
                    };
                    return expr is null ? null : (expr, display, display);
                }

                case double doubleValue:
                {
                    var display = doubleValue.ToString("R", CultureInfo.InvariantCulture);
                    var expr = raw.SpecialType switch
                    {
                        SpecialType.System_Double => display + "d",
                        SpecialType.System_Single => display + "f",
                        SpecialType.System_Decimal => display + "m",
                        _ => null,
                    };
                    return expr is null
                        ? null
                        : (expr, display, ((decimal)doubleValue).ToString(CultureInfo.InvariantCulture));
                }

                default:
                    return null;
            }
        }
    }

    private static ParseResult Failed(List<DiagnosticSpec> diagnostics)
        => new(null, new EquatableArray<DiagnosticSpec>(diagnostics.ToArray()));

    private static string ShortRuleName(string attributeName)
    {
        var simple = attributeName.Substring(attributeName.LastIndexOf('.') + 1);
        const string suffix = "Attribute";
        return simple.EndsWith(suffix, System.StringComparison.Ordinal)
            ? simple.Substring(0, simple.Length - suffix.Length)
            : simple;
    }

    private static bool ImplementsInterface(ITypeSymbol type, string originalDefinitionDisplay, ITypeSymbol argument)
        => type.AllInterfaces.Any(candidate =>
            candidate.OriginalDefinition.ToDisplayString() == originalDefinitionDisplay
            && candidate.TypeArguments.Length == 1
            && SymbolEqualityComparer.Default.Equals(candidate.TypeArguments[0], argument));

    private static ToStringKind ClassifyToString(ITypeSymbol raw)
    {
        if (raw.SpecialType == SpecialType.System_String)
        {
            return ToStringKind.StringRaw;
        }

        var display = raw.ToDisplayString();
        if (display is "System.DateOnly" or "System.TimeOnly" or "System.DateTime" or "System.DateTimeOffset")
        {
            return ToStringKind.Temporal;
        }

        return raw.AllInterfaces.Any(candidate => candidate.ToDisplayString() == "System.IFormattable")
            ? ToStringKind.Formattable
            : ToStringKind.Plain;
    }
}
