using System.Text.Json.Serialization;
using Typewright.Validation;
using Typewright.Validation.Fluent;
using Typewright.Validation.Json;

namespace Billing.Fluent.Domain;

/// <summary>One line of a billing document.</summary>
/// <remarks>Fluent equivalent of Billing.Api's <c>[DomainModel]</c> LineItem. Validators are
/// namespace-qualified because the properties shadow the type names inside the record.</remarks>
[JsonConverter(typeof(DomainModelJsonConverter<LineItem, LineItem.Dto>))]
public sealed record LineItem : IDomainModel<LineItem, LineItem.Dto>
{
    public required ItemDescription Description { get; init; }

    public required Quantity Quantity { get; init; }

    public required UnitPrice UnitPrice { get; init; }

    /// <summary>The raw, serialization-friendly shape of a <see cref="LineItem"/>.</summary>
    public sealed record Dto
    {
        public string? Description { get; init; }

        public int? Quantity { get; init; }

        public decimal? UnitPrice { get; init; }
    }

    private static readonly DomainModelDefinition<LineItem, Dto> Definition = BuildDefinition();

    private static DomainModelDefinition<LineItem, Dto> BuildDefinition()
    {
        var builder = DomainModel.Define<LineItem, Dto>();
        var description = builder.Field(static dto => dto.Description, Billing.Fluent.Domain.ItemDescription.TryCreate);
        var quantity = builder.Field(static dto => dto.Quantity, Billing.Fluent.Domain.Quantity.TryCreate);
        var unitPrice = builder.Field(static dto => dto.UnitPrice, Billing.Fluent.Domain.UnitPrice.TryCreate);

        return builder.Construct(fields => new LineItem
        {
            Description = fields.Get(description),
            Quantity = fields.Get(quantity),
            UnitPrice = fields.Get(unitPrice),
        });
    }

    public static ValidationResult<LineItem> TryCreate(Dto? dto) => Definition.TryCreate(dto);

    public static LineItem Create(Dto? dto) => Definition.Create(dto);

    public static DomainTypeInfo DomainTypeInfo => Definition.TypeInfo;

    public Dto ToDto() => new()
    {
        Description = Description.Value,
        Quantity = Quantity.Value,
        UnitPrice = UnitPrice.Value,
    };
}
