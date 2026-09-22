namespace Typewright.Validation.Fluent;

/// <summary>
/// A fluently built, immutable definition of a domain value: normalization, rules, hooks,
/// declared error set, and descriptor. A hand-written domain value forwards its
/// <c>TryCreate</c>/<c>Create</c> to a static definition, giving the same behavior and
/// capabilities as an attribute-driven generated type without the source generator.
/// </summary>
public sealed class DomainValueDefinition<TSelf, TRaw>
    where TSelf : struct, IDomainValue<TSelf, TRaw>
{
    private readonly Func<TRaw, TSelf> _constructor;
    private readonly Func<TRaw, TRaw>[] _normalizers;
    private readonly Func<TRaw, IDomainError?>[] _rules;
    private readonly Action<TRaw, ValidationErrors>[] _hooks;
    private readonly HashSet<Type> _errorTypes;

    internal DomainValueDefinition(
        Func<TRaw, TSelf> constructor,
        Func<TRaw, TRaw>[] normalizers,
        Func<TRaw, IDomainError?>[] rules,
        Action<TRaw, ValidationErrors>[] hooks,
        HashSet<Type> errorTypes,
        DomainTypeInfo typeInfo)
    {
        _constructor = constructor;
        _normalizers = normalizers;
        _rules = rules;
        _hooks = hooks;
        _errorTypes = errorTypes;
        TypeInfo = typeInfo;
    }

    /// <summary>The reflection-free descriptor, equivalent to a generated type's <c>DomainTypeInfo</c>.</summary>
    public DomainTypeInfo TypeInfo { get; }

    /// <summary>
    /// Validates <paramref name="value"/> and returns the value or all failures. Never
    /// throws. Order matches generated types: null is <see cref="RequiredError"/>, then
    /// normalization, then every rule accumulates, then hooks run only when the rules
    /// passed.
    /// </summary>
    public ValidationResult<TSelf> TryCreate(TRaw? value)
    {
        if (value is null)
        {
            return ValidationResult<TSelf>.Failure(string.Empty, new RequiredError());
        }

        TRaw current = value;
        foreach (var normalize in _normalizers)
        {
            current = normalize(current);
        }

        var errors = new ValidationErrors();
        foreach (var rule in _rules)
        {
            if (rule(current) is { } error)
            {
                errors.Add(error);
            }
        }

        if (errors.Count == 0)
        {
            foreach (var hook in _hooks)
            {
                var before = errors.Count;
                hook(current, errors);
                EnsureDeclared(errors, before);
            }
        }

        return errors.Count > 0
            ? ValidationResult<TSelf>.Failure(errors)
            : ValidationResult<TSelf>.Success(_constructor(current));
    }

    /// <summary>Validates <paramref name="value"/> and returns the value, or throws <see cref="DomainValidationException"/>.</summary>
    public TSelf Create(TRaw? value) => TryCreate(value).OrThrow();

    private void EnsureDeclared(ValidationErrors errors, int from)
    {
        for (var i = from; i < errors.Count; i++)
        {
            var errorType = errors[i].Error.GetType();
            if (!_errorTypes.Contains(errorType))
            {
                throw new InvalidOperationException(
                    $"The Validate hook of '{typeof(TSelf).Name}' reported '{errorType.Name}', which is not "
                    + $"in the type's declared error set. Declare it with Reports<{errorType.Name}>() or use "
                    + $"Must<{errorType.Name}>(...) so the error set stays finite and discoverable.");
            }
        }
    }
}

/// <summary>
/// Raw-type-dependent conveniences: nullable-raw overloads for value-type raws, and
/// parsing for parsable raws (so a hand-written type's <see cref="IParsable{TSelf}"/>
/// members are one-line forwards).
/// </summary>
public static class DomainValueDefinitionExtensions
{
    /// <summary>Parses the raw form with the invariant culture by default, then validates.</summary>
    public static TSelf Parse<TSelf, TRaw>(
        this DomainValueDefinition<TSelf, TRaw> definition, string text, IFormatProvider? provider)
        where TSelf : struct, IDomainValue<TSelf, TRaw>
        where TRaw : IParsable<TRaw>
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(text);
        return definition.Create(TRaw.Parse(text, provider ?? System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>Parses the raw form, then validates; false when either step fails.</summary>
    public static bool TryParse<TSelf, TRaw>(
        this DomainValueDefinition<TSelf, TRaw> definition, string? text, IFormatProvider? provider, out TSelf result)
        where TSelf : struct, IDomainValue<TSelf, TRaw>
        where TRaw : IParsable<TRaw>
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (TRaw.TryParse(text, provider ?? System.Globalization.CultureInfo.InvariantCulture, out var raw))
        {
            var validation = definition.TryCreate(raw);
            if (validation.IsValid)
            {
                result = validation.Value;
                return true;
            }
        }

        result = default;
        return false;
    }

    /// <summary>Nullable-raw overload so nullable DTO properties bind directly; null is a <c>required</c> error.</summary>
    public static ValidationResult<TSelf> TryCreate<TSelf, TRaw>(
        this DomainValueDefinition<TSelf, TRaw> definition, TRaw? value)
        where TSelf : struct, IDomainValue<TSelf, TRaw>
        where TRaw : struct
    {
        ArgumentNullException.ThrowIfNull(definition);
        return value is null
            ? ValidationResult<TSelf>.Failure(string.Empty, new RequiredError())
            : definition.TryCreate(value.Value);
    }

    /// <summary>Nullable-raw overload; null throws with a <c>required</c> error.</summary>
    public static TSelf Create<TSelf, TRaw>(
        this DomainValueDefinition<TSelf, TRaw> definition, TRaw? value)
        where TSelf : struct, IDomainValue<TSelf, TRaw>
        where TRaw : struct
        => definition.TryCreate(value).OrThrow();
}
