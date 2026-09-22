namespace Typewright.Validation.Fluent;

/// <summary>
/// A fluently built, immutable definition of a domain model: fields, cross-field rules,
/// declared error set, and descriptor. A hand-written model forwards its
/// <c>TryCreate</c>/<c>Create</c> to a static definition, giving the same behavior and
/// capabilities as an attribute-driven generated model without the source generator.
/// </summary>
public sealed class DomainModelDefinition<TModel, TDto>
    where TModel : class, IDomainModel<TModel, TDto>
    where TDto : class
{
    private readonly ModelFieldDefinition<TDto>[] _fields;
    private readonly Func<FieldValues, TModel> _constructor;
    private readonly Action<TModel, ValidationErrors>[] _hooks;
    private readonly HashSet<Type> _errorTypes;

    internal DomainModelDefinition(
        ModelFieldDefinition<TDto>[] fields,
        Func<FieldValues, TModel> constructor,
        Action<TModel, ValidationErrors>[] hooks,
        HashSet<Type> errorTypes,
        DomainTypeInfo typeInfo)
    {
        _fields = fields;
        _constructor = constructor;
        _hooks = hooks;
        _errorTypes = errorTypes;
        TypeInfo = typeInfo;
    }

    /// <summary>The reflection-free descriptor, equivalent to a generated model's <c>DomainTypeInfo</c>.</summary>
    public DomainTypeInfo TypeInfo { get; }

    /// <summary>
    /// Validates <paramref name="dto"/> and returns the model or all failures, accumulated
    /// across every field under its JSON path before any cross-field rule runs. Never
    /// throws.
    /// </summary>
    public ValidationResult<TModel> TryCreate(TDto? dto)
    {
        if (dto is null)
        {
            return ValidationResult<TModel>.Failure(string.Empty, new RequiredError());
        }

        var errors = new ValidationErrors();
        var values = new object?[_fields.Length];
        for (var i = 0; i < _fields.Length; i++)
        {
            values[i] = _fields[i].Bind(dto, errors);
        }

        if (errors.Count > 0)
        {
            return ValidationResult<TModel>.Failure(errors);
        }

        return Check(_constructor(new FieldValues(values)));
    }

    /// <summary>Validates <paramref name="dto"/> and returns the model, or throws <see cref="DomainValidationException"/>.</summary>
    public TModel Create(TDto? dto) => TryCreate(dto).OrThrow();

    /// <summary>
    /// Runs only the cross-field rules on an already-built candidate — what a typed
    /// factory over valid fields uses, mirroring the generated factories on models with a
    /// cross-field hook.
    /// </summary>
    public ValidationResult<TModel> Check(TModel candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var errors = new ValidationErrors();
        foreach (var hook in _hooks)
        {
            var before = errors.Count;
            hook(candidate, errors);
            EnsureDeclared(errors, before);
        }

        return errors.Count > 0
            ? ValidationResult<TModel>.Failure(errors)
            : ValidationResult<TModel>.Success(candidate);
    }

    private void EnsureDeclared(ValidationErrors errors, int from)
    {
        for (var i = from; i < errors.Count; i++)
        {
            var errorType = errors[i].Error.GetType();
            if (!_errorTypes.Contains(errorType))
            {
                throw new InvalidOperationException(
                    $"The Validate hook of '{typeof(TModel).Name}' reported '{errorType.Name}', which is not "
                    + $"in the model's error set. Declare it with Reports<{errorType.Name}>() or use "
                    + $"Must<{errorType.Name}>(...) so the error set stays finite and discoverable.");
            }
        }
    }
}
