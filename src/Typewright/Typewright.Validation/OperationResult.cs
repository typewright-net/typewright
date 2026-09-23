using System.Diagnostics.CodeAnalysis;

namespace Typewright.Validation;

/// <summary>
/// The outcome of a domain operation: either a <typeparamref name="T"/> or a single typed
/// <see cref="IDomainError"/> (a domain outcome such as not-found or conflict), never both.
/// Faults (bugs, I/O failures) are not results; they remain exceptions.
/// </summary>
/// <remarks>
/// Values convert implicitly, and <see cref="OperationResult.Reject"/> produces a
/// <see cref="Rejection"/> that converts to any <c>OperationResult&lt;T&gt;</c>, so handlers
/// can return either branch without spelling the type:
/// <code>
/// OperationResult&lt;Balance&gt; Withdraw(Amount amount) =&gt;
///     amount &gt; balance ? OperationResult.Reject(new InsufficientFundsError(balance, amount)) : balance - amount;
/// </code>
/// </remarks>
public readonly struct OperationResult<T>
{
    private const byte SuccessState = 1;
    private const byte FailureState = 2;

    private readonly T _value;
    private readonly IDomainError? _error;
    private readonly byte _state;

    private OperationResult(T value, IDomainError? error, byte state)
    {
        _value = value;
        _error = error;
        _state = state;
    }

    /// <summary>Whether the operation succeeded and <see cref="Value"/> is available.</summary>
    public bool IsSuccess => _state == SuccessState;

    /// <summary>The operation's value. Throws if the operation was rejected.</summary>
    public T Value => _state == SuccessState
        ? _value
        : throw new InvalidOperationException(_state == FailureState
            ? $"The operation was rejected ({_error!.Code}); check IsSuccess before reading Value, or use OrThrow()."
            : "This result was not produced by Success or Failure (default instance).");

    /// <summary>The rejection. Throws if the operation succeeded.</summary>
    public IDomainError Error => _state == FailureState
        ? _error!
        : throw new InvalidOperationException(_state == SuccessState
            ? "A successful result has no error."
            : "This result was not produced by Success or Failure (default instance).");

    /// <summary>Creates a successful result.</summary>
    public static OperationResult<T> Success(T value) => new(value, null, SuccessState);

    /// <summary>Creates a rejected result.</summary>
    public static OperationResult<T> Failure(IDomainError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new OperationResult<T>(default!, error, FailureState);
    }

    /// <summary>Returns the value through <paramref name="value"/> when successful.</summary>
    public bool TryGetValue([MaybeNullWhen(false)] out T value)
    {
        value = _state == SuccessState ? _value : default;
        return _state == SuccessState;
    }

    /// <summary>Returns the error through <paramref name="error"/> when rejected.</summary>
    public bool TryGetError([NotNullWhen(true)] out IDomainError? error)
    {
        error = _state == FailureState ? _error : null;
        return _state == FailureState;
    }

    /// <summary>Folds both branches into one value.</summary>
    public TResult Match<TResult>(Func<T, TResult> success, Func<IDomainError, TResult> failure)
    {
        ArgumentNullException.ThrowIfNull(success);
        ArgumentNullException.ThrowIfNull(failure);
        return _state switch
        {
            SuccessState => success(_value),
            FailureState => failure(_error!),
            _ => throw new InvalidOperationException(
                "This result was not produced by Success or Failure (default instance)."),
        };
    }

    /// <summary>Returns the value, or throws a <see cref="DomainErrorException"/> carrying the rejection.</summary>
    public T OrThrow() => _state == FailureState ? throw new DomainErrorException(_error!) : Value;

    /// <summary>Wraps a value as a successful result.</summary>
    public static implicit operator OperationResult<T>(T value) => Success(value);

    /// <summary>Converts a type-agnostic <see cref="Rejection"/> into a rejected result.</summary>
    public static implicit operator OperationResult<T>(Rejection rejection) => Failure(rejection.Error);

    /// <inheritdoc />
    public override string ToString() => _state switch
    {
        SuccessState => $"Success({_value})",
        FailureState => $"Rejected({_error!.Code})",
        _ => "(default)",
    };
}

/// <summary>A rejection not yet bound to a result type; converts to any <see cref="OperationResult{T}"/>.</summary>
/// <param name="Error">The domain error that rejected the operation.</param>
public readonly record struct Rejection(IDomainError Error);

/// <summary>Factories for <see cref="OperationResult{T}"/>.</summary>
public static class OperationResult
{
    /// <summary>Creates a successful result.</summary>
    public static OperationResult<T> Success<T>(T value) => OperationResult<T>.Success(value);

    /// <summary>Creates a rejection that converts to any <see cref="OperationResult{T}"/>.</summary>
    public static Rejection Reject(IDomainError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Rejection(error);
    }
}
