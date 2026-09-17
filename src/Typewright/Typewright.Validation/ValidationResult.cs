using System.Diagnostics.CodeAnalysis;

namespace Typewright.Validation;

/// <summary>
/// The outcome of validating raw input: either a valid <typeparamref name="T"/> or the
/// accumulated <see cref="ValidationErrors"/>, never both.
/// </summary>
public readonly struct ValidationResult<T>
{
    private readonly bool _isValid;
    private readonly T _value;
    private readonly ValidationErrors? _errors;

    private ValidationResult(bool isValid, T value, ValidationErrors? errors)
    {
        _isValid = isValid;
        _value = value;
        _errors = errors;
    }

    /// <summary>Whether validation succeeded and <see cref="Value"/> is available.</summary>
    public bool IsValid => _isValid;

    /// <summary>The validated value. Throws if the result is not valid.</summary>
    public T Value => _isValid
        ? _value
        : throw new InvalidOperationException(
            "The validation result is not valid; check IsValid before reading Value, or use OrThrow().");

    /// <summary>The validation failures. Throws if the result is valid.</summary>
    public ValidationErrors Errors => _errors
        ?? throw new InvalidOperationException(_isValid
            ? "A valid result has no errors."
            : "This result was not produced by Success or Failure (default instance).");

    /// <summary>Returns the value, or throws a <see cref="DomainValidationException"/> carrying all errors.</summary>
    public T OrThrow() => _isValid ? _value : throw new DomainValidationException(Errors);

    /// <summary>Returns the value through <paramref name="value"/> when valid.</summary>
    public bool TryGet([MaybeNullWhen(false)] out T value)
    {
        value = _isValid ? _value : default;
        return _isValid;
    }

    /// <summary>Creates a valid result.</summary>
    public static ValidationResult<T> Success(T value) => new(true, value, null);

    /// <summary>Creates a failed result carrying at least one error.</summary>
    public static ValidationResult<T> Failure(ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (errors.Count == 0)
        {
            throw new ArgumentException("A failed validation result must carry at least one error.", nameof(errors));
        }

        return new ValidationResult<T>(false, default!, errors);
    }

    /// <summary>Creates a failed result carrying a single error.</summary>
    public static ValidationResult<T> Failure(string path, IDomainError error)
    {
        var errors = new ValidationErrors();
        errors.AddAt(path, error);
        return Failure(errors);
    }
}
