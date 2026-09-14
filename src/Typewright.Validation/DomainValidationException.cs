using System.Text;

namespace Typewright.Validation;

/// <summary>
/// Thrown when raw input cannot be turned into a domain type. Carries every validation
/// failure, not just the first, as typed <see cref="IDomainError"/> values — so handling
/// stays strongly typed: <c>catch (DomainValidationException e) when
/// (e.Errors.Contains&lt;ChecksumError&gt;())</c>, or pattern match over
/// <see cref="ValidationFailure.Error"/>.
/// </summary>
public sealed class DomainValidationException : Exception
{
    /// <summary>The validation failures that caused the exception. Never empty.</summary>
    public ValidationErrors Errors { get; }

    /// <summary>Creates an exception carrying the given failures.</summary>
    public DomainValidationException(ValidationErrors errors)
        : base(BuildMessage(errors))
    {
        Errors = errors;
    }

    /// <summary>Creates an exception carrying a single failure.</summary>
    public DomainValidationException(string path, IDomainError error)
        : this(SingleFailure(path, error))
    {
    }

    private static ValidationErrors SingleFailure(string path, IDomainError error)
    {
        var errors = new ValidationErrors();
        errors.AddAt(path, error);
        return errors;
    }

    private static string BuildMessage(ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (errors.Count == 0)
        {
            throw new ArgumentException("A validation exception must carry at least one error.", nameof(errors));
        }

        var builder = new StringBuilder();
        builder.Append(errors.Count == 1
            ? "Validation failed: "
            : $"Validation failed with {errors.Count} errors: ");

        for (var i = 0; i < errors.Count; i++)
        {
            if (i > 0)
            {
                builder.Append("; ");
            }

            var failure = errors[i];
            if (failure.Path.Length > 0)
            {
                builder.Append(failure.Path).Append(": ");
            }

            builder.Append('[').Append(failure.Error.Code).Append("] ").Append(failure.Error.DefaultMessage);
        }

        return builder.ToString();
    }
}
