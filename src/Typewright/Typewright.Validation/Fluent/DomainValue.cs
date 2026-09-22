using System.Globalization;
using System.Text.RegularExpressions;

namespace Typewright.Validation.Fluent;

/// <summary>
/// Entry point for fluently defining a domain value — the manual equivalent of
/// <c>[DomainValue&lt;TRaw&gt;]</c> plus rule attributes, sharing the same runtime
/// capabilities (error types, descriptors, binding, JSON) without the source generator:
/// <code>
/// private static readonly DomainValueDefinition&lt;AccountNumber, string&gt; Definition =
///     DomainValue.Define&lt;AccountNumber, string&gt;(static value =&gt; new(value))
///         .Normalize(trim: true, casing: Casing.Upper)
///         .Length(1, 64)
///         .Must(Luhn.IsValid, static _ =&gt; new ChecksumError())
///         .Build();
/// </code>
/// </summary>
public static class DomainValue
{
    /// <summary>Starts a definition; <paramref name="constructor"/> wraps a validated raw value.</summary>
    public static DomainValueBuilder<TSelf, TRaw> Define<TSelf, TRaw>(Func<TRaw, TSelf> constructor)
        where TSelf : struct, IDomainValue<TSelf, TRaw>
    {
        ArgumentNullException.ThrowIfNull(constructor);
        return new DomainValueBuilder<TSelf, TRaw>(constructor);
    }
}

/// <summary>
/// Collects a domain value's normalization, rules, hooks, and declared errors. Rules run
/// in declaration order and accumulate; hooks run only when every rule passed, exactly
/// like generated custom <c>Validate</c> hooks.
/// </summary>
public sealed class DomainValueBuilder<TSelf, TRaw>
    where TSelf : struct, IDomainValue<TSelf, TRaw>
{
    private readonly Func<TRaw, TSelf> _constructor;
    private readonly List<Func<TRaw, TRaw>> _normalizers = [];
    private readonly List<Func<TRaw, IDomainError?>> _rules = [];
    private readonly List<DomainRuleInfo> _ruleInfos = [];
    private readonly List<Action<TRaw, ValidationErrors>> _hooks = [];
    private readonly HashSet<Type> _errorTypes = [typeof(RequiredError)];

    internal DomainValueBuilder(Func<TRaw, TSelf> constructor) => _constructor = constructor;

    internal void AddNormalizer(Func<TRaw, TRaw> normalizer) => _normalizers.Add(normalizer);

    internal void AddRule(Func<TRaw, IDomainError?> check, Type errorType, DomainRuleInfo? info)
    {
        _rules.Add(check);
        _errorTypes.Add(errorType);
        if (info is not null)
        {
            _ruleInfos.Add(info);
        }
    }

    /// <summary>
    /// A custom rule declared by construction: when <paramref name="predicate"/> fails,
    /// <paramref name="error"/> is reported, and <typeparamref name="TError"/> joins the
    /// error set automatically. Runs with the hooks, after every rule passed.
    /// </summary>
    public DomainValueBuilder<TSelf, TRaw> Must<TError>(Func<TRaw, bool> predicate, Func<TRaw, TError> error)
        where TError : IDomainError
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(error);
        _errorTypes.Add(typeof(TError));
        _hooks.Add((value, errors) =>
        {
            if (!predicate(value))
            {
                errors.Add(error(value));
            }
        });
        return this;
    }

    /// <summary>
    /// A free-form hook, like the generated types' <c>static partial void Validate</c>.
    /// Every error type it may report must be declared with <see cref="Reports{TError}"/>
    /// (or be rule-implied); an undeclared report throws at validation time — the fluent
    /// equivalent of analyzer TW0005.
    /// </summary>
    public DomainValueBuilder<TSelf, TRaw> Validate(Action<TRaw, ValidationErrors> hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        _hooks.Add(hook);
        return this;
    }

    /// <summary>Declares that a hook may report <typeparamref name="TError"/> — the fluent <c>[Reports&lt;TError&gt;]</c>.</summary>
    public DomainValueBuilder<TSelf, TRaw> Reports<TError>()
        where TError : IDomainError
    {
        _errorTypes.Add(typeof(TError));
        return this;
    }

    /// <summary>Finalizes the immutable definition and its descriptor.</summary>
    public DomainValueDefinition<TSelf, TRaw> Build()
    {
        if (typeof(TRaw) != typeof(string) && RawIsParsable())
        {
            _errorTypes.Add(typeof(FormatError));
        }

        var typeInfo = new DomainTypeInfo
        {
            Name = typeof(TSelf).Name,
            DomainType = typeof(TSelf),
            RawType = typeof(TRaw),
            Rules = _ruleInfos.ToArray(),
            ErrorTypes = _errorTypes.OrderBy(static type => type.Name, StringComparer.Ordinal).ToArray(),
        };

        return new DomainValueDefinition<TSelf, TRaw>(
            _constructor,
            _normalizers.ToArray(),
            _rules.ToArray(),
            _hooks.ToArray(),
            _errorTypes,
            typeInfo);
    }

    private static bool RawIsParsable()
        => typeof(TRaw).GetInterfaces().Any(static candidate =>
            candidate.IsGenericType
            && candidate.GetGenericTypeDefinition() == typeof(IParsable<>)
            && candidate.GetGenericArguments()[0] == typeof(TRaw));
}

/// <summary>The built-in rules, constrained to the raw types they apply to — the fluent
/// counterpart of the rule attributes, with the compiler standing in for TW1002.</summary>
public static class DomainValueBuilderExtensions
{
    /// <summary>Normalizes before any rule runs; the normalized form is what the value stores. The fluent <c>[Normalize]</c>.</summary>
    public static DomainValueBuilder<TSelf, string> Normalize<TSelf>(
        this DomainValueBuilder<TSelf, string> builder, bool trim = false, Casing casing = Casing.None)
        where TSelf : struct, IDomainValue<TSelf, string>
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (trim)
        {
            builder.AddNormalizer(static value => value.Trim());
        }

        if (casing == Casing.Upper)
        {
            builder.AddNormalizer(static value => value.ToUpperInvariant());
        }
        else if (casing == Casing.Lower)
        {
            builder.AddNormalizer(static value => value.ToLowerInvariant());
        }

        return builder;
    }

    /// <summary>Requires the whole value to match a regular expression. The fluent <c>[Pattern]</c>.</summary>
    public static DomainValueBuilder<TSelf, string> Pattern<TSelf>(
        this DomainValueBuilder<TSelf, string> builder, string pattern)
        where TSelf : struct, IDomainValue<TSelf, string>
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(pattern);
        var regex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);
        builder.AddRule(
            value => regex.IsMatch(value) ? null : new PatternError(pattern),
            typeof(PatternError),
            new DomainRuleInfo { Kind = "pattern", Pattern = pattern });
        return builder;
    }

    /// <summary>Requires the length to fall within an inclusive range. The fluent <c>[Length]</c>.</summary>
    public static DomainValueBuilder<TSelf, string> Length<TSelf>(
        this DomainValueBuilder<TSelf, string> builder, int min, int max)
        where TSelf : struct, IDomainValue<TSelf, string>
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddRule(
            value => value.Length < min || value.Length > max ? new LengthError(min, max, value.Length) : null,
            typeof(LengthError),
            new DomainRuleInfo { Kind = "length", MinLength = min, MaxLength = max });
        return builder;
    }

    /// <summary>Requires at least one non-whitespace character. The fluent <c>[NotEmpty]</c>.</summary>
    public static DomainValueBuilder<TSelf, string> NotEmpty<TSelf>(
        this DomainValueBuilder<TSelf, string> builder)
        where TSelf : struct, IDomainValue<TSelf, string>
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddRule(
            static value => string.IsNullOrWhiteSpace(value) ? new NotEmptyError() : null,
            typeof(NotEmptyError),
            new DomainRuleInfo { Kind = "not_empty" });
        return builder;
    }

    /// <summary>Requires the value to fall within an inclusive range. The fluent <c>[Range]</c>.</summary>
    public static DomainValueBuilder<TSelf, TRaw> Range<TSelf, TRaw>(
        this DomainValueBuilder<TSelf, TRaw> builder, TRaw min, TRaw max)
        where TSelf : struct, IDomainValue<TSelf, TRaw>
        where TRaw : IComparable<TRaw>
    {
        ArgumentNullException.ThrowIfNull(builder);
        var minText = InvariantText(min);
        var maxText = InvariantText(max);
        builder.AddRule(
            value => value.CompareTo(min) < 0 || value.CompareTo(max) > 0
                ? new RangeError(minText, maxText, InvariantText(value))
                : null,
            typeof(RangeError),
            new DomainRuleInfo { Kind = "range", Minimum = TryToDecimal(min), Maximum = TryToDecimal(max) });
        return builder;
    }

    private static string InvariantText<TRaw>(TRaw value)
        => value is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.InvariantCulture)
            : value?.ToString() ?? "";

    private static decimal? TryToDecimal<TRaw>(TRaw value)
        => value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal
            ? Convert.ToDecimal(value, CultureInfo.InvariantCulture)
            : null;
}
