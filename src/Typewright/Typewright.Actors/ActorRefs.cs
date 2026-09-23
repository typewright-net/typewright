using System.Diagnostics.CodeAnalysis;
using Typewright.Agents;
using Typewright.Validation;

namespace Typewright.Actors;

/// <summary>An addressable actor, independent of its message type.</summary>
public interface IActorRef
{
    /// <summary>The actor's address.</summary>
    ActorPath Path { get; }

    /// <summary>The message type the actor accepts.</summary>
    Type MessageType { get; }
}

/// <summary>
/// A reference to an actor: the <see cref="IMessageTarget{TMessage}"/> callers depend on.
/// It addresses the actor's cell, so it stays valid across supervised restarts. Sends are
/// stamped with a correlation id (propagated from the message being handled, when sent
/// from inside an actor), the sending actor's path, a timestamp, and trace context.
/// </summary>
/// <typeparam name="TMessage">The message type the actor accepts.</typeparam>
public interface IActorRef<in TMessage> : IActorRef, IMessageTarget<TMessage>;

/// <summary>A stateful actor's last committed state, readable without going through its mailbox.</summary>
/// <typeparam name="TState">The actor's state type.</typeparam>
public interface IActorState<out TState>
{
    /// <summary>The last committed state; never a partially applied one.</summary>
    TState CurrentState { get; }
}

/// <summary>State reads on actor references.</summary>
public static class ActorRefExtensions
{
    /// <summary>
    /// Reads the actor's last committed state without going through its mailbox (the CQRS
    /// read path). False when the actor's state is not a <typeparamref name="TState"/>.
    /// </summary>
    public static bool TryGetState<TState>(this IActorRef actor, [MaybeNullWhen(false)] out TState state)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor is IActorState<TState> stateful)
        {
            state = stateful.CurrentState;
            return true;
        }

        state = default;
        return false;
    }
}

internal sealed class ActorRef<TState, TMessage>(ActorCell<TState, TMessage> cell) : IActorRef<TMessage>, IActorState<TState>
{
    public ActorPath Path => cell.Path;

    public Type MessageType => typeof(TMessage);

    public TState CurrentState => cell.Agent.CurrentState;

    public bool TryPost(TMessage message)
        => Delivery.TryPost(cell.Agent, message, cell.System.StampHeaders());

    public ValueTask PostAsync(TMessage message, CancellationToken cancellationToken = default)
        => Delivery.PostAsync(cell.Agent, message, cell.System.StampHeaders(), cancellationToken);

    public Task<TReply> AskAsync<TReply>(IRequest<TReply> request, CancellationToken cancellationToken = default)
        => Delivery.AskAsync(cell.Agent, request, cell.System.StampHeaders(), cancellationToken);

    public Task<OperationResult<TReply>> TryAskAsync<TReply>(IRequest<TReply> request, CancellationToken cancellationToken = default)
        => Delivery.TryAskAsync(cell.Agent, request, cell.System.StampHeaders(), cancellationToken);

    public override string ToString() => $"actor {Path}";
}

/// <summary>An actor's view of itself for sending: fire-and-forget only, because asking yourself deadlocks.</summary>
internal sealed class SelfSink<TMessage>(IMessageSink<TMessage> actor) : IMessageSink<TMessage>
{
    public bool TryPost(TMessage message) => actor.TryPost(message);

    public ValueTask PostAsync(TMessage message, CancellationToken cancellationToken = default)
        => actor.PostAsync(message, cancellationToken);
}
