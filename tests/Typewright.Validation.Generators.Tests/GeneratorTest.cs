using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Typewright.Validation.Generators.Tests;

internal static class GeneratorTest
{
    private static readonly Lazy<ImmutableArray<MetadataReference>> References = new(Load);

    private static ImmutableArray<MetadataReference> Load()
    {
        var trustedAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var references = trustedAssemblies
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();
        references.Add(MetadataReference.CreateFromFile(typeof(Validation.ValidationFailure).Assembly.Location));
        return [.. references];
    }

    /// <summary>Runs the Typewright generators over <paramref name="source"/>.</summary>
    public static (GeneratorDriverRunResult Result, Compilation Output) Run(string source)
    {
        var compilation = CSharpCompilation.Create(
            "GeneratorTests",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            References.Value,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new DomainValueGenerator(), new DomainModelGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        return (driver.GetRunResult(), output);
    }

    /// <summary>The per-generator result for <typeparamref name="TGenerator"/>.</summary>
    public static GeneratorRunResult For<TGenerator>(this GeneratorDriverRunResult result)
        => result.Results.Single(item => item.Generator.GetGeneratorType() == typeof(TGenerator));
}
