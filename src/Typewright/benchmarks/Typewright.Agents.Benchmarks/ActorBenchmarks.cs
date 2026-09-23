using BenchmarkDotNet.Attributes;
using Typewright.Actors;

namespace Typewright.Agents.Benchmarks;

/// <summary>
/// What addressing costs: an ask through an actor reference (header stamping, sender scope,
/// and the default tracing/metrics interceptors with no listener attached) against the
/// same ask sent straight to an agent.
/// </summary>
[MemoryDiagnoser]
public class ActorBenchmarks
{
    private static readonly GetCount GetCountMessage = new();

    private Agent<long, CounterMessage> _agent = null!;
    private ActorSystem _system = null!;
    private IActorRef<CounterMessage> _actor = null!;

    [GlobalSetup]
    public void Setup()
    {
        _agent = Agent.Start(0L, new CounterHandler());
        _system = new ActorSystem();
        _actor = _system.Spawn(ActorPath.Create("/counter"), ActorDefinition.Create(0L, new CounterHandler()));
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _agent.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _system.Dispose();
    }

    [Benchmark(Baseline = true)]
    public Task<long> Agent_ask() => _agent.AskAsync(GetCountMessage);

    [Benchmark]
    public Task<long> Actor_ask() => _actor.AskAsync(GetCountMessage);
}
