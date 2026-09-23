using System.Collections.Concurrent;
using System.Collections.Frozen;
using Typewright.Validation;

namespace Typewright.Agents;

/// <summary>
/// Builds handlers that route messages by runtime type, with request/reply typing checked
/// at registration:
/// <code>
/// var counter = Agent.Start(0, MessageHandler.For&lt;int, CounterMessage&gt;()
///     .On&lt;Increment&gt;(static (count, _) =&gt; count + 1)
///     .On&lt;GetCount, int&gt;(static (count, _) =&gt; (count, count))
///     .Build());
/// </code>
/// </summary>
public static class MessageHandler
{
    /// <summary>Starts a stateful handler over <typeparamref name="TState"/>.</summary>
    public static MessageHandlerBuilder<TState, TMessage> For<TState, TMessage>() => new();

    /// <summary>Starts a stateless handler.</summary>
    public static StatelessMessageHandlerBuilder<TMessage> For<TMessage>() => new();
}

/// <summary>
/// Registers one route per message type. Routing uses the message's runtime type; when it
/// has no exact route, the most derived registered base type or interface handles it
/// (an ambiguous match, or no match without <see cref="Otherwise"/>, is a failure).
/// </summary>
/// <remarks>
/// Request routes are constrained <c>TRequest : TMessage, IRequest&lt;TReply&gt;</c>, so a
/// handler cannot reply with a type other than the one the request declares. Overloads
/// taking a <see cref="CancellationToken"/> receive a token that fires when the agent
/// aborts <em>or</em> when the request's caller cancels, so they can stop work nobody is
/// waiting for; handlers without a token run to completion.
/// </remarks>
public sealed class MessageHandlerBuilder<TState, TMessage>
{
    private readonly Dictionary<Type, Route<TState, TMessage>> _routes = [];
    private Route<TState, TMessage>? _otherwise;
    private Func<TState, CancellationToken, ValueTask<TState>>? _onStarted;
    private Func<TState, Exception, CancellationToken, ValueTask<TState>>? _onRestarting;
    private Func<TState, CancellationToken, ValueTask>? _onStopped;

    internal MessageHandlerBuilder()
    {
    }

    /// <summary>Handles a fire-and-forget message synchronously.</summary>
    public MessageHandlerBuilder<TState, TMessage> On<TCase>(Func<TState, TCase, TState> handle)
        where TCase : TMessage
    {
        ArgumentNullException.ThrowIfNull(handle);
        return Add<TCase>((state, envelope, _) => new ValueTask<TState>(handle(state, (TCase)envelope.Message!)));
    }

    /// <summary>Handles a fire-and-forget message asynchronously.</summary>
    public MessageHandlerBuilder<TState, TMessage> On<TCase>(Func<TState, TCase, CancellationToken, ValueTask<TState>> handle)
        where TCase : TMessage
    {
        ArgumentNullException.ThrowIfNull(handle);
        return Add<TCase>((state, envelope, token) => handle(state, (TCase)envelope.Message!, token));
    }

    /// <summary>Handles a request synchronously, returning the next state and the reply.</summary>
    public MessageHandlerBuilder<TState, TMessage> On<TRequest, TReply>(Func<TState, TRequest, (TState State, TReply Reply)> handle)
        where TRequest : TMessage, IRequest<TReply>
    {
        ArgumentNullException.ThrowIfNull(handle);
        return Add<TRequest>((state, envelope, _) =>
        {
            var (next, reply) = handle(state, (TRequest)envelope.Message!);
            envelope.ReplyTo.TryReply(reply);
            return new ValueTask<TState>(next);
        });
    }

    /// <summary>Handles a request synchronously, returning the next state and a reply or rejection.</summary>
    public MessageHandlerBuilder<TState, TMessage> On<TRequest, TReply>(
        Func<TState, TRequest, (TState State, OperationResult<TReply> Result)> handle)
        where TRequest : TMessage, IRequest<TReply>
    {
        ArgumentNullException.ThrowIfNull(handle);
        return Add<TRequest>((state, envelope, _) =>
        {
            var (next, result) = handle(state, (TRequest)envelope.Message!);
            envelope.ReplyTo.TryComplete(result);
            return new ValueTask<TState>(next);
        });
    }

    /// <summary>Handles a request asynchronously; the token also fires if the caller cancels.</summary>
    public MessageHandlerBuilder<TState, TMessage> On<TRequest, TReply>(
        Func<TState, TRequest, CancellationToken, ValueTask<(TState State, TReply Reply)>> handle)
        where TRequest : TMessage, IRequest<TReply>
    {
        ArgumentNullException.ThrowIfNull(handle);
        return Add<TRequest>((state, envelope, token) => CallerCancellable.InvokeAsync(
            state, envelope, token, handle, static (channel, reply) => channel.TryReply(reply)));
    }

    /// <summary>Handles a request asynchronously with a reply or rejection; the token also fires if the caller cancels.</summary>
    public MessageHandlerBuilder<TState, TMessage> On<TRequest, TReply>(
        Func<TState, TRequest, CancellationToken, ValueTask<(TState State, OperationResult<TReply> Result)>> handle)
        where TRequest : TMessage, IRequest<TReply>
    {
        ArgumentNullException.ThrowIfNull(handle);
        return Add<TRequest>((state, envelope, token) => CallerCancellable.InvokeAsync(
            state, envelope, token, handle, static (channel, result) => channel.TryComplete(result)));
    }

    /// <summary>Handles any message without a route. Without this, an unrouted message is a failure.</summary>
    public MessageHandlerBuilder<TState, TMessage> Otherwise(Func<TState, Envelope<TMessage>, CancellationToken, ValueTask<TState>> handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (_otherwise is not null)
        {
            throw new InvalidOperationException("Otherwise has already been registered.");
        }

        _otherwise = (state, envelope, token) => handle(state, envelope, token);
        return this;
    }

    /// <summary>Runs before the first message; returns the state to start from.</summary>
    public MessageHandlerBuilder<TState, TMessage> OnStarted(Func<TState, CancellationToken, ValueTask<TState>> hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        _onStarted = hook;
        return this;
    }

    /// <summary>Runs on a <see cref="FailureDecision.Restart"/>; returns the state to continue from.</summary>
    public MessageHandlerBuilder<TState, TMessage> OnRestarting(Func<TState, Exception, CancellationToken, ValueTask<TState>> hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        _onRestarting = hook;
        return this;
    }

    /// <summary>Runs once when the agent terminates, with its final committed state.</summary>
    public MessageHandlerBuilder<TState, TMessage> OnStopped(Func<TState, CancellationToken, ValueTask> hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        _onStopped = hook;
        return this;
    }

    /// <summary>Builds the handler. The builder may be reused; built handlers are independent and thread-safe.</summary>
    public IMessageHandler<TState, TMessage> Build()
        => new RoutedHandler<TState, TMessage>(
            _routes.ToFrozenDictionary(),
            _otherwise,
            _onStarted,
            _onRestarting,
            _onStopped);

    private MessageHandlerBuilder<TState, TMessage> Add<TCase>(Route<TState, TMessage> route)
    {
        if (!_routes.TryAdd(typeof(TCase), route))
        {
            throw new InvalidOperationException($"A route for '{typeof(TCase).Name}' has already been registered.");
        }

        return this;
    }
}

/// <summary>Registers routes for a stateless handler; see <see cref="MessageHandlerBuilder{TState, TMessage}"/>.</summary>
public sealed class StatelessMessageHandlerBuilder<TMessage>
{
    private readonly MessageHandlerBuilder<Unit, TMessage> _inner = new();

    internal StatelessMessageHandlerBuilder()
    {
    }

    /// <summary>Handles a fire-and-forget message synchronously.</summary>
    public StatelessMessageHandlerBuilder<TMessage> On<TCase>(Action<TCase> handle)
        where TCase : TMessage
    {
        ArgumentNullException.ThrowIfNull(handle);
        _inner.On<TCase>((state, message) =>
        {
            handle(message);
            return state;
        });
        return this;
    }

    /// <summary>Handles a fire-and-forget message asynchronously.</summary>
    public StatelessMessageHandlerBuilder<TMessage> On<TCase>(Func<TCase, CancellationToken, ValueTask> handle)
        where TCase : TMessage
    {
        ArgumentNullException.ThrowIfNull(handle);
        _inner.On<TCase>(async (state, message, token) =>
        {
            await handle(message, token);
            return state;
        });
        return this;
    }

    /// <summary>Handles a request synchronously.</summary>
    public StatelessMessageHandlerBuilder<TMessage> On<TRequest, TReply>(Func<TRequest, TReply> handle)
        where TRequest : TMessage, IRequest<TReply>
    {
        ArgumentNullException.ThrowIfNull(handle);
        _inner.On<TRequest, TReply>((state, request) => (state, handle(request)));
        return this;
    }

    /// <summary>Handles a request synchronously with a reply or rejection.</summary>
    public StatelessMessageHandlerBuilder<TMessage> On<TRequest, TReply>(Func<TRequest, OperationResult<TReply>> handle)
        where TRequest : TMessage, IRequest<TReply>
    {
        ArgumentNullException.ThrowIfNull(handle);
        _inner.On<TRequest, TReply>((state, request) => (state, handle(request)));
        return this;
    }

    /// <summary>Handles a request asynchronously; the token also fires if the caller cancels.</summary>
    public StatelessMessageHandlerBuilder<TMessage> On<TRequest, TReply>(Func<TRequest, CancellationToken, ValueTask<TReply>> handle)
        where TRequest : TMessage, IRequest<TReply>
    {
        ArgumentNullException.ThrowIfNull(handle);
        _inner.On<TRequest, TReply>(async (state, request, token) => (state, await handle(request, token)));
        return this;
    }

    /// <summary>Handles a request asynchronously with a reply or rejection; the token also fires if the caller cancels.</summary>
    public StatelessMessageHandlerBuilder<TMessage> On<TRequest, TReply>(
        Func<TRequest, CancellationToken, ValueTask<OperationResult<TReply>>> handle)
        where TRequest : TMessage, IRequest<TReply>
    {
        ArgumentNullException.ThrowIfNull(handle);
        _inner.On<TRequest, TReply>(async (state, request, token) => (state, await handle(request, token)));
        return this;
    }

    /// <summary>Handles any message without a route. Without this, an unrouted message is a failure.</summary>
    public StatelessMessageHandlerBuilder<TMessage> Otherwise(Func<Envelope<TMessage>, CancellationToken, ValueTask> handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        _inner.Otherwise(async (state, envelope, token) =>
        {
            await handle(envelope, token);
            return state;
        });
        return this;
    }

    /// <summary>Runs before the first message.</summary>
    public StatelessMessageHandlerBuilder<TMessage> OnStarted(Func<CancellationToken, ValueTask> hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        _inner.OnStarted(async (state, token) =>
        {
            await hook(token);
            return state;
        });
        return this;
    }

    /// <summary>Runs once when the agent terminates.</summary>
    public StatelessMessageHandlerBuilder<TMessage> OnStopped(Func<CancellationToken, ValueTask> hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        _inner.OnStopped((_, token) => hook(token));
        return this;
    }

    /// <summary>Builds the handler.</summary>
    public IMessageHandler<TMessage> Build() => new StatelessRoutedHandler<TMessage>(_inner.Build());
}

internal delegate ValueTask<TState> Route<TState, TMessage>(TState state, Envelope<TMessage> envelope, CancellationToken cancellationToken);

/// <summary>Runs request handlers that opted into caller cancellation.</summary>
internal static class CallerCancellable
{
    public static ValueTask<TState> InvokeAsync<TState, TMessage, TRequest, TResult>(
        TState state,
        Envelope<TMessage> envelope,
        CancellationToken agentToken,
        Func<TState, TRequest, CancellationToken, ValueTask<(TState State, TResult Result)>> handle,
        Action<IReplyChannel?, TResult> reply)
    {
        var request = (TRequest)(object)envelope.Message!;
        if (envelope.ReplyTo is { Cancellation.CanBeCanceled: true } channel)
        {
            return LinkedAsync(state, request, channel, agentToken, handle, reply);
        }

        var pending = handle(state, request, agentToken);
        if (pending.IsCompletedSuccessfully)
        {
            var (next, result) = pending.Result;
            reply(envelope.ReplyTo, result);
            return new ValueTask<TState>(next);
        }

        return AwaitAsync(pending, envelope.ReplyTo, reply);
    }

    // No ConfigureAwait(false): these run on the agent loop and must stay in its context.
    private static async ValueTask<TState> LinkedAsync<TState, TRequest, TResult>(
        TState state,
        TRequest request,
        IReplyChannel channel,
        CancellationToken agentToken,
        Func<TState, TRequest, CancellationToken, ValueTask<(TState State, TResult Result)>> handle,
        Action<IReplyChannel?, TResult> reply)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(agentToken, channel.Cancellation);
        var (next, result) = await handle(state, request, linked.Token);
        reply(channel, result);
        return next;
    }

    private static async ValueTask<TState> AwaitAsync<TState, TResult>(
        ValueTask<(TState State, TResult Result)> pending,
        IReplyChannel? channel,
        Action<IReplyChannel?, TResult> reply)
    {
        var (next, result) = await pending;
        reply(channel, result);
        return next;
    }
}

internal sealed class RoutedHandler<TState, TMessage>(
    FrozenDictionary<Type, Route<TState, TMessage>> exact,
    Route<TState, TMessage>? otherwise,
    Func<TState, CancellationToken, ValueTask<TState>>? onStarted,
    Func<TState, Exception, CancellationToken, ValueTask<TState>>? onRestarting,
    Func<TState, CancellationToken, ValueTask>? onStopped)
    : IMessageHandler<TState, TMessage>, IAgentLifecycle<TState>
{
    private readonly ConcurrentDictionary<Type, Route<TState, TMessage>?> _resolved = new();

    public ValueTask<TState> HandleAsync(TState state, Envelope<TMessage> envelope, CancellationToken cancellationToken)
    {
        var type = typeof(TMessage).IsValueType ? typeof(TMessage) : envelope.Message!.GetType();
        if (!exact.TryGetValue(type, out var route))
        {
            route = _resolved.GetOrAdd(type, Resolve);
        }

        route ??= otherwise ?? throw new UnhandledMessageException(type);
        return route(state, envelope, cancellationToken);
    }

    public ValueTask<TState> OnStartedAsync(TState state, CancellationToken cancellationToken)
        => onStarted?.Invoke(state, cancellationToken) ?? new(state);

    public ValueTask<TState> OnRestartingAsync(TState initialState, Exception cause, CancellationToken cancellationToken)
        => onRestarting?.Invoke(initialState, cause, cancellationToken) ?? new(initialState);

    public ValueTask OnStoppedAsync(TState finalState, CancellationToken cancellationToken)
        => onStopped?.Invoke(finalState, cancellationToken) ?? ValueTask.CompletedTask;

    private Route<TState, TMessage>? Resolve(Type type)
    {
        var candidates = exact.Keys.Where(registered => registered.IsAssignableFrom(type)).ToArray();
        var mostDerived = candidates
            .Where(candidate => !candidates.Any(other => other != candidate && candidate.IsAssignableFrom(other)))
            .ToArray();

        return mostDerived.Length switch
        {
            0 => null,
            1 => exact[mostDerived[0]],
            _ => (_, _, _) => throw new UnhandledMessageException(
                type,
                $"It matches several routes ({string.Join(", ", mostDerived.Select(match => match.Name))}); register one for it explicitly."),
        };
    }
}

/// <summary>A stateless routed handler that also exposes its stateful core, so agents skip the adapter.</summary>
internal sealed class StatelessRoutedHandler<TMessage>(IMessageHandler<Unit, TMessage> inner)
    : IMessageHandler<TMessage>, IMessageHandler<Unit, TMessage>, IAgentLifecycle<Unit>
{
    private readonly IAgentLifecycle<Unit> _lifecycle = (IAgentLifecycle<Unit>)inner;

    public ValueTask HandleAsync(Envelope<TMessage> envelope, CancellationToken cancellationToken)
    {
        var pending = inner.HandleAsync(Unit.Value, envelope, cancellationToken);
        return pending.IsCompletedSuccessfully ? default : new ValueTask(pending.AsTask());
    }

    public ValueTask<Unit> HandleAsync(Unit state, Envelope<TMessage> envelope, CancellationToken cancellationToken)
        => inner.HandleAsync(state, envelope, cancellationToken);

    public ValueTask<Unit> OnStartedAsync(Unit state, CancellationToken cancellationToken)
        => _lifecycle.OnStartedAsync(state, cancellationToken);

    public ValueTask<Unit> OnRestartingAsync(Unit initialState, Exception cause, CancellationToken cancellationToken)
        => _lifecycle.OnRestartingAsync(initialState, cause, cancellationToken);

    public ValueTask OnStoppedAsync(Unit finalState, CancellationToken cancellationToken)
        => _lifecycle.OnStoppedAsync(finalState, cancellationToken);
}
