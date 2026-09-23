using Microsoft.Extensions.DependencyInjection;
using Typewright.Agents;

namespace Typewright.Actors;

/// <summary>What an actor sees of its surroundings while it is created and runs.</summary>
/// <typeparam name="TMessage">The actor's message type.</typeparam>
public sealed class ActorContext<TMessage>
{
    internal ActorContext(ActorPath path, ActorSystem system, IMessageSink<TMessage> self)
    {
        Path = path;
        System = system;
        Self = self;
    }

    /// <summary>The actor's address.</summary>
    public ActorPath Path { get; }

    /// <summary>The system the actor lives in, for resolving and spawning other actors.</summary>
    public IActorSystem System { get; }

    /// <summary>
    /// The application's services. Create a scope (<c>Services.CreateAsyncScope()</c>)
    /// inside a handler for per-message scoped services.
    /// </summary>
    public IServiceProvider Services => ((ActorSystem)System).Services;

    /// <summary>
    /// The actor itself, for fire-and-forget sends only: an actor that asks itself waits
    /// for a reply only it can produce, which is a deadlock.
    /// </summary>
    public IMessageSink<TMessage> Self { get; }
}

/// <summary>
/// How to create an actor: its initial state and handler (both per actor, from its
/// context) and its agent options. One definition can back any number of actors, such
/// as one per resource key.
/// </summary>
public sealed record ActorDefinition<TState, TMessage>
{
    /// <summary>The state a new (or restarted) actor starts from.</summary>
    public required Func<ActorContext<TMessage>, TState> InitialState { get; init; }

    /// <summary>The actor's handler. A shared instance must be safe to use from many actors (routed handlers are).</summary>
    public required Func<ActorContext<TMessage>, IMessageHandler<TState, TMessage>> Handler { get; init; }

    /// <summary>
    /// Mailbox, dispatcher, failure policy, and interceptors. The system names the agent
    /// after the actor's path and places its own interceptors (sender scope, then tracing
    /// and metrics when enabled) outside these. A failure policy decision of
    /// <see cref="FailureDecision.Escalate"/> hands the failure to the parent's supervision.
    /// </summary>
    public AgentOptions<TMessage>? Options { get; init; }

    /// <summary>
    /// How this actor supervises its children (the actors beneath its path). Null uses
    /// <see cref="ActorSystemOptions.DefaultSupervision"/>.
    /// </summary>
    public SupervisionStrategy? ChildSupervision { get; init; }
}

/// <summary>Factories for <see cref="ActorDefinition{TState, TMessage}"/>.</summary>
public static class ActorDefinition
{
    /// <summary>A definition with a fixed initial state and a shared handler.</summary>
    public static ActorDefinition<TState, TMessage> Create<TState, TMessage>(
        TState initialState,
        IMessageHandler<TState, TMessage> handler,
        AgentOptions<TMessage>? options = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new ActorDefinition<TState, TMessage>
        {
            InitialState = _ => initialState,
            Handler = _ => handler,
            Options = options,
        };
    }

    /// <summary>A definition whose initial state depends on the actor (typically its path) and a shared handler.</summary>
    public static ActorDefinition<TState, TMessage> Create<TState, TMessage>(
        Func<ActorContext<TMessage>, TState> initialState,
        IMessageHandler<TState, TMessage> handler,
        AgentOptions<TMessage>? options = null)
    {
        ArgumentNullException.ThrowIfNull(initialState);
        ArgumentNullException.ThrowIfNull(handler);
        return new ActorDefinition<TState, TMessage>
        {
            InitialState = initialState,
            Handler = _ => handler,
            Options = options,
        };
    }

    /// <summary>
    /// A definition whose handler is created per actor from the application's services;
    /// its constructor may also take the <see cref="ActorContext{TMessage}"/>.
    /// </summary>
    public static ActorDefinition<TState, TMessage> FromServices<TState, TMessage, THandler>(
        Func<ActorContext<TMessage>, TState> initialState,
        AgentOptions<TMessage>? options = null)
        where THandler : class, IMessageHandler<TState, TMessage>
    {
        ArgumentNullException.ThrowIfNull(initialState);
        return new ActorDefinition<TState, TMessage>
        {
            InitialState = initialState,
            Handler = context => ActivatorUtilities.CreateInstance<THandler>(context.Services, context),
            Options = options,
        };
    }
}
