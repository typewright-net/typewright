using Typewright.Validation;

namespace Typewright.Validation.Benchmarks;

// A 20-field document exercising every rule kind, per the plan's phase 5 target:
// bind + validate well under a microsecond, excluding JSON parsing.

[DomainValue<string>]
[Pattern(@"^D\d{9}$")]
public readonly partial record struct DocumentNumber;

[DomainValue<string>]
[Length(1, 64), Normalize(Trim = true, Case = Casing.Upper)]
public readonly partial record struct Account;

[DomainValue<string>]
[Length(1, 128), NotEmpty]
public readonly partial record struct Text;

[DomainValue<int>]
[Range(0, 1_000_000)]
public readonly partial record struct Count;

[DomainValue<decimal>]
[Range(0, 1_000_000)]
public readonly partial record struct Amount;

[DomainValue<DateOnly>(Comparable = true)]
public readonly partial record struct Day;

/// <summary>Twenty domain-typed fields.</summary>
[DomainModel]
public sealed partial record WideDocument
{
    public required DocumentNumber Number { get; init; }
    public required Account Account { get; init; }
    public required Text Title { get; init; }
    public required Text Description { get; init; }
    public required Text Reference { get; init; }
    public required Text Origin { get; init; }
    public required Count Units { get; init; }
    public required Count Packages { get; init; }
    public required Count Pallets { get; init; }
    public required Count Reorders { get; init; }
    public required Amount Net { get; init; }
    public required Amount Tax { get; init; }
    public required Amount Gross { get; init; }
    public required Amount Discount { get; init; }
    public required Day Issued { get; init; }
    public required Day Due { get; init; }
    public required Day Shipped { get; init; }
    public required Day Closed { get; init; }
    public Text? Comment { get; init; }
    public Text? Contact { get; init; }
}
