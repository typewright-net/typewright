using System.Linq.Expressions;

namespace Typewright.Validation.Fluent;

/// <summary>
/// Entry point for fluently defining a domain model — the manual equivalent of
/// <c>[DomainModel]</c>, sharing the same runtime capabilities without the source
/// generator. Fields are declared with typed handles and assembled in a
/// <c>Construct</c> closure, so composition stays compiler-checked at any arity:
/// <code>
/// var builder = DomainModel.Define&lt;BillingDocument, BillingDocument.Dto&gt;();
/// var number = builder.Field(static dto =&gt; dto.InvoiceNumber, InvoiceNumber.TryCreate);
/// var items  = builder.Collection(static dto =&gt; dto.LineItems, LineItem.TryCreate);
/// var definition = builder.Construct(fields =&gt; new BillingDocument
/// {
///     InvoiceNumber = fields.Get(number),
///     LineItems = fields.Get(items),
/// });
/// </code>
/// </summary>
public static class DomainModel
{
    /// <summary>Starts a definition for <typeparamref name="TModel"/> built from <typeparamref name="TDto"/>.</summary>
    public static DomainModelBuilder<TModel, TDto> Define<TModel, TDto>()
        where TModel : class, IDomainModel<TModel, TDto>
        where TDto : class
        => new();
}

/// <summary>A typed token for one declared field, redeemed with <see cref="FieldValues.Get{TField}"/>.</summary>
public readonly struct FieldHandle<TField>
{
    internal FieldHandle(int index) => Index = index;

    internal int Index { get; }
}

/// <summary>The validated field values passed to a definition's <c>Construct</c> closure.</summary>
public sealed class FieldValues
{
    private readonly object?[] _values;

    internal FieldValues(object?[] values) => _values = values;

    /// <summary>The validated value for <paramref name="handle"/>.</summary>
    public TField Get<TField>(FieldHandle<TField> handle) => (TField)_values[handle.Index]!;
}

/// <summary>
/// Collects a domain model's fields, cross-field rules, and declared errors. Field
/// failures accumulate under their JSON paths before any cross-field rule runs, exactly
/// like generated models.
/// </summary>
public sealed class DomainModelBuilder<TModel, TDto>
    where TModel : class, IDomainModel<TModel, TDto>
    where TDto : class
{
    private readonly List<ModelFieldDefinition<TDto>> _fields = [];
    private readonly List<Action<TModel, ValidationErrors>> _hooks = [];
    private readonly HashSet<Type> _errorTypes = [typeof(RequiredError), typeof(JsonInvalidError)];

    internal DomainModelBuilder()
    {
    }

    /// <summary>
    /// A required field: the raw value from <paramref name="property"/> flows through
    /// <paramref name="field"/> (a domain value's or model's <c>TryCreate</c>), which
    /// reports <c>required</c> when the raw value is missing.
    /// </summary>
    public FieldHandle<TField> Field<TRawProp, TField>(
        Expression<Func<TDto, TRawProp>> property,
        Func<TRawProp, ValidationResult<TField>> field,
        string? jsonName = null)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(field);
        var (propertyName, name) = ResolveName(property, jsonName);
        var accessor = property.Compile();
        return AddField<TField>(
            new DomainFieldInfo
            {
                Name = name,
                PropertyName = propertyName,
                FieldType = typeof(TField),
                Required = true,
            },
            (dto, errors) =>
            {
                var result = field(accessor(dto));
                if (result.IsValid)
                {
                    return result.Value;
                }

                errors.Merge(name, result.Errors);
                return null;
            });
    }

    /// <summary>An optional domain value field: a missing raw value maps through as null.</summary>
    public FieldHandle<TField?> OptionalValue<TRawProp, TField>(
        Expression<Func<TDto, TRawProp>> property,
        Func<TRawProp, ValidationResult<TField>> field,
        string? jsonName = null)
        where TField : struct
        => AddOptional<TRawProp, TField, TField?>(property, field, jsonName);

    /// <summary>An optional nested domain model field: a missing raw value maps through as null.</summary>
    public FieldHandle<TField?> OptionalModel<TRawProp, TField>(
        Expression<Func<TDto, TRawProp>> property,
        Func<TRawProp, ValidationResult<TField>> field,
        string? jsonName = null)
        where TField : class
        => AddOptional<TRawProp, TField, TField?>(property, field, jsonName);

    /// <summary>
    /// A required collection of domain values or models: a missing list is a
    /// <c>required</c> error, and element failures are reported at <c>name[index]</c>.
    /// </summary>
    public FieldHandle<IReadOnlyList<TField>> Collection<TElementRaw, TField>(
        Expression<Func<TDto, List<TElementRaw>?>> property,
        Func<TElementRaw, ValidationResult<TField>> element,
        string? jsonName = null)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(element);
        var (propertyName, name) = ResolveName(property, jsonName);
        var accessor = property.Compile();
        return AddField<IReadOnlyList<TField>>(
            new DomainFieldInfo
            {
                Name = name,
                PropertyName = propertyName,
                FieldType = typeof(TField),
                Required = true,
                IsCollection = true,
            },
            (dto, errors) =>
            {
                var list = accessor(dto);
                if (list is null)
                {
                    errors.AddAt(name, new RequiredError());
                    return null;
                }

                return BindElements(list, element, name, errors);
            });
    }

    /// <summary>An optional collection: a missing list maps through as null.</summary>
    public FieldHandle<IReadOnlyList<TField>?> OptionalCollection<TElementRaw, TField>(
        Expression<Func<TDto, List<TElementRaw>?>> property,
        Func<TElementRaw, ValidationResult<TField>> element,
        string? jsonName = null)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(element);
        var (propertyName, name) = ResolveName(property, jsonName);
        var accessor = property.Compile();
        return AddField<IReadOnlyList<TField>?>(
            new DomainFieldInfo
            {
                Name = name,
                PropertyName = propertyName,
                FieldType = typeof(TField),
                Required = false,
                IsCollection = true,
            },
            (dto, errors) =>
            {
                var list = accessor(dto);
                return list is null ? null : BindElements(list, element, name, errors);
            });
    }

    /// <summary>
    /// A non-domain property copied between DTO and model without validation — the fluent
    /// <c>[Raw]</c>. With <paramref name="required"/>, a missing value is a
    /// <c>required</c> error.
    /// </summary>
    public FieldHandle<TProp> Raw<TProp>(
        Expression<Func<TDto, TProp>> property,
        bool required = false,
        string? jsonName = null)
    {
        ArgumentNullException.ThrowIfNull(property);
        var (propertyName, name) = ResolveName(property, jsonName);
        var accessor = property.Compile();
        return AddField<TProp>(
            new DomainFieldInfo
            {
                Name = name,
                PropertyName = propertyName,
                FieldType = typeof(TProp),
                Required = required,
            },
            (dto, errors) =>
            {
                var value = accessor(dto);
                if (required && value is null)
                {
                    errors.AddAt(name, new RequiredError());
                }

                return value;
            });
    }

    /// <summary>
    /// A cross-field rule declared by construction: runs on a fully field-valid candidate,
    /// and <typeparamref name="TError"/> joins the error set automatically.
    /// </summary>
    public DomainModelBuilder<TModel, TDto> Must<TError>(Func<TModel, bool> predicate, Func<TModel, TError> error)
        where TError : IDomainError
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(error);
        _errorTypes.Add(typeof(TError));
        _hooks.Add((model, errors) =>
        {
            if (!predicate(model))
            {
                errors.Add(error(model));
            }
        });
        return this;
    }

    /// <summary>
    /// A free-form cross-field hook, like the generated models' <c>Validate</c>. Its
    /// error types must be declared with <see cref="Reports{TError}"/> (or already be in
    /// the derived set); an undeclared report throws at validation time.
    /// </summary>
    public DomainModelBuilder<TModel, TDto> Validate(Action<TModel, ValidationErrors> hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        _hooks.Add(hook);
        return this;
    }

    /// <summary>Declares that a hook may report <typeparamref name="TError"/> — the fluent <c>[Reports&lt;TError&gt;]</c>.</summary>
    public DomainModelBuilder<TModel, TDto> Reports<TError>()
        where TError : IDomainError
    {
        _errorTypes.Add(typeof(TError));
        return this;
    }

    /// <summary>
    /// Finalizes the definition with the closure that assembles the model from its
    /// validated fields. The error set derives like a generated model's: binding errors,
    /// the union of the fields' sets (via <see cref="DomainTypeRegistry"/>), and the
    /// declared cross-field errors.
    /// </summary>
    public DomainModelDefinition<TModel, TDto> Construct(Func<FieldValues, TModel> constructor)
    {
        ArgumentNullException.ThrowIfNull(constructor);

        foreach (var field in _fields)
        {
            if (DomainTypeRegistry.Find(field.Info.FieldType) is { } fieldInfo)
            {
                _errorTypes.UnionWith(fieldInfo.ErrorTypes);
            }
        }

        var typeInfo = new DomainTypeInfo
        {
            Name = typeof(TModel).Name,
            DomainType = typeof(TModel),
            RawType = typeof(TDto),
            IsModel = true,
            Fields = _fields.Select(static field => field.Info).ToArray(),
            ErrorTypes = _errorTypes.OrderBy(static type => type.Name, StringComparer.Ordinal).ToArray(),
        };

        return new DomainModelDefinition<TModel, TDto>(
            _fields.ToArray(),
            constructor,
            _hooks.ToArray(),
            _errorTypes,
            typeInfo);
    }

    private FieldHandle<THandle> AddField<THandle>(DomainFieldInfo info, Func<TDto, ValidationErrors, object?> bind)
    {
        var index = _fields.Count;
        _fields.Add(new ModelFieldDefinition<TDto>(info, bind));
        return new FieldHandle<THandle>(index);
    }

    private FieldHandle<THandle> AddOptional<TRawProp, TField, THandle>(
        Expression<Func<TDto, TRawProp>> property,
        Func<TRawProp, ValidationResult<TField>> field,
        string? jsonName)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(field);
        var (propertyName, name) = ResolveName(property, jsonName);
        var accessor = property.Compile();
        return AddField<THandle>(
            new DomainFieldInfo
            {
                Name = name,
                PropertyName = propertyName,
                FieldType = typeof(TField),
                Required = false,
            },
            (dto, errors) =>
            {
                var raw = accessor(dto);
                if (raw is null)
                {
                    return null;
                }

                var result = field(raw);
                if (result.IsValid)
                {
                    return result.Value;
                }

                errors.Merge(name, result.Errors);
                return null;
            });
    }

    private static IReadOnlyList<TField> BindElements<TElementRaw, TField>(
        List<TElementRaw> list,
        Func<TElementRaw, ValidationResult<TField>> element,
        string name,
        ValidationErrors errors)
    {
        var values = new List<TField>(list.Count);
        for (var i = 0; i < list.Count; i++)
        {
            var result = element(list[i]);
            if (result.IsValid)
            {
                values.Add(result.Value);
            }
            else
            {
                errors.Merge(name + "[" + i + "]", result.Errors);
            }
        }

        return values;
    }

    private static (string PropertyName, string JsonName) ResolveName(LambdaExpression property, string? jsonName)
    {
        var body = property.Body;
        if (body is UnaryExpression { NodeType: ExpressionType.Convert } unary)
        {
            body = unary.Operand;
        }

        if (body is not MemberExpression member)
        {
            throw new ArgumentException(
                "The property selector must be a simple member access (dto => dto.Property).", nameof(property));
        }

        var name = member.Member.Name;
        return (name, jsonName ?? (name.Length > 0 ? char.ToLowerInvariant(name[0]) + name[1..] : name));
    }
}

internal sealed record ModelFieldDefinition<TDto>(
    DomainFieldInfo Info,
    Func<TDto, ValidationErrors, object?> Bind);
