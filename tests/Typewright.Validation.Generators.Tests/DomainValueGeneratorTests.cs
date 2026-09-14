using System.Reflection;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Typewright.Validation.Generators.Tests;

public class DomainValueGeneratorTests
{
    private const string RepresentativeSource = """
        using System;
        using Typewright.Validation;

        namespace Snapshots;

        [DomainValue<string>]
        [Pattern(@"^I\d{9}$")]
        public readonly partial record struct InvoiceNumber;

        [DomainValue<string>]
        [Length(1, 64), NotEmpty, Normalize(Trim = true, Case = Casing.Upper)]
        public readonly partial record struct AccountNumber
        {
            static partial void Validate(string value, ValidationErrors errors)
            {
                if (value.StartsWith("X", StringComparison.Ordinal))
                {
                    errors.Add("custom", "No X accounts.");
                }
            }
        }

        [DomainValue<DateOnly>(Comparable = true)]
        [Range("2000-01-01", "2099-12-31")]
        public readonly partial record struct EventDate;

        [DomainValue<int>]
        [Range(1, 10_000)]
        public readonly partial record struct Quantity;

        [DomainValue<decimal>]
        [Range(0, 1_000_000)]
        public readonly partial record struct UnitPrice;
        """;

    [Fact]
    public void Generated_sources_match_snapshots()
    {
        var (result, _) = GeneratorTest.Run(RepresentativeSource);
        var sources = result.For<DomainValueGenerator>().GeneratedSources;
        SnapshotAssert.MatchAll(sources.Select(source => (source.HintName, source.SourceText.ToString())));
    }

    [Fact]
    public void Generated_code_compiles_clean()
    {
        var (result, output) = GeneratorTest.Run(RepresentativeSource);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(5, result.GeneratedTrees.Length);
        Assert.Empty(output.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning));
    }

    [Fact]
    public void Generated_values_enforce_rules_at_runtime()
    {
        var (_, output) = GeneratorTest.Run(RepresentativeSource);
        var assembly = EmitAndLoad(output);

        // Normalization (trim + upper) and the custom hook on string raws: the hook
        // rejects accounts starting with X, so the normalized "  x123  " must fail
        // while a plain account passes.
        var accountNumber = assembly.GetType("Snapshots.AccountNumber")!;
        var created = Invoke(accountNumber, "Create", typeof(string), "  abc123  ");
        Assert.Equal("ABC123", GetProperty(created, "Value"));

        var rejectedByHook = Invoke(accountNumber, "TryCreate", typeof(string), "  x123  ");
        Assert.False((bool)GetProperty(rejectedByHook, "IsValid")!);

        // String-parsed range bounds on a DateOnly raw.
        var eventDate = assembly.GetType("Snapshots.EventDate")!;
        var tooEarly = Invoke(eventDate, "TryCreate", typeof(DateOnly), new DateOnly(1999, 12, 31));
        Assert.False((bool)GetProperty(tooEarly, "IsValid")!);
        var inRange = Invoke(eventDate, "TryCreate", typeof(DateOnly), new DateOnly(2026, 1, 1));
        Assert.True((bool)GetProperty(inRange, "IsValid")!);

        // Numeric range on a decimal raw, through the nullable overload.
        var unitPrice = assembly.GetType("Snapshots.UnitPrice")!;
        var missing = Invoke(unitPrice, "TryCreate", typeof(decimal?), null);
        Assert.False((bool)GetProperty(missing, "IsValid")!);
        var negative = Invoke(unitPrice, "TryCreate", typeof(decimal), -1m);
        Assert.False((bool)GetProperty(negative, "IsValid")!);
    }

    [Fact]
    public void Wrong_shape_reports_TW1001()
    {
        var (result, _) = GeneratorTest.Run("""
            using Typewright.Validation;

            namespace Broken;

            [DomainValue<string>]
            public partial record struct NotReadonly;
            """);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("TW1001", diagnostic.Id);
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void Missing_partial_skips_generation_silently()
    {
        // Analyzer TW0003 owns the diagnostic; the generator must not double-report.
        var (result, _) = GeneratorTest.Run("""
            using Typewright.Validation;

            namespace Broken;

            [DomainValue<string>]
            public readonly record struct NotPartial;
            """);

        Assert.Empty(result.Diagnostics);
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void String_rule_on_numeric_raw_reports_TW1002()
    {
        var (result, _) = GeneratorTest.Run("""
            using Typewright.Validation;

            namespace Broken;

            [DomainValue<int>]
            [Pattern("^x$")]
            public readonly partial record struct Weird;
            """);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("TW1002", diagnostic.Id);
        Assert.Single(result.GeneratedTrees);
    }

    [Fact]
    public void Comparable_on_non_comparable_raw_reports_TW1003()
    {
        var (result, _) = GeneratorTest.Run("""
            using Typewright.Validation;

            namespace Broken;

            [DomainValue<byte[]>(Comparable = true)]
            public readonly partial record struct Blob;
            """);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("TW1003", diagnostic.Id);
    }

    private static Assembly EmitAndLoad(Compilation compilation)
    {
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        return Assembly.Load(stream.ToArray());
    }

    private static object? Invoke(Type type, string method, Type parameterType, object? argument)
        => type.GetMethod(method, BindingFlags.Public | BindingFlags.Static, [parameterType])!
            .Invoke(null, [argument]);

    private static object? GetProperty(object? instance, string name)
        => instance!.GetType().GetProperty(name)!.GetValue(instance);
}
