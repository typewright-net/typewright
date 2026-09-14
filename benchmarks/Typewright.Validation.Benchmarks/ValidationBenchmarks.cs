using BenchmarkDotNet.Attributes;
using Typewright.Validation;

namespace Typewright.Validation.Benchmarks;

/// <summary>
/// The DTO-to-domain transform, excluding JSON parsing: what every request pays after
/// deserialization.
/// </summary>
[MemoryDiagnoser]
public class ValidationBenchmarks
{
    private WideDocument.Dto _valid = null!;
    private WideDocument.Dto _invalid = null!;

    [GlobalSetup]
    public void Setup()
    {
        _valid = new WideDocument.Dto
        {
            Number = "D123456789",
            Account = "ACC-2001",
            Title = "Quarterly restock",
            Description = "Warehouse replenishment for Q3",
            Reference = "PO-99182",
            Origin = "Central",
            Units = 1200,
            Packages = 100,
            Pallets = 12,
            Reorders = 3,
            Net = 15_000.50m,
            Tax = 3_150.11m,
            Gross = 18_150.61m,
            Discount = 0m,
            Issued = new DateOnly(2026, 7, 1),
            Due = new DateOnly(2026, 8, 1),
            Shipped = new DateOnly(2026, 7, 3),
            Closed = new DateOnly(2026, 8, 2),
            Comment = "Deliver to dock 4",
            Contact = "s.ramirez",
        };

        // Half the fields invalid: the accumulate-everything worst case.
        _invalid = _valid with
        {
            Number = "X",
            Account = "",
            Title = "   ",
            Units = -1,
            Packages = 2_000_000,
            Net = -5m,
            Issued = null,
            Due = null,
        };
    }

    [Benchmark(Baseline = true)]
    public WideDocument ValidDocument() => WideDocument.TryCreate(_valid).Value;

    [Benchmark]
    public int InvalidDocument_AccumulatesAllErrors() => WideDocument.TryCreate(_invalid).Errors.Count;

    [Benchmark]
    public Account SingleNormalizedValue() => Account.TryCreate("  acc-2001  ").Value;
}
