using Typewright.Validation;

namespace Typewright.Agents;

/// <summary>
/// The type-agnostic side of a reply channel, carried in an <see cref="Envelope{TMessage}"/>.
/// Exactly one <c>Try*</c> call wins; later calls return false.
/// </summary>
public interface IReplyChannel
{
    /// <summary>Settles the request with a fault; the caller's await throws <paramref name="error"/>.</summary>
    bool TryFail(Exception error);

    /// <summary>
    /// Settles the request with a typed domain outcome (not-found, conflict, ...). A
    /// <c>TryAskAsync</c> caller receives it as a rejected <see cref="OperationResult{T}"/>;
    /// an <c>AskAsync</c> caller receives a <see cref="DomainErrorException"/>.
    /// </summary>
    bool TryReject(IDomainError error);

    /// <summary>Settles the request as cancelled.</summary>
    bool TryCancel();

    /// <summary>
    /// Whether the request is already settled — typically because the caller cancelled
    /// while it was queued. Agents skip settled requests at dequeue instead of doing work
    /// nobody is waiting for.
    /// </summary>
    bool IsCompleted { get; }

    /// <summary>
    /// The caller's cancellation token. Handlers that opt in observe it to stop work the
    /// caller has abandoned; <see cref="CancellationToken.None"/> when the caller cannot cancel.
    /// </summary>
    CancellationToken Cancellation { get; }
}

/// <summary>The typed side of a reply channel.</summary>
/// <typeparam name="TReply">The reply the request expects.</typeparam>
public interface IReplyChannel<in TReply> : IReplyChannel
{
    /// <summary>Settles the request with its reply.</summary>
    bool TryReply(TReply value);
}

/// <summary>Helpers for replying through the type-agnostic <see cref="IReplyChannel"/>.</summary>
public static class ReplyChannelExtensions
{
    /// <summary>
    /// Replies with <paramref name="value"/> when the channel accepts a
    /// <typeparamref name="TReply"/>; fails the channel otherwise. Does nothing for
    /// fire-and-forget envelopes (a null channel). Returns whether the reply was delivered.
    /// </summary>
    public static bool TryReply<TReply>(this IReplyChannel? channel, TReply value)
    {
        switch (channel)
        {
            case null:
                return false;
            case IReplyChannel<TReply> typed:
                return typed.TryReply(value);
            default:
                channel.TryFail(new InvalidOperationException(
                    $"The reply channel does not accept a {typeof(TReply).Name} reply."));
                return false;
        }
    }

    /// <summary>Settles the channel from an <see cref="OperationResult{T}"/>: reply on success, reject otherwise.</summary>
    public static bool TryComplete<TReply>(this IReplyChannel? channel, OperationResult<TReply> result)
        => result.TryGetError(out var error)
            ? channel?.TryReject(error) ?? false
            : channel.TryReply(result.Value);
}

/// <summary>
/// The reply channel behind <c>AskAsync</c>: completes a <see cref="Task{TResult}"/> with
/// the reply, faults it with <see cref="DomainErrorException"/> on rejection. Continuations
/// run asynchronously so a slow caller can never run inline on (and stall) the agent loop.
/// </summary>
internal sealed class AskReplyChannel<TReply> : TaskCompletionSource<TReply>, IReplyChannel<TReply>
{
    private CancellationTokenRegistration _registration;

    public AskReplyChannel(CancellationToken cancellation)
        : base(TaskCreationOptions.RunContinuationsAsynchronously)
    {
        Cancellation = cancellation;
        if (cancellation.CanBeCanceled)
        {
            _registration = cancellation.UnsafeRegister(
                static state => ((AskReplyChannel<TReply>)state!).TryCancel(), this);
        }
    }

    public CancellationToken Cancellation { get; }

    public bool IsCompleted => Task.IsCompleted;

    public bool TryReply(TReply value) => Settled(TrySetResult(value));

    public bool TryReject(IDomainError error) => Settled(TrySetException(new DomainErrorException(error)));

    public bool TryFail(Exception error) => Settled(TrySetException(error));

    public bool TryCancel() => Settled(TrySetCanceled(Cancellation));

    private bool Settled(bool won)
    {
        if (won)
        {
            _registration.Unregister();
        }

        return won;
    }
}

/// <summary>
/// The reply channel behind <c>TryAskAsync</c>: replies and rejections both complete the
/// task with an <see cref="OperationResult{T}"/>; only faults and cancellation throw.
/// </summary>
internal sealed class TryAskReplyChannel<TReply> : TaskCompletionSource<OperationResult<TReply>>, IReplyChannel<TReply>
{
    private CancellationTokenRegistration _registration;

    public TryAskReplyChannel(CancellationToken cancellation)
        : base(TaskCreationOptions.RunContinuationsAsynchronously)
    {
        Cancellation = cancellation;
        if (cancellation.CanBeCanceled)
        {
            _registration = cancellation.UnsafeRegister(
                static state => ((TryAskReplyChannel<TReply>)state!).TryCancel(), this);
        }
    }

    public CancellationToken Cancellation { get; }

    public bool IsCompleted => Task.IsCompleted;

    public bool TryReply(TReply value) => Settled(TrySetResult(OperationResult<TReply>.Success(value)));

    public bool TryReject(IDomainError error) => Settled(TrySetResult(OperationResult<TReply>.Failure(error)));

    public bool TryFail(Exception error) => Settled(TrySetException(error));

    public bool TryCancel() => Settled(TrySetCanceled(Cancellation));

    private bool Settled(bool won)
    {
        if (won)
        {
            _registration.Unregister();
        }

        return won;
    }
}
