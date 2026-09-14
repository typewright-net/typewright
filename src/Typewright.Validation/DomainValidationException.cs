using System.Text;

namespace Typewright.Validation;

/// <summary>
/// Thrown when raw input cannot be turned into a domain type. Carries every validation
/// failure, not just the first; inspect <see cref="Errors"/> for paths and stable codes.
/// </summary>
public sealed class DomainValidationException : Exception
{
    /// <summary>The validation failures that caused the exception. Never empty.</summary>
    public ValidationErrors Errors { get; }

    /// <summary>Creates an exception carrying the given errors.</summary>
    public DomainValidationException(ValidationErrors errors)
        : base(BuildMessage(errors))
    {
        Errors = errors;
    }

    /// <summary>Creates an exception carrying a single error.</summary>
    public DomainValidationException(string path, string code, string message)
        : this(SingleError(path, code, message))
    {
    }

    private static ValidationErrors SingleError(string path, string code, string message)
    {
        var errors = new ValidationErrors();
        errors.AddAt(path, code, message);
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

            var error = errors[i];
            if (error.Path.Length > 0)
            {
                builder.Append(error.Path).Append(": ");
            }

            builder.Append('[').Append(error.Code).Append("] ").Append(error.Message);
        }

        return builder.ToString();
    }
}
