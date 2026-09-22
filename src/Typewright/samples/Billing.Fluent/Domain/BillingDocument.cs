using System.Reflection;
using System.Text.Json.Serialization;
using Typewright.Validation;
using Typewright.Validation.AspNetCore;
using Typewright.Validation.Fluent;
using Typewright.Validation.Json;

namespace Billing.Fluent.Domain;

/// <summary>An invoice to be billed against an account.</summary>
/// <remarks>Fluent equivalent of Billing.Api's <c>[DomainModel]</c> BillingDocument:
/// required-ness, the optional memo, and the nested collection all declared through the
/// builder, with the same JSON paths and error accumulation.</remarks>
[JsonConverter(typeof(DomainModelJsonConverter<BillingDocument, BillingDocument.Dto>))]
public sealed record BillingDocument
    : IDomainModel<BillingDocument, BillingDocument.Dto>, IBindableFromHttpContext<BillingDocument>
{
    public required InvoiceNumber InvoiceNumber { get; init; }

    public required InvoiceDate InvoiceDate { get; init; }

    public required AccountNumber AccountNumber { get; init; }

    public Memo? Memo { get; init; }

    public required IReadOnlyList<LineItem> LineItems { get; init; }

    /// <summary>The raw, serialization-friendly shape of a <see cref="BillingDocument"/>.</summary>
    public sealed record Dto
    {
        public string? InvoiceNumber { get; init; }

        public DateOnly? InvoiceDate { get; init; }

        public string? AccountNumber { get; init; }

        public string? Memo { get; init; }

        public List<LineItem.Dto?>? LineItems { get; init; }
    }

    private static readonly DomainModelDefinition<BillingDocument, Dto> Definition = BuildDefinition();

    private static DomainModelDefinition<BillingDocument, Dto> BuildDefinition()
    {
        var builder = DomainModel.Define<BillingDocument, Dto>();
        var invoiceNumber = builder.Field(static dto => dto.InvoiceNumber, Billing.Fluent.Domain.InvoiceNumber.TryCreate);
        var invoiceDate = builder.Field(static dto => dto.InvoiceDate, Billing.Fluent.Domain.InvoiceDate.TryCreate);
        var accountNumber = builder.Field(static dto => dto.AccountNumber, Billing.Fluent.Domain.AccountNumber.TryCreate);
        var memo = builder.OptionalValue(static dto => dto.Memo, Billing.Fluent.Domain.Memo.TryCreate);
        var lineItems = builder.Collection(static dto => dto.LineItems, LineItem.TryCreate);

        return builder.Construct(fields => new BillingDocument
        {
            InvoiceNumber = fields.Get(invoiceNumber),
            InvoiceDate = fields.Get(invoiceDate),
            AccountNumber = fields.Get(accountNumber),
            Memo = fields.Get(memo),
            LineItems = fields.Get(lineItems),
        });
    }

    public static ValidationResult<BillingDocument> TryCreate(Dto? dto) => Definition.TryCreate(dto);

    public static BillingDocument Create(Dto? dto) => Definition.Create(dto);

    public static DomainTypeInfo DomainTypeInfo => Definition.TypeInfo;

    public Dto ToDto() => new()
    {
        InvoiceNumber = InvoiceNumber.Value,
        InvoiceDate = InvoiceDate.Value,
        AccountNumber = AccountNumber.Value,
        Memo = Memo?.Value,
        LineItems = LineItems.Select(item => (LineItem.Dto?)item.ToDto()).ToList(),
    };

    public static ValueTask<BillingDocument?> BindAsync(HttpContext context, ParameterInfo parameter)
        => TypewrightBinding.BindDomainModelAsync<BillingDocument, Dto>(context, parameter);
}
