namespace Typewright.Validation;

/// <summary>
/// A domain field value: a <c>readonly record struct</c> wrapping exactly one raw value
/// (<see cref="string"/>, <see cref="int"/>, <see cref="DateOnly"/>, ...) whose invariants
/// hold for every instance produced through <see cref="TryCreate"/> or <see cref="Create"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="TryCreate"/> is the primitive: it never throws, so callers (in particular the
/// HTTP boundary) can accumulate every failure in a request. <see cref="Create"/> is sugar
/// that throws a single <see cref="DomainValidationException"/> carrying all errors.
/// </para>
/// <para>
/// Input is nullable so "missing" is an ordinary <c>required</c> validation error rather
/// than an <see cref="ArgumentNullException"/>; this lets a DTO with nullable raw fields
/// feed the domain type directly. Note that when <typeparamref name="TRaw"/> is a value
/// type, the interface signature is the plain raw type; implementations additionally
/// declare a <c>Nullable&lt;TRaw&gt;</c> overload so nullable DTO properties bind directly.
/// </para>
/// </remarks>
/// <typeparam name="TSelf">The implementing value type.</typeparam>
/// <typeparam name="TRaw">The raw type the value wraps.</typeparam>
public interface IDomainValue<TSelf, TRaw>
    where TSelf : struct, IDomainValue<TSelf, TRaw>
{
    /// <summary>The validated raw value.</summary>
    TRaw Value { get; }

    /// <summary>Validates <paramref name="value"/> and returns the value or all failures. Never throws.</summary>
    static abstract ValidationResult<TSelf> TryCreate(TRaw? value);

    /// <summary>Validates <paramref name="value"/> and returns the value, or throws <see cref="DomainValidationException"/>.</summary>
    static abstract TSelf Create(TRaw? value);
}
