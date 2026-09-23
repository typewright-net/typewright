using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Typewright.Agents;

/// <summary>The outcome of offering an envelope to a mailbox.</summary>
public enum PostResult
{
    /// <summary>The envelope was accepted.</summary>
    Posted,

    /// <summary>The mailbox is full and its policy does not make room.</summary>
    Full,

    /// <summary>The mailbox is completed and accepts nothing more.</summary>
    Closed,
}

/// <summary>What a bounded mailbox does when it is full.</summary>
public enum MailboxFullMode
{
    /// <summary><c>PostAsync</c> waits for space; <c>TryPost</c> fails.</summary>
    Wait,

    /// <summary>Both <c>PostAsync</c> and <c>TryPost</c> fail immediately.</summary>
    Reject,

    /// <summary>The oldest queued message is dropped to make room (its request, if any, fails).</summary>
    DropOldest,

    /// <summary>The newest queued message is dropped to make room (its request, if any, fails).</summary>
    DropNewest,
}

/// <summary>Capacity and overflow behavior for the default mailbox.</summary>
/// <param name="Capacity">Maximum queued messages; null for unbounded.</param>
/// <param name="FullMode">What happens when a bounded mailbox is full.</param>
public sealed record MailboxOptions(int? Capacity = null, MailboxFullMode FullMode = MailboxFullMode.Wait)
{
    /// <summary>An unbounded FIFO mailbox.</summary>
    public static MailboxOptions Unbounded { get; } = new();
}

/// <summary>
/// An agent's message queue. One agent reads it (single reader); any number of senders
/// write it. Implementations must fail the reply channel of any request they drop.
/// </summary>
/// <typeparam name="TMessage">The agent's message type.</typeparam>
public interface IMailbox<TMessage>
{
    /// <summary>Offers an envelope without waiting.</summary>
    PostResult TryPost(Envelope<TMessage> envelope);

    /// <summary>Offers an envelope, waiting for space when the policy is to wait.</summary>
    ValueTask<PostResult> PostAsync(Envelope<TMessage> envelope, CancellationToken cancellationToken);

    /// <summary>Waits until an envelope is available (true) or the mailbox is completed and empty (false).</summary>
    ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken);

    /// <summary>Takes the next envelope, if any.</summary>
    bool TryTake(out Envelope<TMessage> envelope);

    /// <summary>Stops accepting envelopes; queued envelopes remain readable.</summary>
    void Complete();

    /// <summary>The number of queued envelopes (approximate under concurrency).</summary>
    int Count { get; }

    /// <summary>Reads envelopes until the mailbox is completed and empty.</summary>
    async IAsyncEnumerable<Envelope<TMessage>> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (await WaitToReadAsync(cancellationToken))
        {
            while (TryTake(out var envelope))
            {
                yield return envelope;
            }
        }
    }
}

/// <summary>
/// The default mailbox: a FIFO over <see cref="Channel{T}"/>, unbounded unless
/// <see cref="MailboxOptions.Capacity"/> is set. Synchronous continuations are disabled,
/// so a sender never runs the agent loop inline on its own thread.
/// </summary>
public sealed class ChannelMailbox<TMessage> : IMailbox<TMessage>
{
    private readonly Channel<Envelope<TMessage>> _channel;
    private readonly bool _reject;
    private volatile bool _completed;

    /// <summary>Creates a mailbox with the given capacity and overflow behavior.</summary>
    public ChannelMailbox(MailboxOptions? options = null)
    {
        options ??= MailboxOptions.Unbounded;
        if (options.Capacity is not { } capacity)
        {
            _channel = Channel.CreateUnbounded<Envelope<TMessage>>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
            });
            return;
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1, nameof(options));
        _reject = options.FullMode == MailboxFullMode.Reject;
        _channel = Channel.CreateBounded<Envelope<TMessage>>(
            new BoundedChannelOptions(capacity)
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
                FullMode = options.FullMode switch
                {
                    MailboxFullMode.DropOldest => BoundedChannelFullMode.DropOldest,
                    MailboxFullMode.DropNewest => BoundedChannelFullMode.DropNewest,
                    _ => BoundedChannelFullMode.Wait,
                },
            },
            static dropped => dropped.ReplyTo?.TryFail(new MessageDroppedException()));
    }

    /// <inheritdoc />
    public int Count => _channel.Reader.Count;

    /// <inheritdoc />
    public PostResult TryPost(Envelope<TMessage> envelope)
        => _channel.Writer.TryWrite(envelope) ? PostResult.Posted
            : _completed ? PostResult.Closed
            : PostResult.Full;

    /// <inheritdoc />
    public ValueTask<PostResult> PostAsync(Envelope<TMessage> envelope, CancellationToken cancellationToken)
    {
        var result = TryPost(envelope);
        return result != PostResult.Full || _reject
            ? new ValueTask<PostResult>(result)
            : WaitAndPostAsync(envelope, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken)
        => _channel.Reader.WaitToReadAsync(cancellationToken);

    /// <inheritdoc />
    public bool TryTake(out Envelope<TMessage> envelope) => _channel.Reader.TryRead(out envelope);

    /// <inheritdoc />
    public void Complete()
    {
        _completed = true;
        _channel.Writer.TryComplete();
    }

    private async ValueTask<PostResult> WaitAndPostAsync(Envelope<TMessage> envelope, CancellationToken cancellationToken)
    {
        while (await _channel.Writer.WaitToWriteAsync(cancellationToken).ConfigureAwait(false))
        {
            if (_channel.Writer.TryWrite(envelope))
            {
                return PostResult.Posted;
            }
        }

        return PostResult.Closed;
    }
}
