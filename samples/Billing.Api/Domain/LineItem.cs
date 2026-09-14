using System.Text.Json.Serialization;
using Typewright.Validation;
using Typewright.Validation.Json;

namespace Billing.Api.Domain;

// Phase 0 hand-written reference for what the generator will emit from:
//
//   [DomainModel]
//   public partial record LineItem
//   {
//       public required ItemDescription Description { get; init; }
//       public required Quantity Quantity { get; init; }
//       public required UnitPrice UnitPrice { get; init; }
//   }
//
// No cross-field rules, so the constructor stays public: any combination of valid
// fields is a valid line item.

/// <summary>One line of a billing document.</summary>
[JsonConverter(typeof(DomainModelJsonConverter<LineItem, LineItem.Dto>))]
public sealed partial record LineItem : IDomainModel<LineItem, LineItem.Dto>
{
    /// <summary>What the line covers.</summary>
    public required ItemDescription Description { get; init; }

    /// <summary>How many units.</summary>
    public required Quantity Quantity { get; init; }

    /// <summary>The price per unit.</summary>
    public required UnitPrice UnitPrice { get; init; }

    /// <summary>The raw, serialization-friendly shape of a <see cref="LineItem"/>.</summary>
    public sealed record Dto
    {
        /// <summary>Raw value for <see cref="LineItem.Description"/>.</summary>
        public string? Description { get; init; }

        /// <summary>Raw value for <see cref="LineItem.Quantity"/>.</summary>
        public int? Quantity { get; init; }

        /// <summary>Raw value for <see cref="LineItem.UnitPrice"/>.</summary>
        public decimal? UnitPrice { get; init; }
    }

    /// <inheritdoc cref="IDomainModel{TSelf, TDto}.TryCreate" />
    public static ValidationResult<LineItem> TryCreate(Dto? dto)
    {
        if (dto is null)
        {
            return ValidationResult<LineItem>.Failure(
                string.Empty, ValidationErrorCodes.Required, "A value is required.");
        }

        var errors = new ValidationErrors();

        var descriptionResult = ItemDescription.TryCreate(dto.Description);
        if (!descriptionResult.IsValid)
        {
            errors.Merge("description", descriptionResult.Errors);
        }

        var quantityResult = Quantity.TryCreate(dto.Quantity);
        if (!quantityResult.IsValid)
        {
            errors.Merge("quantity", quantityResult.Errors);
        }

        var unitPriceResult = UnitPrice.TryCreate(dto.UnitPrice);
        if (!unitPriceResult.IsValid)
        {
            errors.Merge("unitPrice", unitPriceResult.Errors);
        }

        if (errors.Count > 0)
        {
            return ValidationResult<LineItem>.Failure(errors);
        }

        return ValidationResult<LineItem>.Success(new LineItem
        {
            Description = descriptionResult.Value,
            Quantity = quantityResult.Value,
            UnitPrice = unitPriceResult.Value,
        });
    }

    /// <inheritdoc cref="IDomainModel{TSelf, TDto}.Create" />
    public static LineItem Create(Dto? dto) => TryCreate(dto).OrThrow();

    /// <inheritdoc />
    public Dto ToDto() => new()
    {
        Description = Description.Value,
        Quantity = Quantity.Value,
        UnitPrice = UnitPrice.Value,
    };
}
