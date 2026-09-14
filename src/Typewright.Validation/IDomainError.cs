using System.Collections.Concurrent;
using System.Text;

namespace Typewright.Validation;

/// <summary>
/// A domain error: one sealed record per kind of failure, carrying the structured facts
/// of what happened and never presentation text. Error types are open — declare them
/// next to whatever produces them — while each domain type's <em>set</em> of possible
/// errors is finite and generated (see <c>DomainTypeInfo.ErrorTypes</c> and the
/// generated <c>MatchError</c>). Not validation-specific: operation errors such as
/// not-found or conflict implement the same contract.
/// </summary>
public interface IDomainError
{
    /// <summary>
    /// Stable machine-readable code for wire formats; derived from the type name by
    /// default (strip the <c>Error</c> suffix, snake_case: <c>DateRangeError</c> becomes
    /// <c>date_range</c>). Override for a custom stable code.
    /// </summary>
    string Code => DomainErrorCode.For(GetType());

    /// <summary>
    /// Invariant default text used by the default renderer. Override to give an error a
    /// better default; presentation and localization belong to
    /// <see cref="IDomainErrorRenderer"/> implementations.
    /// </summary>
    string DefaultMessage => $"The value is invalid ({Code}).";
}

/// <summary>Derives the default wire code for an error type. Cached per type.</summary>
public static class DomainErrorCode
{
    private static readonly ConcurrentDictionary<Type, string> Cache = new();

    /// <summary>The snake_case code for <paramref name="errorType"/>.</summary>
    public static string For(Type errorType)
    {
        ArgumentNullException.ThrowIfNull(errorType);
        return Cache.GetOrAdd(errorType, static type =>
        {
            var name = type.Name;
            var backtick = name.IndexOf('`');
            if (backtick > 0)
            {
                name = name[..backtick];
            }

            const string suffix = "Error";
            if (name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.Ordinal))
            {
                name = name[..^suffix.Length];
            }

            var builder = new StringBuilder(name.Length + 4);
            for (var i = 0; i < name.Length; i++)
            {
                var c = name[i];
                if (char.IsUpper(c))
                {
                    if (i > 0)
                    {
                        builder.Append('_');
                    }

                    builder.Append(char.ToLowerInvariant(c));
                }
                else
                {
                    builder.Append(c);
                }
            }

            return builder.ToString();
        });
    }
}
