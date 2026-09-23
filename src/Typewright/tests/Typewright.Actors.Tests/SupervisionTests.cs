using Typewright.Actors;
using Typewright.Agents;
using Xunit;

namespace Typewright.Actors.Tests;

public class SupervisionTests
{
    private static readonly ActorPath Parent = ActorPath.Create("/parent");
    private static readonly ActorPath Child = ActorPath.Create("/parent/child");

    private static async Task<IActorRef<CounterMessage>> SpawnFamilyAsync(ActorSystem system, SupervisionStrategy childSupervision)
    {
        var parent = system.Spawn(Parent, Counter.Definition(initial: 1_000, childSupervision: childSupervision));
        parent.TryPost(new Add(1));
        await parent.AskAsync(new Read()).Within();
        var child = system.Spawn(Child, Counter.Definition(onFailure: FailureDecision.Escalate));
        child.TryPost(new Add(5));
        Assert.Equal(5, await child.AskAsync(new Read()).Within());
        return child;
    }

    [Fact]
    public async Task Restart_recreates_the_child_from_its_initial_state_behind_the_same_reference()
    {
        await using var system = new ActorSystem();
        var child = await SpawnFamilyAsync(system, SupervisionStrategy.Restart());

        await Assert.ThrowsAsync<InvalidOperationException>(() => child.AskAsync(new Boom("bad")).Within());

        Assert.Equal(0, await child.AskEventually(new Read()));
        Assert.Same(child, system.Resolve<CounterMessage>(Child));
    }

    [Fact]
    public async Task Resume_recreates_the_child_from_its_last_committed_state()
    {
        await using var system = new ActorSystem();
        var child = await SpawnFamilyAsync(system, SupervisionStrategy.Resume);

        await Assert.ThrowsAsync<InvalidOperationException>(() => child.AskAsync(new Boom("bad")).Within());

        Assert.Equal(5, await child.AskEventually(new Read()));
    }

    [Fact]
    public async Task Stop_removes_the_child()
    {
        await using var system = new ActorSystem();
        var child = await SpawnFamilyAsync(system, SupervisionStrategy.Stop);

        await Assert.ThrowsAsync<InvalidOperationException>(() => child.AskAsync(new Boom("bad")).Within());

        await Waits.Eventually(() => system.Resolve<CounterMessage>(Child) is null);
        Assert.False(child.TryPost(new Add(1)));
        await Assert.ThrowsAsync<AgentStoppedException>(() => child.AskAsync(new Read()).Within());
    }

    [Fact]
    public async Task Escalate_stops_the_child_and_hands_the_parent_to_its_own_supervisor()
    {
        // The parent is top-level, so the system default (restart) decides for it.
        await using var system = new ActorSystem();
        var child = await SpawnFamilyAsync(system, SupervisionStrategy.Escalate);
        var parent = system.Resolve<CounterMessage>(Parent)!;

        await Assert.ThrowsAsync<InvalidOperationException>(() => child.AskAsync(new Boom("bad")).Within());

        await Waits.Eventually(() => system.Resolve<CounterMessage>(Child) is null);
        await Waits.Eventually(() => parent.TryGetState<int>(out var state) && state == 1_000);   // restarted
        Assert.Equal(1_000, await parent.AskAsync(new Read()).Within());
    }

    [Fact]
    public async Task Restart_limits_stop_a_child_that_keeps_failing()
    {
        await using var system = new ActorSystem();
        var child = await SpawnFamilyAsync(system, SupervisionStrategy.Restart(maxRestarts: 2, within: TimeSpan.FromMinutes(1)));

        for (var attempt = 0; attempt < 2; attempt++)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => child.AskEventually(new Boom($"#{attempt}")));
            Assert.Equal(0, await child.AskEventually(new Read()));   // recreated
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => child.AskEventually(new Boom("#3")));

        await Waits.Eventually(() => system.Resolve<CounterMessage>(Child) is null);
    }

    [Fact]
    public async Task Requests_queued_behind_an_escalating_failure_fail_instead_of_hanging()
    {
        await using var system = new ActorSystem();
        var child = await SpawnFamilyAsync(system, SupervisionStrategy.Restart());
        var hold = new Hold(new TaskCompletionSource(), new TaskCompletionSource());
        child.TryPost(hold);
        await hold.Entered.Task.Within();
        var failing = child.AskAsync(new Boom("bad"));
        var queued = child.AskAsync(new Read());
        hold.Gate.SetResult();

        await Assert.ThrowsAsync<InvalidOperationException>(() => failing.Within());
        await Assert.ThrowsAsync<AgentStoppedException>(() => queued.Within());
    }

    [Fact]
    public async Task Top_level_actors_use_the_system_default_supervision()
    {
        var options = new ActorSystemOptions { DefaultSupervision = SupervisionStrategy.Resume };
        await using var system = new ActorSystem(options);
        var solo = system.Spawn(ActorPath.Create("/solo"), Counter.Definition(onFailure: FailureDecision.Escalate, initial: 3));
        solo.TryPost(new Add(4));

        await Assert.ThrowsAsync<InvalidOperationException>(() => solo.AskAsync(new Boom("bad")).Within());

        Assert.Equal(7, await solo.AskEventually(new Read()));
    }
}
