using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Typewright.Agents;

namespace Typewright.Actors;

/// <summary>Registry, factory, and supervisor of addressable actors.</summary>
public interface IActorSystem : IAsyncDisposable
{
    /// <summary>Spawns an actor at <paramref name="path"/>; throws <see cref="ActorAlreadyExistsException"/> if one exists.</summary>
    IActorRef<TMessage> Spawn<TState, TMessage>(ActorPath path, ActorDefinition<TState, TMessage> definition);

    /// <summary>
    /// Returns the actor at <paramref name="path"/>, spawning it from
    /// <paramref name="definition"/> if there is none; concurrent callers get the same
    /// actor. This is the per-resource-key entry point. Throws if the existing actor has
    /// a different state or message type.
    /// </summary>
    IActorRef<TMessage> GetOrSpawn<TState, TMessage>(ActorPath path, ActorDefinition<TState, TMessage> definition);

    /// <summary>The actor at <paramref name="path"/> if it exists and accepts <typeparamref name="TMessage"/>; otherwise null.</summary>
    IActorRef<TMessage>? Resolve<TMessage>(ActorPath path);

    /// <summary>The paths of the running actors.</summary>
    IReadOnlyCollection<ActorPath> Actors { get; }

    /// <summary>
    /// Stops the actor at <paramref name="path"/> and every actor beneath it, deepest
    /// first, each draining its mailbox; aborts whatever is still running if
    /// <paramref name="cancellationToken"/> fires first.
    /// </summary>
    Task StopAsync(ActorPath path, CancellationToken cancellationToken = default);

    /// <summary>Stops every actor, deepest first; the system accepts no new actors afterwards.</summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}

/// <summary>System-wide settings.</summary>
public sealed class ActorSystemOptions
{
    /// <summary>
    /// Supervision for actors whose parent defines none, including top-level actors.
    /// Default: restart up to 3 times per minute, then stop.
    /// </summary>
    public SupervisionStrategy DefaultSupervision { get; set; } = SupervisionStrategy.Restart(3, TimeSpan.FromMinutes(1));

    /// <summary>Add a <see cref="TracingInterceptor{TMessage}"/> to every actor. Default: true.</summary>
    public bool EnableTracing { get; set; } = true;

    /// <summary>Add a <see cref="MetricsInterceptor{TMessage}"/> to every actor. Default: true.</summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>The clock for header timestamps and supervision windows.</summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    /// <summary>How long <c>DisposeAsync</c> lets actors drain before aborting them. Default: 30 seconds.</summary>
    public TimeSpan StopTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>The default <see cref="IActorSystem"/>: in-process, one agent per actor.</summary>
public sealed partial class ActorSystem : IActorSystem, IDisposable
{
    private readonly ConcurrentDictionary<ActorPath, ActorCell> _cells = new();
    private readonly ILogger _logger;
    private int _stopping;

    /// <summary>Creates a system.</summary>
    /// <param name="options">System-wide settings; defaults when null.</param>
    /// <param name="services">Services handed to actors through their context; empty when null.</param>
    /// <param name="logger">Lifecycle and supervision logging; silent when null.</param>
    public ActorSystem(ActorSystemOptions? options = null, IServiceProvider? services = null, ILogger<ActorSystem>? logger = null)
    {
        Options = options ?? new ActorSystemOptions();
        Services = services ?? EmptyServices.Instance;
        _logger = logger ?? NullLogger<ActorSystem>.Instance;
    }

    internal ActorSystemOptions Options { get; }

    internal IServiceProvider Services { get; }

    internal bool IsStopping => Volatile.Read(ref _stopping) != 0;

    /// <inheritdoc />
    public IReadOnlyCollection<ActorPath> Actors => [.. _cells.Keys];

    /// <inheritdoc />
    public IActorRef<TMessage> Spawn<TState, TMessage>(ActorPath path, ActorDefinition<TState, TMessage> definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ThrowIfStopping();
        var cell = new ActorCell<TState, TMessage>(this, path, definition);
        if (!_cells.TryAdd(path, cell))
        {
            throw new ActorAlreadyExistsException(path);
        }

        cell.EnsureStarted();
        return cell.Ref;
    }

    /// <inheritdoc />
    public IActorRef<TMessage> GetOrSpawn<TState, TMessage>(ActorPath path, ActorDefinition<TState, TMessage> definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ThrowIfStopping();

        // Creating a cell runs no user code; the winner's factories run once, in EnsureStarted.
        var cell = _cells.GetOrAdd(path, static (key, state) => new ActorCell<TState, TMessage>(state.System, key, state.Definition), (System: this, Definition: definition));
        if (cell is not ActorCell<TState, TMessage> typed)
        {
            throw new InvalidOperationException(
                $"The actor at '{path}' has state {cell.StateType.Name} and messages {cell.MessageType.Name}, "
                + $"not {typeof(TState).Name} and {typeof(TMessage).Name}.");
        }

        typed.EnsureStarted();
        return typed.Ref;
    }

    /// <inheritdoc />
    public IActorRef<TMessage>? Resolve<TMessage>(ActorPath path)
    {
        if (!_cells.TryGetValue(path, out var cell))
        {
            return null;
        }

        cell.EnsureStarted();
        return cell.UntypedRef as IActorRef<TMessage>;
    }

    /// <inheritdoc />
    public Task StopAsync(ActorPath path, CancellationToken cancellationToken = default)
        => StopCellsAsync(_cells.Values.Where(cell => cell.Path == path || cell.Path.IsDescendantOf(path)), cancellationToken);

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Exchange(ref _stopping, 1);
        return StopCellsAsync(_cells.Values, cancellationToken);
    }

    /// <summary>Stops every actor, aborting any still running after <see cref="ActorSystemOptions.StopTimeout"/>.</summary>
    public async ValueTask DisposeAsync()
    {
        using var timeout = new CancellationTokenSource(Options.StopTimeout, Options.TimeProvider);
        await StopAsync(timeout.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// Synchronous disposal for containers that dispose synchronously; blocks on
    /// <see cref="DisposeAsync"/>. Prefer <c>DisposeAsync</c>, and never call this from a
    /// thread an actor's dispatcher depends on.
    /// </summary>
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    /// <summary>
    /// Headers for a message sent through an actor reference: the correlation id of the
    /// message being handled (or a new one), the sending actor's path when sent from
    /// inside an actor, a timestamp, and the current trace context.
    /// </summary>
    internal MessageHeaders StampHeaders()
    {
        var scope = ActorScope.Current;
        var activity = Activity.Current;
        return new MessageHeaders
        {
            CorrelationId = scope?.Headers.CorrelationId ?? CorrelationId.New(),
            Sender = scope?.Self.Value,
            Timestamp = Options.TimeProvider.GetUtcNow(),
            TraceParent = activity is { IdFormat: ActivityIdFormat.W3C } ? activity.Id : scope?.Headers.TraceParent,
        };
    }

    internal void OnStarted(ActorCell cell) => LogStarted(_logger, cell.Path);

    internal void OnStopped(ActorCell cell)
    {
        if (_cells.TryRemove(new KeyValuePair<ActorPath, ActorCell>(cell.Path, cell)))
        {
            LogStopped(_logger, cell.Path);
        }
    }

    /// <summary>The cell's agent failed and stopped by its own policy: the actor and its subtree stop.</summary>
    internal void OnFailed(ActorCell cell, Exception exception)
    {
        LogFailed(_logger, exception, cell.Path);
        _ = StopSubtreeAsync(cell);
    }

    /// <summary>
    /// Applies the parent's supervision to <paramref name="failed"/>, whose agent escalated
    /// (or whose child escalated to it).
    /// </summary>
    internal void Supervise(ActorCell failed, Exception exception)
    {
        if (IsStopping)
        {
            OnStopped(failed);
            return;
        }

        var parent = failed.Path.Parent is { } parentPath && _cells.TryGetValue(parentPath, out var parentCell) ? parentCell : null;
        var strategy = parent?.ChildSupervision ?? Options.DefaultSupervision;

        SupervisionDirective directive;
        try
        {
            directive = strategy.Decide(new ChildFailure(failed.Path, exception, failed.Restarts, Options.TimeProvider));
        }
        catch (Exception strategyFailure)
        {
            LogStrategyFailed(_logger, strategyFailure, failed.Path);
            directive = SupervisionDirective.Stop;
        }

        LogSupervised(_logger, exception, failed.Path, directive);
        switch (directive)
        {
            case SupervisionDirective.Resume:
            case SupervisionDirective.Restart:
                failed.Recover(resume: directive == SupervisionDirective.Resume);
                break;

            case SupervisionDirective.Escalate when parent is not null:
                _ = StopSubtreeAsync(failed);
                Supervise(parent, new ActorEscalationException(failed.Path, exception));
                break;

            default:
                if (directive == SupervisionDirective.Escalate)
                {
                    LogUnhandledEscalation(_logger, exception, failed.Path);
                }

                _ = StopSubtreeAsync(failed);
                break;
        }
    }

    internal void OnRecoveryFailed(ActorCell cell, Exception exception)
    {
        LogRecoveryFailed(_logger, exception, cell.Path);
        _ = StopSubtreeAsync(cell);
    }

    /// <summary>
    /// Stops <paramref name="cell"/> (whether or not its agent is still running) and every
    /// actor beneath it. The cell is passed explicitly because it may already have left
    /// the registry.
    /// </summary>
    private Task StopSubtreeAsync(ActorCell cell)
        => StopCellsAsync(
            _cells.Values.Where(other => other.Path.IsDescendantOf(cell.Path)).Append(cell).Distinct(),
            CancellationToken.None);

    private static async Task StopCellsAsync(IEnumerable<ActorCell> cells, CancellationToken cancellationToken)
    {
        foreach (var level in cells.GroupBy(cell => cell.Path.Depth).OrderByDescending(level => level.Key))
        {
            await Task.WhenAll(level.Select(cell => cell.StopAsync(cancellationToken))).ConfigureAwait(false);
        }
    }

    private void ThrowIfStopping()
    {
        if (IsStopping)
        {
            throw new InvalidOperationException("The actor system is stopping and accepts no new actors.");
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Actor {Path} started.")]
    private static partial void LogStarted(ILogger logger, ActorPath path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Actor {Path} stopped.")]
    private static partial void LogStopped(ILogger logger, ActorPath path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Actor {Path} failed and stopped, with everything beneath it.")]
    private static partial void LogFailed(ILogger logger, Exception exception, ActorPath path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Actor {Path} escalated a failure; supervision decided {Directive}.")]
    private static partial void LogSupervised(ILogger logger, Exception exception, ActorPath path, SupervisionDirective directive);

    [LoggerMessage(Level = LogLevel.Error, Message = "The supervision strategy for {Path} threw; stopping the actor.")]
    private static partial void LogStrategyFailed(ILogger logger, Exception exception, ActorPath path);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Actor {Path} escalated past the top of the hierarchy; stopping it.")]
    private static partial void LogUnhandledEscalation(ILogger logger, Exception exception, ActorPath path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Recreating actor {Path} failed; stopping it.")]
    private static partial void LogRecoveryFailed(ILogger logger, Exception exception, ActorPath path);

    private sealed class EmptyServices : IServiceProvider
    {
        public static EmptyServices Instance { get; } = new();

        public object? GetService(Type serviceType) => null;
    }
}

/// <summary>One actor path's slot: owns the current agent and recreates it under supervision.</summary>
internal abstract class ActorCell(ActorPath path)
{
    private readonly List<long> _restarts = [];

    public ActorPath Path { get; } = path;

    public abstract Type StateType { get; }

    public abstract Type MessageType { get; }

    public abstract IActorRef UntypedRef { get; }

    public abstract SupervisionStrategy? ChildSupervision { get; }

    public IReadOnlyList<long> Restarts
    {
        get
        {
            lock (_restarts)
            {
                return [.. _restarts];
            }
        }
    }

    public abstract void EnsureStarted();

    public abstract void Recover(bool resume);

    public abstract Task StopAsync(CancellationToken cancellationToken);

    protected void RecordRestart(long timestamp)
    {
        lock (_restarts)
        {
            _restarts.Add(timestamp);
        }
    }
}

internal sealed class ActorCell<TState, TMessage> : ActorCell
{
    private readonly ActorSystem _system;
    private readonly ActorDefinition<TState, TMessage> _definition;
    private readonly ActorContext<TMessage> _context;
    private readonly Lock _lock = new();
    private Agent<TState, TMessage>? _agent;
    private bool _stopping;

    public ActorCell(ActorSystem system, ActorPath path, ActorDefinition<TState, TMessage> definition)
        : base(path)
    {
        _system = system;
        _definition = definition;
        Ref = new ActorRef<TState, TMessage>(this);
        _context = new ActorContext<TMessage>(path, system, new SelfSink<TMessage>(Ref));
    }

    public ActorRef<TState, TMessage> Ref { get; }

    public ActorSystem System => _system;

    /// <summary>The current agent; an actor that never started (it was stopped first) reads as stopped.</summary>
    public Agent<TState, TMessage> Agent
        => Volatile.Read(ref _agent) ?? throw new AgentStoppedException(Path.Value);

    public override Type StateType => typeof(TState);

    public override Type MessageType => typeof(TMessage);

    public override IActorRef UntypedRef => Ref;

    public override SupervisionStrategy? ChildSupervision => _definition.ChildSupervision;

    public override void EnsureStarted()
    {
        if (Volatile.Read(ref _agent) is not null)
        {
            return;
        }

        lock (_lock)
        {
            if (_agent is not null || _stopping)
            {
                return;
            }

            try
            {
                StartAgent(_definition.InitialState(_context));
            }
            catch
            {
                _system.OnStopped(this);
                throw;
            }
        }

        _system.OnStarted(this);
    }

    public override void Recover(bool resume)
    {
        Agent<TState, TMessage>? replaced;
        lock (_lock)
        {
            replaced = _agent;
            if (_stopping || replaced is null)
            {
                return;
            }

            // A parent recovering from a child's escalation is still running: resuming it
            // means leaving it be.
            if (resume && replaced.Status is AgentStatus.Running)
            {
                return;
            }

            try
            {
                StartAgent(resume ? replaced.CurrentState : _definition.InitialState(_context));
                RecordRestart(_system.Options.TimeProvider.GetTimestamp());
            }
            catch (Exception exception)
            {
                _system.OnRecoveryFailed(this, exception);
                return;
            }
        }

        if (replaced.Status is AgentStatus.Running or AgentStatus.Created)
        {
            _ = replaced.AbortAsync();
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        Agent<TState, TMessage>? agent;
        lock (_lock)
        {
            _stopping = true;
            agent = _agent;
        }

        if (agent is not null)
        {
            await agent.StopAsync(cancellationToken).ConfigureAwait(false);
        }

        _system.OnStopped(this);
    }

    // Called under _lock.
    private void StartAgent(TState initialState)
    {
        var agent = new Agent<TState, TMessage>(initialState, _definition.Handler(_context), BuildOptions());
        Volatile.Write(ref _agent, agent);
        agent.Start();
        agent.Completion.ContinueWith(
            static (_, state) =>
            {
                var (cell, terminated) = ((ActorCell<TState, TMessage>, Agent<TState, TMessage>))state!;
                cell.OnTerminated(terminated);
            },
            (this, agent),
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
    }

    private AgentOptions<TMessage> BuildOptions()
    {
        var configured = _definition.Options ?? AgentOptions<TMessage>.Default;
        var interceptors = new List<IMessageInterceptor<TMessage>> { new ActorScopeInterceptor<TMessage>(Path) };
        if (_system.Options.EnableTracing)
        {
            interceptors.Add(new TracingInterceptor<TMessage>());
        }

        if (_system.Options.EnableMetrics)
        {
            interceptors.Add(new MetricsInterceptor<TMessage>());
        }

        interceptors.AddRange(configured.Interceptors);
        return configured with { Name = Path.Value, Interceptors = interceptors };
    }

    private void OnTerminated(Agent<TState, TMessage> terminated)
    {
        bool stopping;
        lock (_lock)
        {
            if (!ReferenceEquals(_agent, terminated))
            {
                return;   // superseded by a recovery
            }

            stopping = _stopping;
        }

        var fault = terminated.Completion.Exception?.InnerException;
        if (stopping || _system.IsStopping)
        {
            _system.OnStopped(this);
        }
        else if (terminated.Status == AgentStatus.Escalated && fault is not null)
        {
            _system.Supervise(this, fault);
        }
        else if (fault is not null)
        {
            _system.OnFailed(this, fault);
        }
        else
        {
            _system.OnStopped(this);
        }
    }
}

/// <summary>The actor whose message is being handled on the current async flow.</summary>
internal sealed record ActorScope(ActorPath Self, MessageHeaders Headers)
{
    private static readonly AsyncLocal<ActorScope?> CurrentScope = new();

    public static ActorScope? Current
    {
        get => CurrentScope.Value;
        set => CurrentScope.Value = value;
    }
}

/// <summary>
/// Establishes the <see cref="ActorScope"/> while a message is handled, so references
/// used by the handler stamp this actor as the sender and continue its correlation id.
/// The change is confined to this call's async flow.
/// </summary>
internal sealed class ActorScopeInterceptor<TMessage>(ActorPath self) : IMessageInterceptor<TMessage>
{
    public async ValueTask InvokeAsync(MessageContext<TMessage> context, MessageDelegate<TMessage> next)
    {
        ActorScope.Current = new ActorScope(self, context.Envelope.Headers);
        await next(context);
    }
}
