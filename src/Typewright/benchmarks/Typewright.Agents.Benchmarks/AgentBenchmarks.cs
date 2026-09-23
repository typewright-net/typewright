using BenchmarkDotNet.Attributes;

namespace Typewright.Agents.Benchmarks;

public abstract record CounterMessage;

public sealed record Increment : CounterMessage;

public sealed record GetCount : CounterMessage, IRequest<long>;

internal sealed class CounterHandler : IMessageHandler<long, CounterMessage>
{
    public ValueTask<long> HandleAsync(long state, Envelope<CounterMessage> envelope, CancellationToken cancellationToken)
    {
        if (envelope.Message is GetCount)
        {
            envelope.ReplyTo.TryReply(state);
        }

        return new(envelope.Message is Increment ? state + 1 : state);
    }
}

public enum DispatcherKind
{
    ThreadPool,
    DedicatedThread,
    ExclusiveScheduler,
}

/// <summary>
/// Throughput of fire-and-forget posts (a batch, then one ask to wait for the drain) and
/// the round-trip latency of a single ask, per dispatcher.
/// </summary>
[MemoryDiagnoser]
public class AgentBenchmarks
{
    private static readonly Increment IncrementMessage = new();
    private static readonly GetCount GetCountMessage = new();

    private Agent<long, CounterMessage> _agent = null!;

    [Params(DispatcherKind.ThreadPool, DispatcherKind.DedicatedThread, DispatcherKind.ExclusiveScheduler)]
    public DispatcherKind Dispatcher { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        IDispatcher dispatcher = Dispatcher switch
        {
            DispatcherKind.DedicatedThread => new DedicatedThreadDispatcher(),
            DispatcherKind.ExclusiveScheduler => new TaskSchedulerDispatcher(new ConcurrentExclusiveSchedulerPair().ExclusiveScheduler),
            _ => ThreadPoolDispatcher.Instance,
        };
        _agent = Agent.Start(0L, new CounterHandler(), new AgentOptions<CounterMessage> { Dispatcher = dispatcher });
    }

    [GlobalCleanup]
    public void Cleanup() => _agent.DisposeAsync().AsTask().GetAwaiter().GetResult();

    /// <summary>1,000 posts plus the ask that waits for them; divide by 1,000 for per-message cost.</summary>
    [Benchmark(OperationsPerInvoke = 1_000)]
    public async Task<long> Post_1000_then_drain()
    {
        for (var i = 0; i < 1_000; i++)
        {
            _agent.TryPost(IncrementMessage);
        }

        return await _agent.AskAsync(GetCountMessage);
    }

    /// <summary>One request/reply round trip.</summary>
    [Benchmark]
    public Task<long> Ask_round_trip() => _agent.AskAsync(GetCountMessage);
}
