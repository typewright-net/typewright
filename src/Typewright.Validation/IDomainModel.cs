namespace Typewright.Validation;

/// <summary>
/// A domain model: a record composed of domain values, other domain models, and collections
/// of them. It is constructed from its serialization-friendly DTO shape, whose properties
/// are the raw types, all nullable; validation accumulates every failure across all
/// properties before any cross-field rule runs.
/// </summary>
/// <typeparam name="TSelf">The implementing model type.</typeparam>
/// <typeparam name="TDto">The raw, serialization-friendly shape of the model.</typeparam>
public interface IDomainModel<TSelf, TDto>
    where TSelf : IDomainModel<TSelf, TDto>
    where TDto : class
{
    /// <summary>Validates <paramref name="dto"/> and returns the model or all failures. Never throws.</summary>
    static abstract ValidationResult<TSelf> TryCreate(TDto? dto);

    /// <summary>Validates <paramref name="dto"/> and returns the model, or throws <see cref="DomainValidationException"/>.</summary>
    static abstract TSelf Create(TDto? dto);

    /// <summary>Converts the model back to its raw DTO shape.</summary>
    TDto ToDto();
}
