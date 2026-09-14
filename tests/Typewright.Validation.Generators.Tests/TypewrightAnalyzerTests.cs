using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace Typewright.Validation.Generators.Tests;

public class TypewrightAnalyzerTests
{
    private const string Domain = """
        using System;
        using Typewright.Validation;

        namespace Analyzed;

        [DomainValue<string>]
        [Length(1, 16)]
        public readonly partial record struct Code;

        [DomainValue<DateOnly>(Comparable = true)]
        public readonly partial record struct When;

        [DomainModel]
        public sealed partial record Window
        {
            public required When Begin { get; init; }
            public required When End { get; init; }

            static partial void Validate(Window candidate, ValidationErrors errors)
            {
                if (candidate.End < candidate.Begin)
                {
                    errors.Add("window.invalid", "End before begin.");
                }
            }
        }

        [DomainModel]
        public sealed partial record Tag
        {
            public required Code Code { get; init; }
        }

        """;

    private static async Task<string[]> AnalyzeAsync(string userCode)
    {
        var (_, output) = GeneratorTest.Run(Domain + userCode);
        var withAnalyzers = output.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new TypewrightAnalyzer()));
        var diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
        return [.. diagnostics.Select(diagnostic => diagnostic.Id).OrderBy(id => id)];
    }

    [Fact]
    public async Task Valid_domain_usage_is_clean()
    {
        // The generated code itself uses `default` (in TryParse) and the private
        // constructor; none of that may be reported, only user code is analyzed.
        var ids = await AnalyzeAsync("""
            public static class Usage
            {
                public static Code Ok() => Code.Create("fine");
            }
            """);

        Assert.Empty(ids);
    }

    [Fact]
    public async Task New_and_default_of_a_domain_value_report_TW0001()
    {
        var ids = await AnalyzeAsync("""
            public static class Usage
            {
                public static Code Broken1() => new Code();
                public static Code Broken2() => default;
                public static Code Broken3() => default(Code);
                public static Code? Fine() => default;   // Nullable<Code> is null, not an invalid value
            }
            """);

        Assert.Equal(["TW0001", "TW0001", "TW0001"], ids);
    }

    [Fact]
    public async Task With_on_a_cross_field_model_reports_TW0002()
    {
        var ids = await AnalyzeAsync("""
            public static class Usage
            {
                public static Window Broken(Window window, When end) => window with { End = end };
                public static Tag Fine(Tag tag, Code code) => tag with { Code = code };   // no cross-field rule
            }
            """);

        Assert.Equal(["TW0002"], ids);
    }

    [Fact]
    public async Task Non_partial_domain_types_report_TW0003()
    {
        var ids = await AnalyzeAsync("""
            [DomainValue<string>]
            public readonly record struct NotPartialValue;

            [DomainModel]
            public sealed record NotPartialModel;
            """);

        Assert.Equal(["TW0003", "TW0003"], ids);
    }

    [Fact]
    public async Task Non_domain_property_reports_TW0004_unless_marked_raw()
    {
        var ids = await AnalyzeAsync("""
            [DomainModel]
            public sealed partial record Mixed
            {
                public required Code Code { get; init; }
                public required string Plain { get; init; }

                [Raw]
                public int? Explicit { get; init; }
            }
            """);

        Assert.Equal(["TW0004"], ids);
    }
}
