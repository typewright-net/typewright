using System.Reflection;
using System.Text.Json.Serialization;
using Typewright.Validation;
using Typewright.Validation.AspNetCore;
using Typewright.Validation.Json;

namespace Billing.Api.Domain;

// Phase 0 hand-written reference for what the generator will emit from:
//
//   [DomainModel]
//   public partial record BillingDocument
//   {
//       public required InvoiceNumber InvoiceNumber { get; init; }
//       public required InvoiceDate InvoiceDate { get; init; }
//       public required AccountNumber AccountNumber { get; init; }
//       public Memo? Memo { get; init; }
//       public required IReadOnlyList<LineItem> LineItems { get; init; }
//   }
//
// Nullability drives required-ness: the nullable Memo maps null through as null;
// every other property yields a `required` error when its raw value is missing.
// No cross-field rules, so the constructor stays public.

/// <summary>An invoice to be billed against an account.</summary>
[JsonConverter(typeof(DomainModelJsonConverter<BillingDocument, BillingDocument.Dto>))]
public sealed partial record BillingDocument
    : IDomainModel<BillingDocument, BillingDocument.Dto>, IBindableFromHttpContext<BillingDocument>
{
    /// <summary>The invoice's own number.</summary>
    public required InvoiceNumber InvoiceNumber { get; init; }

    /// <summary>The date the invoice was issued.</summary>
    public required InvoiceDate InvoiceDate { get; init; }

    /// <summary>The account being billed.</summary>
    public required AccountNumber AccountNumber { get; init; }

    /// <summary>An optional free-form note.</summary>
    public Memo? Memo { get; init; }

    /// <summary>The invoice lines.</summary>
    public required IReadOnlyList<LineItem> LineItems { get; init; }

    /// <summary>The raw, serialization-friendly shape of a <see cref="BillingDocument"/>.</summary>
    public sealed record Dto
    {
        /// <summary>Raw value for <see cref="BillingDocument.InvoiceNumber"/>.</summary>
        public string? InvoiceNumber { get; init; }

        /// <summary>Raw value for <see cref="BillingDocument.InvoiceDate"/>.</summary>
        public DateOnly? InvoiceDate { get; init; }

        /// <summary>Raw value for <see cref="BillingDocument.AccountNumber"/>.</summary>
        public string? AccountNumber { get; init; }

        /// <summary>Raw value for <see cref="BillingDocument.Memo"/>.</summary>
        public string? Memo { get; init; }

        /// <summary>Raw values for <see cref="BillingDocument.LineItems"/>.</summary>
        public List<LineItem.Dto>? LineItems { get; init; }
    }

    /// <inheritdoc cref="IDomainModel{TSelf, TDto}.TryCreate" />
    public static ValidationResult<BillingDocument> TryCreate(Dto? dto)
    {
        if (dto is null)
        {
            return ValidationResult<BillingDocument>.Failure(
                string.Empty, ValidationErrorCodes.Required, "A value is required.");
        }

        var errors = new ValidationErrors();

        var invoiceNumberResult = InvoiceNumber.TryCreate(dto.InvoiceNumber);
        if (!invoiceNumberResult.IsValid)
        {
            errors.Merge("invoiceNumber", invoiceNumberResult.Errors);
        }

        var invoiceDateResult = InvoiceDate.TryCreate(dto.InvoiceDate);
        if (!invoiceDateResult.IsValid)
        {
            errors.Merge("invoiceDate", invoiceDateResult.Errors);
        }

        var accountNumberResult = AccountNumber.TryCreate(dto.AccountNumber);
        if (!accountNumberResult.IsValid)
        {
            errors.Merge("accountNumber", accountNumberResult.Errors);
        }

        Memo? memo = null;
        if (dto.Memo is not null)
        {
            // Qualified because the Memo property shadows the type name in this scope; the
            // generator emits fully qualified names for the same reason.
            var memoResult = global::Billing.Api.Domain.Memo.TryCreate(dto.Memo);
            if (memoResult.IsValid)
            {
                memo = memoResult.Value;
            }
            else
            {
                errors.Merge("memo", memoResult.Errors);
            }
        }

        List<LineItem>? lineItems = null;
        if (dto.LineItems is null)
        {
            errors.AddAt("lineItems", ValidationErrorCodes.Required, "A value is required.");
        }
        else
        {
            lineItems = new List<LineItem>(dto.LineItems.Count);
            for (var i = 0; i < dto.LineItems.Count; i++)
            {
                var lineItemResult = LineItem.TryCreate(dto.LineItems[i]);
                if (lineItemResult.IsValid)
                {
                    lineItems.Add(lineItemResult.Value);
                }
                else
                {
                    errors.Merge($"lineItems[{i}]", lineItemResult.Errors);
                }
            }
        }

        if (errors.Count > 0)
        {
            return ValidationResult<BillingDocument>.Failure(errors);
        }

        return ValidationResult<BillingDocument>.Success(new BillingDocument
        {
            InvoiceNumber = invoiceNumberResult.Value,
            InvoiceDate = invoiceDateResult.Value,
            AccountNumber = accountNumberResult.Value,
            Memo = memo,
            LineItems = lineItems!,
        });
    }

    /// <inheritdoc cref="IDomainModel{TSelf, TDto}.Create" />
    public static BillingDocument Create(Dto? dto) => TryCreate(dto).OrThrow();

    /// <inheritdoc />
    public Dto ToDto() => new()
    {
        InvoiceNumber = InvoiceNumber.Value,
        InvoiceDate = InvoiceDate.Value,
        AccountNumber = AccountNumber.Value,
        Memo = Memo?.Value,
        LineItems = LineItems.Select(item => item.ToDto()).ToList(),
    };

    /// <summary>Binds from the JSON request body via the DTO shape, accumulating every error.</summary>
    public static ValueTask<BillingDocument?> BindAsync(HttpContext context, ParameterInfo parameter)
        => TypewrightBinding.BindDomainModelAsync<BillingDocument, Dto>(context, parameter);
}
