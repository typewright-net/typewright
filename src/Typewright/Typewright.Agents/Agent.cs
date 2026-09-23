using Typewright.Validation;

namespace Typewright.Agents;

/// <summary>An agent's lifecycle state.</summary>
public enum AgentStatus
{
    /// <summary>Constructed; messages posted now are buffered until <c>Start</c>.</summary>
    Created,

    /// <summary>Processing messages.</summary>
    Running,

    /// <summary>Stop or abort requested; finishing up.</summary>
    Stopping,

    /// <summary>Stopped gracefully after draining its mailbox.</summary>
    Stopped,

    /// <summary>Aborted: in-flight work was cancelled and queued requests failed.</summary>
    Aborted,

    /// <summary>Stopped by its failure policy (or a failing lifecycle hook); <c>Completion</c> is faulted.</summary>
    Failed,

    /// <summary>Stopped by an <see cref="FailureDecision.Escalate"/> decision; <c>Completion</c> is faulted.</summary>
    Escalated,
}

/// <summary>Configuration shared by any number of agents (factories and policies are reused, not shared state).</summary>
/// <typeparam name="TMessage">The agents' message type.</typeparam>
public sealed record AgentOptions<TMessage>
{
    /// <summary>Options with every default.</summary>
    public static AgentOptions<TMessage> Default { get; } = new();

    /// <summary>A name for diagnostics, metrics, and traces; generated when null.</summary>
    public string? Name { get; init; }

    /// <summary>Creates each agent's mailbox. Default: an unbounded <see cref="ChannelMailbox{TMessage}"/>.</summary>
    public Func<IMailbox<TMessage>> Mailbox { get; init; } = static () => new ChannelMailbox<TMessage>();

    /// <summary>Where the loop runs. Default: <see cref="ThreadPoolDispatcher"/>.</summary>
    public IDispatcher Dispatcher { get; init; } = ThreadPoolDispatcher.Instance;

    /// <summary>How failures are handled. Default: resume with the pre-failure state.</summary>
    public IFailurePolicy<TMessage> FailurePolicy { get; init; } = global::Typewright.Agents.FailurePolicy.Resume<TMessage>();

    /// <summary>The interceptor pipeline around the handler, outermost first. Default: empty.</summary>
    public IReadOnlyList<IMessageInterceptor<TMessage>> Interceptors { get; init; } = [];

    /// <summary>
    /// Skip requests whose reply channel is already settled (caller cancelled) when they
    /// are dequeued. Default: true. Turn off when every accepted command must be applied.
    /// </summary>
    public bool SkipCancelledRequests { get; init; } = true;

    /// <summary>How long <c>DisposeAsync</c> lets a graceful stop run before aborting. Default: 5 seconds.</summary>
    public TimeSpan StopTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>The clock for failure windows and timeouts. Default: <see cref="TimeProvider.System"/>.</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
}

/// <summary>The client and lifecycle surface of an agent.</summary>
/// <typeparam name="TMessage">The agent's message type.</typeparam>
public interface IAgent<TMessage> : IMessageTarget<TMessage>, IEnvelopeTarget<TMessage>, IAsyncDisposable
{
    /// <summary>The agent's diagnostic name.</summary>
    string Name { get; }

    /// <summary>The agent's lifecycle state.</summary>
    AgentStatus Status { get; }

    /// <summary>
    /// Completes when the agent terminates: successfully when stopped or aborted, faulted
    /// with the failure when stopped by its failure policy or a lifecycle hook.
    /// </summary>
    Task Completion { get; }

    /// <summary>The number of queued messages (approximate).</summary>
    int MailboxCount { get; }

    /// <summary>Starts processing; messages posted before this were buffered.</summary>
    void Start();

    /// <summary>
    /// Stops accepting messages, processes everything already queued, then terminates. If
    /// <paramref name="cancellationToken"/> fires first, aborts instead. Does not rethrow
    /// the agent's failure; observe <see cref="Completion"/> for that.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>Cancels in-flight work, fails every queued request, and terminates.</summary>
    Task AbortAsync();
}

/// <summary>Factories for agents.</summary>
public static class Agent
{
    /// <summary>Creates and starts a stateful agent.</summary>
    public static Agent<TState, TMessage> Start<TState, TMessage>(
        TState initialState,
        IMessageHandler<TState, TMessage> handler,
        AgentOptions<TMessage>? options = null)
    {
        var agent = new Agent<TState, TMessage>(initialState, handler, options);
        agent.Start();
        return agent;
    }

    /// <summary>Creates and starts a stateless agent.</summary>
    public static IAgent<TMessage> Start<TMessage>(IMessageHandler<TMessage> handler, AgentOptions<TMessage>? options = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Start(Unit.Value, new StatelessHandlerAdapter<TMessage>(handler), options);
    }
}

/// <summary>
/// A mailbox processor: processes one message at a time, in order, threading
/// <typeparamref name="TState"/> through its handler. Its last committed state is readable
/// from any thread through <see cref="CurrentState"/> without going through the mailbox.
/// </summary>
/// <typeparam name="TState">The agent's state; should be immutable.</typeparam>
/// <typeparam name="TMessage">The agent's message type.</typeparam>
public sealed class Agent<TState, TMessage> : IAgent<TMessage>
{
    private const int PhaseCreated = 0;
    private const int PhaseRunning = 1;
    private const int PhaseStopping = 2;
    private const int PhaseTerminated = 3;

    private static long s_nextId;

    private readonly TState _initialState;
    private readonly IMessageHandler<TState, TMessage> _handler;
    private readonly IAgentLifecycle<TState>? _lifecycle;
    private readonly AgentOptions<TMessage> _options;
    private readonly IMailbox<TMessage> _mailbox;
    private readonly MessageDelegate<TMessage> _pipeline;
    private readonly FailureHistory _failures;
    private readonly CancellationTokenSource _abort = new();
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock _publishLock = new();

    private int _phase;
    private volatile bool _abortRequested;
    private AgentStatus _terminalStatus;

    // Owned by the loop: the committed state, and the in-flight message's result.
    private TState _state;
    private TState _pending;

    // Guarded by _publishLock: the committed state as seen by other threads.
    private TState _published;

    /// <summary>Creates an agent; call <see cref="Start"/> to begin processing.</summary>
    public Agent(TState initialState, IMessageHandler<TState, TMessage> handler, AgentOptions<TMessage>? options = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _options = options ?? AgentOptions<TMessage>.Default;
        _initialState = initialState;
        _state = initialState;
        _pending = initialState;
        _published = initialState;
        _handler = handler;
        _lifecycle = handler as IAgentLifecycle<TState>;
        Name = _options.Name ?? $"{typeof(TMessage).Name}#{Interlocked.Increment(ref s_nextId)}";
        _mailbox = _options.Mailbox() ?? throw new InvalidOperationException("The mailbox factory returned null.");
        _failures = new FailureHistory(_options.TimeProvider);
        _pipeline = BuildPipeline(_options.Interceptors);
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public AgentStatus Status => Volatile.Read(ref _phase) switch
    {
        PhaseCreated => AgentStatus.Created,
        PhaseRunning => AgentStatus.Running,
        PhaseStopping => AgentStatus.Stopping,
        _ => _terminalStatus,
    };

    /// <inheritdoc />
    public Task Completion => _completion.Task;

    /// <inheritdoc />
    public int MailboxCount => _mailbox.Count;

    /// <summary>
    /// The last committed state: the initial state, the result of the last successfully
    /// handled message, or the state after a restart. Never a partially applied state.
    /// Reading it does not go through the mailbox.
    /// </summary>
    public TState CurrentState
    {
        get
        {
            lock (_publishLock)
            {
                return _published;
            }
        }
    }

    /// <inheritdoc />
    public void Start()
    {
        if (Interlocked.CompareExchange(ref _phase, PhaseRunning, PhaseCreated) != PhaseCreated)
        {
            throw new InvalidOperationException($"The agent '{Name}' has already been started or stopped.");
        }

        Task loop;
        try
        {
            loop = _options.Dispatcher.Run(RunAsync);
        }
        catch (Exception exception)
        {
            TerminateWithoutLoop(exception);
            throw;
        }

        loop.ContinueWith(
            static (task, state) => ((Agent<TState, TMessage>)state!).OnLoopExited(task),
            this,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _phase, PhaseStopping, PhaseCreated) == PhaseCreated)
        {
            TerminateWithoutLoop(fault: null);
            return;
        }

        Interlocked.CompareExchange(ref _phase, PhaseStopping, PhaseRunning);
        _mailbox.Complete();
        await Completion.WaitAsync(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (!Completion.IsCompleted)
        {
            await AbortAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task AbortAsync()
    {
        _abortRequested = true;
        if (Interlocked.CompareExchange(ref _phase, PhaseStopping, PhaseCreated) == PhaseCreated)
        {
            TerminateWithoutLoop(fault: null);
            return;
        }

        Interlocked.CompareExchange(ref _phase, PhaseStopping, PhaseRunning);
        _mailbox.Complete();
        _abort.Cancel();
        await Completion.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }

    /// <summary>Stops gracefully, aborting if the stop takes longer than <see cref="AgentOptions{TMessage}.StopTimeout"/>.</summary>
    public async ValueTask DisposeAsync()
    {
        using var timeout = new CancellationTokenSource(_options.StopTimeout, _options.TimeProvider);
        await StopAsync(timeout.Token).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public bool TryPost(TMessage message) => Delivery.TryPost(this, message);

    /// <inheritdoc />
    public ValueTask PostAsync(TMessage message, CancellationToken cancellationToken = default)
        => Delivery.PostAsync(this, message, headers: null, cancellationToken);

    /// <inheritdoc />
    public Task<TReply> AskAsync<TReply>(IRequest<TReply> request, CancellationToken cancellationToken = default)
        => Delivery.AskAsync(this, request, headers: null, cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult<TReply>> TryAskAsync<TReply>(IRequest<TReply> request, CancellationToken cancellationToken = default)
        => Delivery.TryAskAsync(this, request, headers: null, cancellationToken);

    /// <inheritdoc />
    public bool TryDeliver(Envelope<TMessage> envelope) => _mailbox.TryPost(envelope) == PostResult.Posted;

    /// <inheritdoc />
    public ValueTask DeliverAsync(Envelope<TMessage> envelope, CancellationToken cancellationToken = default)
    {
        var pending = _mailbox.PostAsync(envelope, cancellationToken);
        return pending.IsCompletedSuccessfully ? ToDelivery(pending.Result) : AwaitDeliveryAsync(pending);
    }

    /// <inheritdoc />
    public override string ToString() => $"Agent '{Name}' ({Status})";

    private MessageDelegate<TMessage> BuildPipeline(IReadOnlyList<IMessageInterceptor<TMessage>> interceptors)
    {
        MessageDelegate<TMessage> pipeline = InvokeHandlerAsync;
        for (var i = interceptors.Count - 1; i >= 0; i--)
        {
            var interceptor = interceptors[i] ?? throw new InvalidOperationException("Interceptors must not be null.");
            var next = pipeline;
            pipeline = context => interceptor.InvokeAsync(context, next);
        }

        return pipeline;
    }

    private ValueTask InvokeHandlerAsync(MessageContext<TMessage> context)
    {
        var handled = _handler.HandleAsync(_pending, context.Envelope, context.CancellationToken);
        if (handled.IsCompletedSuccessfully)
        {
            _pending = handled.Result;
            return default;
        }

        return AwaitHandlerAsync(handled);
    }

    private async ValueTask AwaitHandlerAsync(ValueTask<TState> handled) => _pending = await handled;

    // The loop never uses ConfigureAwait(false): every continuation returns to the
    // context the dispatcher established.
    private async Task RunAsync()
    {
        var token = _abort.Token;
        Exception? fault = null;
        var escalated = false;
        try
        {
            if (_lifecycle is not null)
            {
                _state = await _lifecycle.OnStartedAsync(_state, token);
                Publish(_state);
            }

            while (fault is null && await _mailbox.WaitToReadAsync(token))
            {
                while (fault is null && _mailbox.TryTake(out var envelope))
                {
                    if (token.IsCancellationRequested)
                    {
                        envelope.ReplyTo?.TryFail(new AgentStoppedException(Name));
                        token.ThrowIfCancellationRequested();
                    }

                    if (_options.SkipCancelledRequests && envelope.ReplyTo is { IsCompleted: true })
                    {
                        AgentDiagnostics.RecordSkipped(Name);
                        continue;
                    }

                    (fault, escalated) = await ProcessAsync(envelope, token);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Aborted.
        }
        catch (Exception exception)
        {
            // A lifecycle hook failed.
            fault = exception;
        }
        finally
        {
            await TerminateAsync(fault, escalated);
        }
    }

    private async ValueTask<(Exception? Fault, bool Escalated)> ProcessAsync(Envelope<TMessage> envelope, CancellationToken token)
    {
        _pending = _state;
        try
        {
            await _pipeline(new MessageContext<TMessage>(envelope, Name, token));
            _state = _pending;
            Publish(_state);
            _failures.RecordSuccess();
            return default;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            envelope.ReplyTo?.TryFail(new AgentStoppedException(Name));
            throw;
        }
        catch (OperationCanceledException) when (envelope.ReplyTo is { Cancellation.IsCancellationRequested: true })
        {
            // The caller abandoned the request and the handler honored it: a cancellation,
            // not a failure. The state is not committed and the failure policy is not consulted.
            envelope.ReplyTo?.TryCancel();
            return default;
        }
        catch (Exception exception)
        {
            envelope.ReplyTo?.TryFail(exception);
            _failures.RecordFailure();

            FailureDecision decision;
            try
            {
                decision = _options.FailurePolicy.OnFailure(envelope, exception, new FailureContext(_failures, Name));
            }
            catch (Exception policyFailure)
            {
                return (policyFailure, false);
            }

            switch (decision)
            {
                case FailureDecision.Resume:
                    return default;

                case FailureDecision.Restart:
                    _failures.RecordRestart();
                    _state = _lifecycle is null
                        ? _initialState
                        : await _lifecycle.OnRestartingAsync(_initialState, exception, token);
                    Publish(_state);
                    return default;

                case FailureDecision.Escalate:
                    return (exception, true);

                default:
                    return (exception, false);
            }
        }
    }

    private async Task TerminateAsync(Exception? fault, bool escalated)
    {
        _mailbox.Complete();
        FailUndelivered();

        if (_lifecycle is not null)
        {
            try
            {
                await _lifecycle.OnStoppedAsync(_state, _abort.Token);
            }
            catch (OperationCanceledException) when (_abort.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                fault ??= exception;
            }
        }

        Complete(fault, escalated);
    }

    private void TerminateWithoutLoop(Exception? fault)
    {
        _mailbox.Complete();
        FailUndelivered();
        Complete(fault, escalated: false);
    }

    private void OnLoopExited(Task loop)
    {
        if (!_completion.Task.IsCompleted)
        {
            // The loop died outside its own handling (e.g. the dispatcher failed).
            TerminateWithoutLoop(loop.Exception?.InnerException
                ?? new InvalidOperationException($"The loop of agent '{Name}' ended unexpectedly."));
        }
    }

    private void FailUndelivered()
    {
        while (_mailbox.TryTake(out var undelivered))
        {
            undelivered.ReplyTo?.TryFail(new AgentStoppedException(Name));
        }
    }

    private void Complete(Exception? fault, bool escalated)
    {
        _terminalStatus = fault is not null
            ? escalated ? AgentStatus.Escalated : AgentStatus.Failed
            : _abortRequested ? AgentStatus.Aborted : AgentStatus.Stopped;
        Volatile.Write(ref _phase, PhaseTerminated);

        if (fault is null)
        {
            _completion.TrySetResult();
        }
        else
        {
            _completion.TrySetException(fault);
        }
    }

    private void Publish(TState state)
    {
        if (typeof(TState) == typeof(Unit))
        {
            return;
        }

        lock (_publishLock)
        {
            _published = state;
        }
    }

    private ValueTask ToDelivery(PostResult result) => result switch
    {
        PostResult.Posted => default,
        PostResult.Full => ValueTask.FromException(new MailboxFullException(Name)),
        _ => ValueTask.FromException(new AgentStoppedException(Name)),
    };

    private async ValueTask AwaitDeliveryAsync(ValueTask<PostResult> pending)
        => await ToDelivery(await pending.ConfigureAwait(false)).ConfigureAwait(false);
}
