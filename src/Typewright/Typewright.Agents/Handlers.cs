namespace Typewright.Agents;

/// <summary>
/// Processes one message against the agent's state and returns the next state. The
/// agent commits the returned state only when handling completes without an exception,
/// so a failed message never leaves a partially applied state. State types should be
/// immutable (records) because the committed state is readable from other threads.
/// </summary>
/// <typeparam name="TState">The agent's state.</typeparam>
/// <typeparam name="TMessage">The agent's message type.</typeparam>
public interface IMessageHandler<TState, TMessage>
{
    /// <summary>Handles <paramref name="envelope"/>; replies (if any) go through <see cref="Envelope{TMessage}.ReplyTo"/>.</summary>
    /// <param name="state">The current committed state.</param>
    /// <param name="envelope">The message and its delivery metadata.</param>
    /// <param name="cancellationToken">Cancelled when the agent aborts.</param>
    ValueTask<TState> HandleAsync(TState state, Envelope<TMessage> envelope, CancellationToken cancellationToken);
}

/// <summary>Processes one message for a stateless agent.</summary>
/// <typeparam name="TMessage">The agent's message type.</typeparam>
public interface IMessageHandler<TMessage>
{
    /// <summary>Handles <paramref name="envelope"/>; replies (if any) go through <see cref="Envelope{TMessage}.ReplyTo"/>.</summary>
    ValueTask HandleAsync(Envelope<TMessage> envelope, CancellationToken cancellationToken);
}

/// <summary>
/// Optional lifecycle hooks a handler may implement. Each runs on the agent's loop, in
/// its dispatcher context, one at a time with message handling.
/// </summary>
/// <typeparam name="TState">The agent's state.</typeparam>
public interface IAgentLifecycle<TState>
{
    /// <summary>
    /// Runs before the first message; returns the state to start from (for example, state
    /// loaded from storage). A failure here fails the agent.
    /// </summary>
    ValueTask<TState> OnStartedAsync(TState state, CancellationToken cancellationToken) => new(state);

    /// <summary>
    /// Runs when a failure policy decides <see cref="FailureDecision.Restart"/>; returns the
    /// state to continue from (default: the initial state). A failure here fails the agent.
    /// </summary>
    ValueTask<TState> OnRestartingAsync(TState initialState, Exception cause, CancellationToken cancellationToken)
        => new(initialState);

    /// <summary>
    /// Runs once when the agent terminates for any reason, with its final committed state.
    /// The token is cancelled when the agent was aborted.
    /// </summary>
    ValueTask OnStoppedAsync(TState finalState, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}

/// <summary>Adapts a stateless handler to the stateful core loop.</summary>
internal sealed class StatelessHandlerAdapter<TMessage>(IMessageHandler<TMessage> inner) : IMessageHandler<Unit, TMessage>
{
    public ValueTask<Unit> HandleAsync(Unit state, Envelope<TMessage> envelope, CancellationToken cancellationToken)
    {
        var pending = inner.HandleAsync(envelope, cancellationToken);
        return pending.IsCompletedSuccessfully ? default : AwaitAsync(pending);
    }

    private static async ValueTask<Unit> AwaitAsync(ValueTask pending)
    {
        await pending;
        return default;
    }
}
