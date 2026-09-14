using System.Collections;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Typewright.Validation.Generators.Tests;

public class DomainModelGeneratorTests
{
    private const string RepresentativeSource = """
        using System;
        using System.Collections.Generic;
        using System.Text.Json.Serialization;
        using Typewright.Validation;

        namespace Snapshots;

        [DomainValue<string>]
        [Length(1, 32)]
        public readonly partial record struct Sku;

        [DomainValue<int>]
        [Range(1, 100)]
        public readonly partial record struct Count;

        [DomainValue<DateOnly>(Comparable = true)]
        public readonly partial record struct ShipDate;

        [DomainModel]
        public sealed partial record Parcel
        {
            public required Sku Sku { get; init; }
            public required Count Count { get; init; }
        }

        public sealed record TimeTravelError : IDomainError;

        [DomainModel]
        [Reports<TimeTravelError>]
        public sealed partial record Shipment
        {
            [JsonPropertyName("shipmentSku")]
            public required Sku Sku { get; init; }
            public required ShipDate Sent { get; init; }
            public ShipDate? Delivered { get; init; }
            public Sku? BackupSku { get; init; }
            public required IReadOnlyList<Parcel> Parcels { get; init; }
            public List<Count>? Extras { get; init; }

            static partial void Validate(Shipment candidate, ValidationErrors errors)
            {
                if (candidate.Delivered is { } delivered && delivered < candidate.Sent)
                {
                    errors.Add(new TimeTravelError());
                }
            }
        }
        """;

    [Fact]
    public void Generated_sources_match_snapshots()
    {
        var (result, _) = GeneratorTest.Run(RepresentativeSource);
        var sources = result.For<DomainModelGenerator>().GeneratedSources;
        SnapshotAssert.MatchAll(sources.Select(source => (source.HintName, source.SourceText.ToString())));
    }

    [Fact]
    public void Generated_code_compiles_clean()
    {
        var (result, output) = GeneratorTest.Run(RepresentativeSource);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(2, result.For<DomainModelGenerator>().GeneratedSources.Length);
        Assert.Empty(output.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning));
    }

    [Fact]
    public void Generated_models_validate_at_runtime()
    {
        var (_, output) = GeneratorTest.Run(RepresentativeSource);
        var assembly = EmitAndLoad(output);

        var shipment = assembly.GetType("Snapshots.Shipment")!;
        var dtoType = assembly.GetType("Snapshots.Shipment+Dto")!;
        var parcelDtoType = assembly.GetType("Snapshots.Parcel+Dto")!;

        // The cross-field hook makes the constructor private.
        Assert.Empty(shipment.GetConstructors(BindingFlags.Public | BindingFlags.Instance));

        // Errors accumulate across properties with JSON paths, including into collections,
        // and the explicit [JsonPropertyName] is honored in paths.
        var badParcel = Activator.CreateInstance(parcelDtoType)!;
        SetProperty(badParcel, "Count", 0);
        var parcels = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(parcelDtoType))!;
        parcels.Add(badParcel);

        var dto = Activator.CreateInstance(dtoType)!;
        SetProperty(dto, "Sent", new DateOnly(2026, 3, 1));
        SetProperty(dto, "Parcels", parcels);

        var invalid = InvokeTryCreate(shipment, dtoType, dto);
        Assert.False((bool)GetProperty(invalid, "IsValid")!);
        var errors = (IEnumerable)GetProperty(invalid, "Errors")!;
        var paths = errors.Cast<object>()
            .Select(error => (string)GetProperty(error, "Path")!)
            .ToHashSet();
        var expectedPaths = new HashSet<string> { "shipmentSku", "parcels[0].sku", "parcels[0].count" };
        Assert.Equal(expectedPaths, paths);

        // A fully valid DTO round-trips, optional properties map null through, and the
        // cross-field hook rejects delivery before sending.
        SetProperty(dto, "Sku", "WIDGET");
        SetProperty(badParcel, "Sku", "BOLT");
        SetProperty(badParcel, "Count", 5);
        parcels[0] = badParcel;

        var valid = InvokeTryCreate(shipment, dtoType, dto);
        Assert.True((bool)GetProperty(valid, "IsValid")!);
        var model = GetProperty(valid, "Value")!;
        Assert.Null(GetProperty(model, "Delivered"));
        Assert.Null(GetProperty(model, "Extras"));

        SetProperty(dto, "Delivered", new DateOnly(2026, 2, 1));
        var timeTravel = InvokeTryCreate(shipment, dtoType, dto);
        Assert.False((bool)GetProperty(timeTravel, "IsValid")!);
        var crossField = ((IEnumerable)GetProperty(timeTravel, "Errors")!).Cast<object>().Single();
        Assert.Equal("", GetProperty(crossField, "Path"));
        var crossFieldError = (Validation.IDomainError)GetProperty(crossField, "Error")!;
        Assert.Equal("time_travel", crossFieldError.Code);
        Assert.Equal("TimeTravelError", crossFieldError.GetType().Name);
    }

    [Fact]
    public void Non_record_model_reports_TW1004()
    {
        var (result, _) = GeneratorTest.Run("""
            using Typewright.Validation;

            namespace Broken;

            [DomainModel]
            public sealed partial class NotARecord;
            """);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("TW1004", diagnostic.Id);
    }

    [Fact]
    public void Non_domain_properties_pass_through_unvalidated()
    {
        // Analyzer TW0004 warns about these; the generator copies them through the DTO
        // so the model still round-trips.
        var (result, output) = GeneratorTest.Run("""
            using Typewright.Validation;

            namespace Passthrough;

            [DomainModel]
            public sealed partial record Mixed
            {
                public required string PlainString { get; init; }

                [Raw]
                public int? Note { get; init; }
            }
            """);

        Assert.Empty(result.Diagnostics);
        var assembly = EmitAndLoad(output);

        var mixed = assembly.GetType("Passthrough.Mixed")!;
        var dtoType = assembly.GetType("Passthrough.Mixed+Dto")!;

        var missing = InvokeTryCreate(mixed, dtoType, Activator.CreateInstance(dtoType));
        Assert.False((bool)GetProperty(missing, "IsValid")!);
        var error = ((IEnumerable)GetProperty(missing, "Errors")!).Cast<object>().Single();
        Assert.Equal("plainString", GetProperty(error, "Path"));

        var dto = Activator.CreateInstance(dtoType)!;
        SetProperty(dto, "PlainString", "copied");
        SetProperty(dto, "Note", 7);
        var valid = InvokeTryCreate(mixed, dtoType, dto);
        var model = GetProperty(valid, "Value")!;
        Assert.Equal("copied", GetProperty(model, "PlainString"));
        Assert.Equal(7, GetProperty(model, "Note"));
    }

    private static object? InvokeTryCreate(Type model, Type dtoType, object? dto)
        => model.GetMethod("TryCreate", BindingFlags.Public | BindingFlags.Static, [dtoType])!
            .Invoke(null, [dto]);

    private static Assembly EmitAndLoad(Compilation compilation)
    {
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        return Assembly.Load(stream.ToArray());
    }

    private static void SetProperty(object instance, string name, object? value)
        => instance.GetType().GetProperty(name)!.SetValue(instance, value);

    private static object? GetProperty(object? instance, string name)
        => instance!.GetType().GetProperty(name)!.GetValue(instance);
}
