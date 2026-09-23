using Typewright.Validation;

namespace Typewright.Agents;

/// <summary>Something that accepts fire-and-forget messages.</summary>
/// <typeparam name="TMessage">The message type accepted.</typeparam>
public interface IMessageSink<in TMessage>
{
    /// <summary>Posts without waiting; false when the mailbox is full (under a waiting or rejecting policy) or stopped.</summary>
    bool TryPost(TMessage message);

    /// <summary>
    /// Posts, waiting for space in a full bounded mailbox. Throws
    /// <see cref="AgentStoppedException"/> when stopped and <see cref="MailboxFullException"/>
    /// under a rejecting policy. The token governs only the wait: once enqueued, the message runs.
    /// </summary>
    ValueTask PostAsync(TMessage message, CancellationToken cancellationToken = default);
}

/// <summary>
/// The surface callers depend on to talk to an agent or actor — local, or remote behind a
/// proxy that implements the same interface.
/// </summary>
/// <typeparam name="TMessage">The message type accepted.</typeparam>
public interface IMessageTarget<in TMessage> : IMessageSink<TMessage>
{
    /// <summary>
    /// Sends a request and awaits its reply. A domain rejection throws
    /// <see cref="DomainErrorException"/>; a handler fault throws that fault. Cancelling
    /// stops the wait immediately and, if the request has not been dequeued yet, the agent
    /// skips it — so a cancelled request may or may not have been processed.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="request"/> is not a <typeparamref name="TMessage"/>.</exception>
    Task<TReply> AskAsync<TReply>(IRequest<TReply> request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a request and awaits its outcome: the reply, or the typed domain error that
    /// rejected it. Faults and cancellation still throw. Cancellation semantics match
    /// <see cref="AskAsync{TReply}"/>.
    /// </summary>
    Task<OperationResult<TReply>> TryAskAsync<TReply>(IRequest<TReply> request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Envelope-level delivery: what the actor layer and remote transports use to send
/// messages with headers and custom reply channels. <see cref="Delivery"/> builds the
/// message-level operations on top of it.
/// </summary>
/// <typeparam name="TMessage">The message type accepted.</typeparam>
public interface IEnvelopeTarget<TMessage>
{
    /// <summary>Delivers without waiting; false when full or stopped.</summary>
    bool TryDeliver(Envelope<TMessage> envelope);

    /// <summary>Delivers, waiting for space in a full bounded mailbox; throws when stopped or rejected.</summary>
    ValueTask DeliverAsync(Envelope<TMessage> envelope, CancellationToken cancellationToken = default);
}

/// <summary>
/// Message-level operations over any <see cref="IEnvelopeTarget{TMessage}"/>, with optional
/// headers. Agents implement <see cref="IMessageTarget{TMessage}"/> with these; the actor
/// layer calls them with stamped headers; a remote proxy gets them for free.
/// </summary>
public static class Delivery
{
    /// <summary>Posts a fire-and-forget message without waiting.</summary>
    public static bool TryPost<TMessage>(IEnvelopeTarget<TMessage> target, TMessage message, MessageHeaders? headers = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ThrowIfNull(message);
        return target.TryDeliver(new Envelope<TMessage>(message, null, headers));
    }

    /// <summary>Posts a fire-and-forget message, waiting for mailbox space.</summary>
    public static ValueTask PostAsync<TMessage>(
        IEnvelopeTarget<TMessage> target,
        TMessage message,
        MessageHeaders? headers = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ThrowIfNull(message);
        return target.DeliverAsync(new Envelope<TMessage>(message, null, headers), cancellationToken);
    }

    /// <summary>Sends a request and awaits its reply; see <see cref="IMessageTarget{TMessage}.AskAsync{TReply}"/>.</summary>
    public static Task<TReply> AskAsync<TMessage, TReply>(
        IEnvelopeTarget<TMessage> target,
        IRequest<TReply> request,
        MessageHeaders? headers = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        var message = AsMessage<TMessage, TReply>(request);
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<TReply>(cancellationToken);
        }

        var channel = new AskReplyChannel<TReply>(cancellationToken);
        return Send(target, new Envelope<TMessage>(message, channel, headers), channel, channel.Task, cancellationToken);
    }

    /// <summary>Sends a request and awaits its outcome; see <see cref="IMessageTarget{TMessage}.TryAskAsync{TReply}"/>.</summary>
    public static Task<OperationResult<TReply>> TryAskAsync<TMessage, TReply>(
        IEnvelopeTarget<TMessage> target,
        IRequest<TReply> request,
        MessageHeaders? headers = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        var message = AsMessage<TMessage, TReply>(request);
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<OperationResult<TReply>>(cancellationToken);
        }

        var channel = new TryAskReplyChannel<TReply>(cancellationToken);
        return Send(target, new Envelope<TMessage>(message, channel, headers), channel, channel.Task, cancellationToken);
    }

    private static TMessage AsMessage<TMessage, TReply>(IRequest<TReply> request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request is TMessage message
            ? message
            : throw new ArgumentException(
                $"The request '{request.GetType().Name}' is not a {typeof(TMessage).Name}, so this target cannot accept it.",
                nameof(request));
    }

    private static Task<TResult> Send<TMessage, TResult>(
        IEnvelopeTarget<TMessage> target,
        Envelope<TMessage> envelope,
        IReplyChannel channel,
        Task<TResult> reply,
        CancellationToken cancellationToken)
    {
        ValueTask delivery;
        try
        {
            delivery = target.DeliverAsync(envelope, cancellationToken);
        }
        catch (Exception exception)
        {
            channel.TryFail(exception);
            return reply;
        }

        return delivery.IsCompletedSuccessfully ? reply : AwaitDeliveryAsync(delivery, channel, reply);
    }

    private static async Task<TResult> AwaitDeliveryAsync<TResult>(ValueTask delivery, IReplyChannel channel, Task<TResult> reply)
    {
        try
        {
            await delivery.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            channel.TryCancel();
        }
        catch (Exception exception)
        {
            channel.TryFail(exception);
        }

        return await reply.ConfigureAwait(false);
    }

    private static void ThrowIfNull<TMessage>(TMessage message)
    {
        if (message is null)
        {
            throw new ArgumentNullException(nameof(message));
        }
    }
}
