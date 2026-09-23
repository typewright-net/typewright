using System.Collections.Immutable;
using Typewright.Agents;
using Xunit;

namespace Typewright.Agents.Tests;

public class AgentCoreTests
{
    [Fact]
    public async Task Threads_state_through_the_handler_and_replies_to_requests()
    {
        await using var agent = Agent.Start(0, new CounterHandler());

        agent.TryPost(new Add(2));
        agent.TryPost(new Add(3));

        Assert.Equal(5, await agent.AskAsync(new Get()).Within());
    }

    [Fact]
    public async Task Processes_messages_in_order_one_at_a_time()
    {
        var inFlight = 0;
        var maxInFlight = 0;
        var handler = new OrderRecorder(
            onEnter: () => maxInFlight = Math.Max(maxInFlight, Interlocked.Increment(ref inFlight)),
            onExit: () => Interlocked.Decrement(ref inFlight));
        await using var agent = Agent.Start(ImmutableList<int>.Empty, handler);

        var sent = Enumerable.Range(1, 500).ToArray();
        await Task.WhenAll(sent.Select(i => agent.PostAsync(new Record(i)).AsTask()));
        var seen = await agent.AskAsync(new Snapshot()).Within();

        Assert.Equal(sent, seen);
        Assert.Equal(1, maxInFlight);
    }

    [Fact]
    public async Task Messages_posted_before_start_are_buffered()
    {
        var agent = new Agent<int, TestMessage>(0, new CounterHandler());
        Assert.Equal(AgentStatus.Created, agent.Status);

        agent.TryPost(new Add(7));
        var reply = agent.AskAsync(new Get());
        Assert.False(reply.IsCompleted);

        agent.Start();

        Assert.Equal(7, await reply.Within());
        Assert.Equal(AgentStatus.Running, agent.Status);
        await agent.DisposeAsync();
    }

    [Fact]
    public async Task Current_state_is_readable_without_the_mailbox_and_never_partial()
    {
        // A struct state wider than a machine word: a torn read would show mismatched halves.
        await using var agent = Agent.Start(new Wide(0, 0, 0, 0), new WideHandler());
        using var stop = new CancellationTokenSource();

        var reader = Task.Run(() =>
        {
            var reads = 0;
            while (!stop.IsCancellationRequested)
            {
                var state = agent.CurrentState;
                Assert.True(state.A == state.B && state.B == state.C && state.C == state.D, $"Torn read: {state}");
                reads++;
            }

            return reads;
        });

        for (var i = 0; i < 20_000; i++)
        {
            agent.TryPost(new Bump());
        }

        var final = await agent.AskAsync(new ReadWide()).Within();
        stop.Cancel();

        Assert.Equal(20_000, final.A);
        Assert.True(await reader.Within() > 0);
        Assert.Equal(final, agent.CurrentState);
    }

    [Fact]
    public async Task A_failed_message_fails_its_request_and_the_agent_resumes_with_the_prior_state()
    {
        await using var agent = Agent.Start(0, new CounterHandler());
        agent.TryPost(new Add(10));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => agent.AskAsync(new Fail("boom")).Within());

        Assert.Equal("boom", failure.Message);
        Assert.Equal(10, await agent.AskAsync(new Get()).Within());
        Assert.Equal(AgentStatus.Running, agent.Status);
    }

    [Fact]
    public async Task Stop_policy_faults_completion_and_fails_queued_requests()
    {
        var gate = new TaskCompletionSource();
        var entered = new TaskCompletionSource();
        await using var agent = Agent.Start(0, new CounterHandler(), new AgentOptions<TestMessage>
        {
            FailurePolicy = FailurePolicy.Stop<TestMessage>(),
        });

        agent.TryPost(new Block(gate, entered));
        await entered.Task.Within();
        var failing = agent.AskAsync(new Fail("fatal"));
        var queued = agent.AskAsync(new Get());
        gate.SetResult();

        await Assert.ThrowsAsync<InvalidOperationException>(() => failing.Within());
        await Assert.ThrowsAsync<AgentStoppedException>(() => queued.Within());
        var fault = await Assert.ThrowsAsync<InvalidOperationException>(() => agent.Completion.Within());
        Assert.Equal("fatal", fault.Message);
        Assert.Equal(AgentStatus.Failed, agent.Status);
        Assert.False(agent.TryPost(new Add(1)));
    }

    [Fact]
    public async Task Escalate_policy_stops_and_flags_the_escalation()
    {
        await using var agent = Agent.Start(0, new CounterHandler(), new AgentOptions<TestMessage>
        {
            FailurePolicy = FailurePolicy.Escalate<TestMessage>(),
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => agent.AskAsync(new Fail("up")).Within());
        await Assert.ThrowsAsync<InvalidOperationException>(() => agent.Completion.Within());

        Assert.Equal(AgentStatus.Escalated, agent.Status);
    }

    [Fact]
    public async Task Restart_policy_resets_through_the_lifecycle_hook_and_keeps_queued_messages()
    {
        var handler = new LifecycleHandler();
        await using var agent = Agent.Start(0, handler, new AgentOptions<TestMessage>
        {
            FailurePolicy = FailurePolicy.Restart<TestMessage>(),
        });

        agent.TryPost(new Add(5));
        var failed = agent.AskAsync(new Fail("reset"));
        agent.TryPost(new Add(1));

        await Assert.ThrowsAsync<InvalidOperationException>(() => failed.Within());
        Assert.Equal(1_001, await agent.AskAsync(new Get()).Within());
        Assert.Equal(["started", "restarting:reset"], handler.Events);
    }

    [Fact]
    public async Task Restart_limit_stops_the_agent_once_exceeded()
    {
        await using var agent = Agent.Start(0, new CounterHandler(), new AgentOptions<TestMessage>
        {
            FailurePolicy = FailurePolicy.Restart<TestMessage>(maxRestarts: 2, within: TimeSpan.FromMinutes(1)),
        });

        for (var i = 0; i < 2; i++)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => agent.AskAsync(new Fail($"#{i}")).Within());
            Assert.Equal(AgentStatus.Running, agent.Status);
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => agent.AskAsync(new Fail("#3")).Within());
        await Assert.ThrowsAsync<InvalidOperationException>(() => agent.Completion.Within());
        Assert.Equal(AgentStatus.Failed, agent.Status);
    }

    [Fact]
    public async Task Lifecycle_hooks_run_at_start_and_stop()
    {
        var handler = new LifecycleHandler { StartingState = 40 };
        var agent = Agent.Start(1, handler);
        agent.TryPost(new Add(2));

        Assert.Equal(42, await agent.AskAsync(new Get()).Within());
        await agent.StopAsync().Within();

        Assert.Equal(1, handler.StartedWith);
        Assert.Equal(42, await handler.Stopped.Task.Within());
        Assert.Equal(["started", "stopped"], handler.Events);
    }

    [Fact]
    public async Task Graceful_stop_processes_everything_queued_then_refuses_new_messages()
    {
        var gate = new TaskCompletionSource();
        var entered = new TaskCompletionSource();
        var agent = Agent.Start(0, new CounterHandler());
        agent.TryPost(new Block(gate, entered));
        await entered.Task.Within();
        agent.TryPost(new Add(1));
        var queued = agent.AskAsync(new Get());

        var stopping = agent.StopAsync();
        Assert.Equal(AgentStatus.Stopping, agent.Status);
        gate.SetResult();
        await stopping.Within();

        Assert.Equal(1, await queued.Within());
        Assert.Equal(AgentStatus.Stopped, agent.Status);
        Assert.True(agent.Completion.IsCompletedSuccessfully);
        Assert.False(agent.TryPost(new Add(1)));
        await Assert.ThrowsAsync<AgentStoppedException>(() => agent.PostAsync(new Add(1)).Within());
        await Assert.ThrowsAsync<AgentStoppedException>(() => agent.AskAsync(new Get()).Within());
    }

    [Fact]
    public async Task Abort_cancels_in_flight_work_and_fails_queued_requests()
    {
        var entered = new TaskCompletionSource();
        var agent = Agent.Start(0, new CounterHandler());
        agent.TryPost(new Block(new TaskCompletionSource(), entered));
        await entered.Task.Within();
        var queued = agent.AskAsync(new Get());

        await agent.AbortAsync().Within();

        await Assert.ThrowsAsync<AgentStoppedException>(() => queued.Within());
        Assert.Equal(AgentStatus.Aborted, agent.Status);
        Assert.True(agent.Completion.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Stop_aborts_when_its_token_fires_first()
    {
        var entered = new TaskCompletionSource();
        var agent = Agent.Start(0, new CounterHandler());
        agent.TryPost(new Block(new TaskCompletionSource(), entered));
        await entered.Task.Within();

        using var impatient = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await agent.StopAsync(impatient.Token).Within();

        Assert.Equal(AgentStatus.Aborted, agent.Status);
    }

    [Fact]
    public async Task Stopping_an_agent_that_never_started_fails_its_buffered_requests()
    {
        var agent = new Agent<int, TestMessage>(0, new CounterHandler());
        var buffered = agent.AskAsync(new Get());

        await agent.StopAsync().Within();

        await Assert.ThrowsAsync<AgentStoppedException>(() => buffered.Within());
        Assert.Equal(AgentStatus.Stopped, agent.Status);
        Assert.Throws<InvalidOperationException>(agent.Start);
    }

    [Fact]
    public async Task A_caller_that_blocks_after_its_reply_cannot_stall_the_agent()
    {
        // If reply continuations ran inline on the agent loop, the synchronous Wait below
        // would block the loop from ever processing the second request: a deadlock.
        await using var agent = Agent.Start(0, new CounterHandler(), new AgentOptions<TestMessage>
        {
            Dispatcher = new DedicatedThreadDispatcher(),
        });

        var caller = Task.Run(async () =>
        {
            await agent.AskAsync(new Get());
            return agent.AskAsync(new Get()).Wait(TestTimeouts.Default);
        });

        Assert.True(await caller.Within());
    }

    [Fact]
    public async Task Dedicated_thread_dispatcher_keeps_the_loop_and_handlers_on_one_thread()
    {
        await using var agent = Agent.Start(0, new CounterHandler(), new AgentOptions<TestMessage>
        {
            Dispatcher = new DedicatedThreadDispatcher("pinned"),
        });

        var threads = new HashSet<int>();
        for (var i = 0; i < 20; i++)
        {
            var (before, after) = await agent.AskAsync(new WhereAmI()).Within();
            threads.Add(before);
            threads.Add(after);
        }

        Assert.Single(threads);
        Assert.NotEqual(Environment.CurrentManagedThreadId, threads.Single());
    }

    [Fact]
    public async Task Agents_sharing_an_exclusive_scheduler_never_execute_simultaneously()
    {
        // Exclusion holds between awaits: no two synchronous segments of the agents' code
        // overlap. (It is not atomicity across awaits; per-agent ordering covers that.)
        var pair = new ConcurrentExclusiveSchedulerPair();
        var shared = new SharedCounter();
        var options = new AgentOptions<Touch> { Dispatcher = new TaskSchedulerDispatcher(pair.ExclusiveScheduler) };
        await using var first = Agent.Start(new Unit(), new SharedCounterHandler(shared), options);
        await using var second = Agent.Start(new Unit(), new SharedCounterHandler(shared), options);

        var sends = Enumerable.Range(0, 200)
            .Select(i => (i % 2 == 0 ? first : second).AskAsync(new Touch()))
            .ToArray();
        await Task.WhenAll(sends).Within();

        Assert.Equal(400, shared.Segments);
        Assert.Equal(1, shared.MaxConcurrent);
    }

    [Fact]
    public async Task Stateless_agents_handle_messages_without_state()
    {
        var seen = new List<string>();
        await using var agent = Agent.Start(new EchoHandler(seen));

        agent.TryPost("a");
        Assert.Equal("b!", await agent.AskAsync(new Echo("b")).Within());
    }

    [Fact]
    public async Task Requests_that_are_not_the_agents_message_type_are_rejected_up_front()
    {
        await using var agent = Agent.Start(0, new CounterHandler());

        Assert.Throws<ArgumentException>(() => { _ = agent.AskAsync(new Foreign()); });
        Assert.Throws<ArgumentNullException>(() => agent.TryPost(null!));
    }

    [Fact]
    public async Task Unknown_messages_are_failures_not_silent_drops()
    {
        await using var agent = Agent.Start(0, new CounterHandler());

        await Assert.ThrowsAsync<UnhandledMessageException>(() => agent.AskAsync(new Unknown()).Within());
    }

    // --- supporting types ---

    public abstract record Ordered;

    public sealed record Record(int Value) : Ordered;

    public sealed record Snapshot : Ordered, IRequest<ImmutableList<int>>;

    private sealed class OrderRecorder(Action onEnter, Action onExit) : IMessageHandler<ImmutableList<int>, Ordered>
    {
        public async ValueTask<ImmutableList<int>> HandleAsync(
            ImmutableList<int> state, Envelope<Ordered> envelope, CancellationToken cancellationToken)
        {
            onEnter();
            try
            {
                await Task.Yield();
                switch (envelope.Message)
                {
                    case Record record:
                        return state.Add(record.Value);
                    case Snapshot:
                        envelope.ReplyTo.TryReply(state);
                        return state;
                    default:
                        throw new UnhandledMessageException(envelope.Message.GetType());
                }
            }
            finally
            {
                onExit();
            }
        }
    }

    public readonly record struct Wide(long A, long B, long C, long D);

    public abstract record WideMessage;

    public sealed record Bump : WideMessage;

    public sealed record ReadWide : WideMessage, IRequest<Wide>;

    private sealed class WideHandler : IMessageHandler<Wide, WideMessage>
    {
        public ValueTask<Wide> HandleAsync(Wide state, Envelope<WideMessage> envelope, CancellationToken cancellationToken)
        {
            if (envelope.Message is ReadWide)
            {
                envelope.ReplyTo.TryReply(state);
                return new(state);
            }

            var next = state.A + 1;
            return new(new Wide(next, next, next, next));
        }
    }

    public sealed record Touch : IRequest<Unit>;

    private sealed class SharedCounter
    {
        public int Segments;
        public int Inside;
        public int MaxConcurrent;
    }

    private sealed class SharedCounterHandler(SharedCounter shared) : IMessageHandler<Unit, Touch>
    {
        public async ValueTask<Unit> HandleAsync(Unit state, Envelope<Touch> envelope, CancellationToken cancellationToken)
        {
            Segment();
            await Task.Yield();
            Segment();
            envelope.ReplyTo.TryReply(Unit.Value);
            return state;
        }

        private void Segment()
        {
            var inside = Interlocked.Increment(ref shared.Inside);
            InterlockedMax(ref shared.MaxConcurrent, inside);
            Thread.SpinWait(2_000);
            shared.Segments++;   // safe only because segments are exclusive
            Interlocked.Decrement(ref shared.Inside);
        }

        private static void InterlockedMax(ref int target, int value)
        {
            int current;
            while ((current = Volatile.Read(ref target)) < value
                && Interlocked.CompareExchange(ref target, value, current) != current)
            {
            }
        }
    }

    public sealed record Echo(string Text) : IRequest<string>;

    private sealed class EchoHandler(List<string> seen) : IMessageHandler<object>
    {
        public ValueTask HandleAsync(Envelope<object> envelope, CancellationToken cancellationToken)
        {
            switch (envelope.Message)
            {
                case string text:
                    seen.Add(text);
                    break;
                case Echo echo:
                    envelope.ReplyTo.TryReply(echo.Text + "!");
                    break;
            }

            return ValueTask.CompletedTask;
        }
    }
}
