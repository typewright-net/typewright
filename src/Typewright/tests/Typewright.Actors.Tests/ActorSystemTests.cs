using System.Collections.Concurrent;
using Typewright.Actors;
using Typewright.Agents;
using Xunit;

namespace Typewright.Actors.Tests;

public class ActorSystemTests
{
    private static readonly ActorPath CounterPath = ActorPath.Create("/counter");

    [Fact]
    public async Task Spawned_actors_are_addressable_and_duplicates_are_rejected()
    {
        await using var system = new ActorSystem();
        var counter = system.Spawn(CounterPath, Counter.Definition());

        counter.TryPost(new Add(2));

        Assert.Equal(2, await counter.AskAsync(new Read()).Within());
        Assert.Equal(CounterPath, counter.Path);
        Assert.Same(counter, system.Resolve<CounterMessage>(CounterPath));
        Assert.Throws<ActorAlreadyExistsException>(() => system.Spawn(CounterPath, Counter.Definition()));
        Assert.Contains(CounterPath, system.Actors);
    }

    [Fact]
    public async Task GetOrSpawn_creates_exactly_one_actor_under_contention()
    {
        await using var system = new ActorSystem();
        var initialized = 0;
        var definition = new ActorDefinition<int, CounterMessage>
        {
            InitialState = _ => Interlocked.Increment(ref initialized),
            Handler = _ => Counter.Handler(),
        };
        var path = ActorPath.Create("/orders/7");

        var refs = await Task.WhenAll(Enumerable.Range(0, 64).Select(_ => Task.Run(() => system.GetOrSpawn(path, definition))));

        Assert.Equal(1, initialized);
        Assert.All(refs, actor => Assert.Same(refs[0], actor));
        Assert.Equal(1, await refs[0].AskAsync(new Read()).Within());
    }

    [Fact]
    public async Task Resolve_and_GetOrSpawn_respect_message_and_state_types()
    {
        await using var system = new ActorSystem();
        system.Spawn(CounterPath, Counter.Definition());

        Assert.Null(system.Resolve<string>(CounterPath));
        Assert.Null(system.Resolve<CounterMessage>(ActorPath.Create("/nobody")));
        Assert.Throws<InvalidOperationException>(() => system.GetOrSpawn(CounterPath, Recorder.Definition));
    }

    [Fact]
    public async Task State_is_readable_without_the_mailbox()
    {
        await using var system = new ActorSystem();
        var counter = system.Spawn(CounterPath, Counter.Definition(initial: 5));
        var hold = new Hold(new TaskCompletionSource(), new TaskCompletionSource());
        counter.TryPost(new Add(1));
        counter.TryPost(hold);
        await hold.Entered.Task.Within();
        counter.TryPost(new Add(100));   // queued behind the held message

        Assert.True(counter.TryGetState<int>(out var state));
        Assert.Equal(6, state);          // the committed state, while the mailbox is blocked
        Assert.False(counter.TryGetState<string>(out _));
        hold.Gate.SetResult();
    }

    [Fact]
    public async Task Sends_are_stamped_with_sender_and_a_propagated_correlation_id()
    {
        await using var system = new ActorSystem();
        var first = system.Spawn(ActorPath.Create("/first"), Recorder.Definition);
        var second = system.Spawn(ActorPath.Create("/second"), Recorder.Definition);

        Assert.True(await first.AskAsync(new Forward(second.Path, "hello")).Within());
        var atFirst = (await first.AskAsync(new Dump()).Within())[0];
        await Waits.Eventually(() => second.TryGetState<System.Collections.Immutable.ImmutableList<Heard>>(out var heard) && heard.Count == 1);
        second.TryGetState<System.Collections.Immutable.ImmutableList<Heard>>(out var atSecond);

        // From outside any actor: no sender, a fresh correlation id, a timestamp.
        Assert.Null(atFirst.Headers.Sender);
        Assert.NotNull(atFirst.Headers.CorrelationId);
        Assert.NotNull(atFirst.Headers.Timestamp);

        // From inside /first: /first is the sender and the correlation id carries over.
        var forwarded = Assert.Single(atSecond!);
        Assert.Equal("hello", forwarded.Message);
        Assert.Equal("/first", forwarded.Headers.Sender);
        Assert.Equal(atFirst.Headers.CorrelationId, forwarded.Headers.CorrelationId);
    }

    [Fact]
    public async Task Stopping_a_path_stops_its_subtree_deepest_first_and_nothing_else()
    {
        var stops = new ConcurrentQueue<string>();
        await using var system = new ActorSystem();
        system.Spawn(ActorPath.Create("/orders"), Counter.Definition(stops: stops, label: "orders"));
        system.Spawn(ActorPath.Create("/orders/1"), Counter.Definition(stops: stops, label: "orders/1"));
        system.Spawn(ActorPath.Create("/orders/1/lines"), Counter.Definition(stops: stops, label: "orders/1/lines"));
        var other = system.Spawn(ActorPath.Create("/customers"), Counter.Definition(stops: stops, label: "customers"));

        await system.StopAsync(ActorPath.Create("/orders")).Within();

        Assert.Equal(["orders/1/lines", "orders/1", "orders"], stops);
        Assert.Equal(new[] { other.Path }, system.Actors);
        Assert.Equal(0, await other.AskAsync(new Read()).Within());
    }

    [Fact]
    public async Task Stopping_the_system_stops_everything_and_refuses_new_actors()
    {
        var stops = new ConcurrentQueue<string>();
        var system = new ActorSystem();
        var counter = system.Spawn(CounterPath, Counter.Definition(stops: stops, label: "counter"));

        await system.StopAsync().Within();

        Assert.Equal(["counter"], stops);
        Assert.Empty(system.Actors);
        Assert.False(counter.TryPost(new Add(1)));
        Assert.Throws<InvalidOperationException>(() => system.GetOrSpawn(CounterPath, Counter.Definition()));
    }

    [Fact]
    public async Task An_actor_stopped_by_its_own_policy_takes_its_subtree_with_it()
    {
        await using var system = new ActorSystem();
        var parent = system.Spawn(ActorPath.Create("/parent"), Counter.Definition(onFailure: FailureDecision.Stop));
        system.Spawn(ActorPath.Create("/parent/child"), Counter.Definition());

        await Assert.ThrowsAsync<InvalidOperationException>(() => parent.AskAsync(new Boom("fatal")).Within());

        await Waits.Eventually(() => system.Actors.Count == 0);
    }
}
