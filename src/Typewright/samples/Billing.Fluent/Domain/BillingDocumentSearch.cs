using System.Text.Json.Serialization;
using Typewright.Validation;
using Typewright.Validation.Fluent;
using Typewright.Validation.Json;

namespace Billing.Fluent.Domain;

/// <summary>The search range ends before it begins. Code <c>date_range</c>.</summary>
public sealed record DateRangeError(DateOnly Begin, DateOnly End) : IDomainError
{
    public string DefaultMessage => "The end date must be on or after the begin date.";
}

/// <summary>A date-range search for the invoices of one account.</summary>
/// <remarks>Fluent equivalent of Billing.Api's cross-field model: the private constructor
/// keeps external construction out, the <c>Must</c> rule declares
/// <see cref="DateRangeError"/> by construction, and the typed factory runs the same rule
/// through <c>Definition.Check</c>.</remarks>
[JsonConverter(typeof(DomainModelJsonConverter<BillingDocumentSearch, BillingDocumentSearch.Dto>))]
public sealed record BillingDocumentSearch : IDomainModel<BillingDocumentSearch, BillingDocumentSearch.Dto>
{
    private BillingDocumentSearch()
    {
    }

    public required InvoiceDate BeginDate { get; init; }

    public required InvoiceDate EndDate { get; init; }

    public required AccountNumber AccountNumber { get; init; }

    /// <summary>The raw, serialization-friendly shape of a <see cref="BillingDocumentSearch"/>.</summary>
    public sealed record Dto
    {
        public DateOnly? BeginDate { get; init; }

        public DateOnly? EndDate { get; init; }

        public string? AccountNumber { get; init; }
    }

    private static readonly DomainModelDefinition<BillingDocumentSearch, Dto> Definition = BuildDefinition();

    private static DomainModelDefinition<BillingDocumentSearch, Dto> BuildDefinition()
    {
        var builder = DomainModel.Define<BillingDocumentSearch, Dto>();
        var beginDate = builder.Field(static dto => dto.BeginDate, InvoiceDate.TryCreate);
        var endDate = builder.Field(static dto => dto.EndDate, InvoiceDate.TryCreate);
        var accountNumber = builder.Field(static dto => dto.AccountNumber, Billing.Fluent.Domain.AccountNumber.TryCreate);

        return builder
            .Must(
                static search => search.EndDate >= search.BeginDate,
                static search => new DateRangeError(search.BeginDate.Value, search.EndDate.Value))
            .Construct(fields => new BillingDocumentSearch
            {
                BeginDate = fields.Get(beginDate),
                EndDate = fields.Get(endDate),
                AccountNumber = fields.Get(accountNumber),
            });
    }

    public static ValidationResult<BillingDocumentSearch> TryCreate(Dto? dto) => Definition.TryCreate(dto);

    public static BillingDocumentSearch Create(Dto? dto) => Definition.Create(dto);

    /// <summary>Builds a search from already-valid fields, running the cross-field rule.</summary>
    public static ValidationResult<BillingDocumentSearch> TryCreate(
        InvoiceDate beginDate, InvoiceDate endDate, AccountNumber accountNumber)
        => Definition.Check(new BillingDocumentSearch
        {
            BeginDate = beginDate,
            EndDate = endDate,
            AccountNumber = accountNumber,
        });

    /// <summary>Builds a search from already-valid fields, throwing when the cross-field rule fails.</summary>
    public static BillingDocumentSearch Create(InvoiceDate beginDate, InvoiceDate endDate, AccountNumber accountNumber)
        => TryCreate(beginDate, endDate, accountNumber).OrThrow();

    public static DomainTypeInfo DomainTypeInfo => Definition.TypeInfo;

    public Dto ToDto() => new()
    {
        BeginDate = BeginDate.Value,
        EndDate = EndDate.Value,
        AccountNumber = AccountNumber.Value,
    };
}
