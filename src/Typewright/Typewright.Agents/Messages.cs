using System.Collections.Immutable;
using Typewright.Validation;

namespace Typewright.Agents;

/// <summary>
/// Marks a message as a request that expects a <typeparamref name="TReply"/>. The reply
/// type flows from the request to both ends: senders ask through it
/// (<c>AskAsync(new GetCount())</c> infers <c>Task&lt;int&gt;</c>), and handlers register
/// for it with the reply type checked against this declaration.
/// </summary>
/// <typeparam name="TReply">The reply the request expects.</typeparam>
public interface IRequest<out TReply>;

/// <summary>The absence of a meaningful value; the state type of stateless agents.</summary>
public readonly record struct Unit
{
    /// <summary>The single value.</summary>
    public static Unit Value => default;

    /// <inheritdoc />
    public override string ToString() => "()";
}

/// <summary>Identifies a message and the chain of messages it caused, across agents and hosts.</summary>
[DomainValue<Guid>]
public readonly partial record struct CorrelationId
{
    /// <summary>A new random correlation id.</summary>
    public static CorrelationId New() => Create(Guid.NewGuid());
}

/// <summary>
/// Message metadata carried beside the payload. Local agents need none of it and use
/// <see cref="Empty"/> (no allocation); the actor layer stamps sender, correlation, and
/// trace context. Everything here is primitive or a domain value so it serializes at the
/// remote boundary.
/// </summary>
public sealed record MessageHeaders
{
    /// <summary>Headers carrying nothing; the default for local messages.</summary>
    public static MessageHeaders Empty { get; } = new();

    /// <summary>Identifies this message's causal chain.</summary>
    public CorrelationId? CorrelationId { get; init; }

    /// <summary>The sender's address, when the sender is addressable (an actor path).</summary>
    public string? Sender { get; init; }

    /// <summary>When the message was sent.</summary>
    public DateTimeOffset? Timestamp { get; init; }

    /// <summary>W3C <c>traceparent</c> of the sending activity, for distributed tracing.</summary>
    public string? TraceParent { get; init; }

    /// <summary>Additional string headers.</summary>
    public ImmutableDictionary<string, string> Extra { get; init; } = ImmutableDictionary<string, string>.Empty;
}

/// <summary>
/// A message plus its delivery metadata: the reply channel (for requests) and headers.
/// The reply channel lives here rather than in the payload so payloads stay plain,
/// serializable data and a remote proxy can substitute a channel that routes the reply
/// back by address.
/// </summary>
/// <typeparam name="TMessage">The agent's message type.</typeparam>
public readonly record struct Envelope<TMessage>
{
    private readonly MessageHeaders? _headers;

    /// <summary>Creates an envelope.</summary>
    /// <param name="message">The payload.</param>
    /// <param name="replyTo">Where the reply goes, for requests; null for fire-and-forget messages.</param>
    /// <param name="headers">Delivery metadata; null means <see cref="MessageHeaders.Empty"/>.</param>
    public Envelope(TMessage message, IReplyChannel? replyTo = null, MessageHeaders? headers = null)
    {
        Message = message;
        ReplyTo = replyTo;
        _headers = headers;
    }

    /// <summary>The payload.</summary>
    public TMessage Message { get; }

    /// <summary>Where the reply goes; null for fire-and-forget messages.</summary>
    public IReplyChannel? ReplyTo { get; }

    /// <summary>Delivery metadata; never null.</summary>
    public MessageHeaders Headers => _headers ?? MessageHeaders.Empty;
}
