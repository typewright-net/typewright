using System.Collections.Immutable;
using Typewright.Agents;

namespace Typewright.Agents.Tests;

internal static class TestTimeouts
{
    public static readonly TimeSpan Default = TimeSpan.FromSeconds(10);

    public static Task<T> Within<T>(this Task<T> task) => task.WaitAsync(Default);

    public static Task Within(this Task task) => task.WaitAsync(Default);

    public static Task Within(this ValueTask task) => task.AsTask().WaitAsync(Default);

    /// <summary>Polls until <paramref name="condition"/> holds, failing after the default timeout.</summary>
    public static async Task Eventually(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Default;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The condition did not become true in time.");
            }

            await Task.Delay(5);
        }
    }
}

public abstract record TestMessage;

/// <summary>Adds to the counter.</summary>
public sealed record Add(int Amount) : TestMessage;

/// <summary>Replies with the counter.</summary>
public sealed record Get : TestMessage, IRequest<int>;

/// <summary>Throws with <see cref="Reason"/>.</summary>
public sealed record Fail(string Reason) : TestMessage, IRequest<int>;

/// <summary>Signals <see cref="Entered"/>, then waits for <see cref="Gate"/> (or the agent's abort).</summary>
public sealed record Block(TaskCompletionSource Gate, TaskCompletionSource? Entered = null) : TestMessage;

/// <summary>Records the thread the handler runs on, before and after an await.</summary>
public sealed record WhereAmI : TestMessage, IRequest<(int Before, int After)>;

/// <summary>A request that no handler knows about.</summary>
public sealed record Unknown : TestMessage, IRequest<int>;

/// <summary>A request that is not a <see cref="TestMessage"/> at all.</summary>
public sealed record Foreign : IRequest<int>;

/// <summary>A raw, switch-based handler over an int counter — no router involved.</summary>
public sealed class CounterHandler : IMessageHandler<int, TestMessage>
{
    public async ValueTask<int> HandleAsync(int state, Envelope<TestMessage> envelope, CancellationToken cancellationToken)
    {
        switch (envelope.Message)
        {
            case Add add:
                return state + add.Amount;

            case Get:
                envelope.ReplyTo.TryReply(state);
                return state;

            case Fail fail:
                throw new InvalidOperationException(fail.Reason);

            case Block block:
                block.Entered?.TrySetResult();
                await block.Gate.Task.WaitAsync(cancellationToken);
                return state;

            case WhereAmI:
                var before = Environment.CurrentManagedThreadId;
                await Task.Delay(1, cancellationToken);
                envelope.ReplyTo.TryReply((before, Environment.CurrentManagedThreadId));
                return state;

            default:
                throw new UnhandledMessageException(envelope.Message.GetType());
        }
    }
}

/// <summary>A counter handler with lifecycle hooks that record what happened.</summary>
public sealed class LifecycleHandler : IMessageHandler<int, TestMessage>, IAgentLifecycle<int>
{
    private readonly CounterHandler _inner = new();

    public int StartedWith { get; private set; } = -1;

    public ImmutableList<string> Events { get; private set; } = [];

    public TaskCompletionSource<int> Stopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int StartingState { get; init; } = 100;

    public int RestartState { get; init; } = 1_000;

    public ValueTask<int> HandleAsync(int state, Envelope<TestMessage> envelope, CancellationToken cancellationToken)
        => _inner.HandleAsync(state, envelope, cancellationToken);

    public ValueTask<int> OnStartedAsync(int state, CancellationToken cancellationToken)
    {
        StartedWith = state;
        Events = Events.Add("started");
        return new(StartingState);
    }

    public ValueTask<int> OnRestartingAsync(int initialState, Exception cause, CancellationToken cancellationToken)
    {
        Events = Events.Add($"restarting:{cause.Message}");
        return new(RestartState);
    }

    public ValueTask OnStoppedAsync(int finalState, CancellationToken cancellationToken)
    {
        Events = Events.Add("stopped");
        Stopped.TrySetResult(finalState);
        return ValueTask.CompletedTask;
    }
}
