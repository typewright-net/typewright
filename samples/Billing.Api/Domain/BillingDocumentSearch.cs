using System.Text.Json.Serialization;
using Typewright.Validation;
using Typewright.Validation.Json;

namespace Billing.Api.Domain;

// ── User-authored part. Once the generator exists this is all that remains, plus:
//
//   [DomainModel]
//   (properties as below)

/// <summary>A date-range search for the invoices of one account.</summary>
public sealed partial record BillingDocumentSearch
{
    // Cross-field rule; runs only after every field validated individually, never on a
    // partially valid candidate.
    static partial void Validate(BillingDocumentSearch candidate, ValidationErrors errors)
    {
        if (candidate.EndDate < candidate.BeginDate)
        {
            errors.Add("date_range.invalid", "The end date must be on or after the begin date.");
        }
    }
}

// ── Phase 0 hand-written reference for what the generator will emit. Because the model
// has a cross-field Validate hook, the constructor is private: instances can only be
// built through the factories, so the date-range rule always holds. (`with` expressions
// still bypass it; analyzer TW0002 will close that hole in phase 3.)
[JsonConverter(typeof(DomainModelJsonConverter<BillingDocumentSearch, BillingDocumentSearch.Dto>))]
public sealed partial record BillingDocumentSearch : IDomainModel<BillingDocumentSearch, BillingDocumentSearch.Dto>
{
    private BillingDocumentSearch()
    {
    }

    /// <summary>The first invoice date included in the search.</summary>
    public required InvoiceDate BeginDate { get; init; }

    /// <summary>The last invoice date included in the search.</summary>
    public required InvoiceDate EndDate { get; init; }

    /// <summary>The account whose invoices are searched.</summary>
    public required AccountNumber AccountNumber { get; init; }

    /// <summary>The raw, serialization-friendly shape of a <see cref="BillingDocumentSearch"/>.</summary>
    public sealed record Dto
    {
        /// <summary>Raw value for <see cref="BillingDocumentSearch.BeginDate"/>.</summary>
        public DateOnly? BeginDate { get; init; }

        /// <summary>Raw value for <see cref="BillingDocumentSearch.EndDate"/>.</summary>
        public DateOnly? EndDate { get; init; }

        /// <summary>Raw value for <see cref="BillingDocumentSearch.AccountNumber"/>.</summary>
        public string? AccountNumber { get; init; }
    }

    /// <inheritdoc cref="IDomainModel{TSelf, TDto}.TryCreate" />
    public static ValidationResult<BillingDocumentSearch> TryCreate(Dto? dto)
    {
        if (dto is null)
        {
            return ValidationResult<BillingDocumentSearch>.Failure(
                string.Empty, ValidationErrorCodes.Required, "A value is required.");
        }

        var errors = new ValidationErrors();

        var beginDateResult = InvoiceDate.TryCreate(dto.BeginDate);
        if (!beginDateResult.IsValid)
        {
            errors.Merge("beginDate", beginDateResult.Errors);
        }

        var endDateResult = InvoiceDate.TryCreate(dto.EndDate);
        if (!endDateResult.IsValid)
        {
            errors.Merge("endDate", endDateResult.Errors);
        }

        var accountNumberResult = AccountNumber.TryCreate(dto.AccountNumber);
        if (!accountNumberResult.IsValid)
        {
            errors.Merge("accountNumber", accountNumberResult.Errors);
        }

        if (errors.Count > 0)
        {
            return ValidationResult<BillingDocumentSearch>.Failure(errors);
        }

        return TryCreate(beginDateResult.Value, endDateResult.Value, accountNumberResult.Value);
    }

    /// <summary>Builds a search from already-valid fields, running the cross-field rule.</summary>
    public static ValidationResult<BillingDocumentSearch> TryCreate(
        InvoiceDate beginDate, InvoiceDate endDate, AccountNumber accountNumber)
    {
        var candidate = new BillingDocumentSearch
        {
            BeginDate = beginDate,
            EndDate = endDate,
            AccountNumber = accountNumber,
        };

        var errors = new ValidationErrors();
        Validate(candidate, errors);
        return errors.Count > 0
            ? ValidationResult<BillingDocumentSearch>.Failure(errors)
            : ValidationResult<BillingDocumentSearch>.Success(candidate);
    }

    /// <inheritdoc cref="IDomainModel{TSelf, TDto}.Create" />
    public static BillingDocumentSearch Create(Dto? dto) => TryCreate(dto).OrThrow();

    /// <summary>Builds a search from already-valid fields, throwing when the cross-field rule fails.</summary>
    public static BillingDocumentSearch Create(InvoiceDate beginDate, InvoiceDate endDate, AccountNumber accountNumber)
        => TryCreate(beginDate, endDate, accountNumber).OrThrow();

    static partial void Validate(BillingDocumentSearch candidate, ValidationErrors errors);

    /// <inheritdoc />
    public Dto ToDto() => new()
    {
        BeginDate = BeginDate.Value,
        EndDate = EndDate.Value,
        AccountNumber = AccountNumber.Value,
    };
}
