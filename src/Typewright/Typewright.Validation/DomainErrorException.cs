namespace Typewright.Validation;

/// <summary>
/// Carries a single typed <see cref="IDomainError"/> that rejected an operation (not-found,
/// conflict, ...) through code paths that throw. The non-throwing alternative is
/// <see cref="OperationResult{T}"/>; validation failures use
/// <see cref="DomainValidationException"/>, which accumulates many errors with paths.
/// Handle with a <c>when</c> guard: <c>catch (DomainErrorException e) when (e.Error is
/// InsufficientFundsError)</c>.
/// </summary>
public sealed class DomainErrorException : Exception
{
    /// <summary>Creates an exception carrying <paramref name="error"/>.</summary>
    public DomainErrorException(IDomainError error)
        : base(BuildMessage(error))
    {
        Error = error;
    }

    /// <summary>The domain error that rejected the operation.</summary>
    public IDomainError Error { get; }

    private static string BuildMessage(IDomainError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return $"The operation was rejected: [{error.Code}] {error.DefaultMessage}";
    }
}
