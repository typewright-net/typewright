using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text.Json.Serialization;
using Typewright.Validation;
using Typewright.Validation.AspNetCore;
using Typewright.Validation.Json;

namespace Billing.Api.Domain;

// Phase 0 hand-written reference for what the generator will emit from:
//
//   [DomainValue<DateOnly>(Comparable = true)]
//   public readonly partial record struct InvoiceDate;

/// <summary>The date an invoice was issued.</summary>
[JsonConverter(typeof(DomainValueJsonConverter<InvoiceDate, DateOnly>))]
public readonly partial record struct InvoiceDate
    : IDomainValue<InvoiceDate, DateOnly>, ISpanParsable<InvoiceDate>, IComparable<InvoiceDate>
{
    private readonly DateOnly _value;

    private InvoiceDate(DateOnly value) => _value = value;

    /// <summary>The validated raw value.</summary>
    public DateOnly Value => _value;

    /// <inheritdoc cref="IDomainValue{TSelf, TRaw}.TryCreate" />
    public static ValidationResult<InvoiceDate> TryCreate(DateOnly value)
        => ValidationResult<InvoiceDate>.Success(new InvoiceDate(value));

    /// <summary>Nullable-raw overload so nullable DTO properties bind directly; null is a <c>required</c> error.</summary>
    public static ValidationResult<InvoiceDate> TryCreate(DateOnly? value)
        => value is null
            ? ValidationResult<InvoiceDate>.Failure(string.Empty, ValidationErrorCodes.Required, "A value is required.")
            : TryCreate(value.Value);

    /// <inheritdoc cref="IDomainValue{TSelf, TRaw}.Create" />
    public static InvoiceDate Create(DateOnly value) => TryCreate(value).OrThrow();

    /// <summary>Nullable-raw overload; null throws with a <c>required</c> error.</summary>
    public static InvoiceDate Create(DateOnly? value) => TryCreate(value).OrThrow();

    /// <summary>Unwraps the raw value. Explicit so the domain type does not silently decay.</summary>
    public static explicit operator DateOnly(InvoiceDate value) => value.Value;

    /// <inheritdoc />
    public override string ToString() => _value.ToString("O", CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public int CompareTo(InvoiceDate other) => _value.CompareTo(other._value);

    /// <summary>Compares by the raw value.</summary>
    public static bool operator <(InvoiceDate left, InvoiceDate right) => left._value < right._value;

    /// <summary>Compares by the raw value.</summary>
    public static bool operator >(InvoiceDate left, InvoiceDate right) => left._value > right._value;

    /// <summary>Compares by the raw value.</summary>
    public static bool operator <=(InvoiceDate left, InvoiceDate right) => left._value <= right._value;

    /// <summary>Compares by the raw value.</summary>
    public static bool operator >=(InvoiceDate left, InvoiceDate right) => left._value >= right._value;

    /// <inheritdoc />
    public static InvoiceDate Parse(string s, IFormatProvider? provider)
        => Create(DateOnly.Parse(s, provider ?? CultureInfo.InvariantCulture));

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out InvoiceDate result)
    {
        if (DateOnly.TryParse(s, provider ?? CultureInfo.InvariantCulture, out var raw))
        {
            var validation = TryCreate(raw);
            result = validation.IsValid ? validation.Value : default;
            return validation.IsValid;
        }

        result = default;
        return false;
    }

    /// <inheritdoc />
    public static InvoiceDate Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        => Create(DateOnly.Parse(s, provider ?? CultureInfo.InvariantCulture));

    /// <inheritdoc />
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out InvoiceDate result)
        => TryParse(s.ToString(), provider, out result);

    /// <summary>Binds from route, query, or header; a failed bind throws with errors scoped to the parameter name.</summary>
    public static ValueTask<InvoiceDate?> BindAsync(HttpContext context, ParameterInfo parameter)
        => TypewrightBinding.BindDomainValueAsync<InvoiceDate, DateOnly>(context, parameter);
}
