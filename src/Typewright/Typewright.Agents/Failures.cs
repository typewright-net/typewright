namespace Typewright.Agents;

/// <summary>What an agent does after a message fails. The failed request's reply channel is always failed first.</summary>
public enum FailureDecision
{
    /// <summary>Continue with the state from before the failed message.</summary>
    Resume,

    /// <summary>Reset to the initial state (through <see cref="IAgentLifecycle{TState}.OnRestartingAsync"/>) and continue; queued messages are kept.</summary>
    Restart,

    /// <summary>Stop the agent: the <c>Completion</c> task faults with the exception and queued requests fail.</summary>
    Stop,

    /// <summary>Stop the agent and flag the stop as escalated, for a supervisor to act on.</summary>
    Escalate,
}

/// <summary>Decides how an agent responds to a failed message.</summary>
/// <typeparam name="TMessage">The agent's message type.</typeparam>
public interface IFailurePolicy<TMessage>
{
    /// <summary>Returns the decision for <paramref name="exception"/> thrown while handling <paramref name="envelope"/>.</summary>
    FailureDecision OnFailure(Envelope<TMessage> envelope, Exception exception, FailureContext context);
}

/// <summary>
/// An agent's failure history at the time of a failure (the current failure included),
/// so shared, stateless policies can express rate limits such as "restart at most three
/// times per minute".
/// </summary>
public readonly struct FailureContext
{
    private readonly FailureHistory _history;

    internal FailureContext(FailureHistory history, string agentName)
    {
        _history = history;
        AgentName = agentName;
    }

    /// <summary>The failing agent's name.</summary>
    public string AgentName { get; }

    /// <summary>Failures since the last successfully handled message, including this one.</summary>
    public int ConsecutiveFailures => _history.Consecutive;

    /// <summary>Failures over the agent's lifetime, including this one.</summary>
    public int TotalFailures => _history.Total;

    /// <summary>Restarts over the agent's lifetime.</summary>
    public int TotalRestarts => _history.TotalRestarts;

    /// <summary>Failures within the trailing <paramref name="window"/>, including this one (counts at most the last 64).</summary>
    public int FailuresWithin(TimeSpan window) => _history.Failures.CountWithin(window);

    /// <summary>Restarts within the trailing <paramref name="window"/> (counts at most the last 64).</summary>
    public int RestartsWithin(TimeSpan window) => _history.Restarts.CountWithin(window);
}

/// <summary>Built-in failure policies.</summary>
public static class FailurePolicy
{
    /// <summary>Always continue with the pre-failure state. The default.</summary>
    public static IFailurePolicy<TMessage> Resume<TMessage>() => DelegatePolicy<TMessage>.ResumeInstance;

    /// <summary>Stop the agent on the first failure.</summary>
    public static IFailurePolicy<TMessage> Stop<TMessage>() => DelegatePolicy<TMessage>.StopInstance;

    /// <summary>Stop the agent and escalate to a supervisor on the first failure.</summary>
    public static IFailurePolicy<TMessage> Escalate<TMessage>() => DelegatePolicy<TMessage>.EscalateInstance;

    /// <summary>
    /// Restart, at most <paramref name="maxRestarts"/> times within <paramref name="within"/>;
    /// beyond that, apply <paramref name="otherwise"/> (default <see cref="FailureDecision.Stop"/>).
    /// </summary>
    public static IFailurePolicy<TMessage> Restart<TMessage>(
        int maxRestarts = int.MaxValue,
        TimeSpan? within = null,
        FailureDecision otherwise = FailureDecision.Stop)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxRestarts);
        var window = within ?? TimeSpan.MaxValue;
        return From<TMessage>((_, _, context) =>
            context.RestartsWithin(window) < maxRestarts ? FailureDecision.Restart : otherwise);
    }

    /// <summary>A policy from a delegate.</summary>
    public static IFailurePolicy<TMessage> From<TMessage>(Func<Envelope<TMessage>, Exception, FailureContext, FailureDecision> decide)
    {
        ArgumentNullException.ThrowIfNull(decide);
        return new DelegatePolicy<TMessage>(decide);
    }

    private sealed class DelegatePolicy<TMessage>(Func<Envelope<TMessage>, Exception, FailureContext, FailureDecision> decide)
        : IFailurePolicy<TMessage>
    {
        public static readonly DelegatePolicy<TMessage> ResumeInstance = new(static (_, _, _) => FailureDecision.Resume);
        public static readonly DelegatePolicy<TMessage> StopInstance = new(static (_, _, _) => FailureDecision.Stop);
        public static readonly DelegatePolicy<TMessage> EscalateInstance = new(static (_, _, _) => FailureDecision.Escalate);

        public FailureDecision OnFailure(Envelope<TMessage> envelope, Exception exception, FailureContext context)
            => decide(envelope, exception, context);
    }
}

/// <summary>Per-agent failure and restart bookkeeping. Touched only by the agent loop.</summary>
internal sealed class FailureHistory(TimeProvider time)
{
    public int Consecutive { get; private set; }

    public int Total { get; private set; }

    public int TotalRestarts { get; private set; }

    public TimestampRing Failures { get; } = new(time);

    public TimestampRing Restarts { get; } = new(time);

    public void RecordFailure()
    {
        Consecutive++;
        Total++;
        Failures.Record();
    }

    public void RecordRestart()
    {
        TotalRestarts++;
        Restarts.Record();
    }

    public void RecordSuccess() => Consecutive = 0;
}

/// <summary>The most recent timestamps of an event, for trailing-window counts.</summary>
internal sealed class TimestampRing(TimeProvider time)
{
    private const int Size = 64;
    private readonly long[] _timestamps = new long[Size];
    private int _count;
    private int _next;

    public void Record()
    {
        _timestamps[_next] = time.GetTimestamp();
        _next = (_next + 1) % Size;
        _count = Math.Min(_count + 1, Size);
    }

    public int CountWithin(TimeSpan window)
    {
        var now = time.GetTimestamp();
        var within = 0;
        for (var i = 0; i < _count; i++)
        {
            if (time.GetElapsedTime(_timestamps[i], now) <= window)
            {
                within++;
            }
        }

        return within;
    }
}
