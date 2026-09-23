using System.Diagnostics.Metrics;
using Typewright.Agents;
using Xunit;

namespace Typewright.Agents.Tests;

/// <summary>
/// Plan 03, C7: a caller's cancellation stops the wait immediately; a request still
/// queued is skipped at dequeue (unless the agent opts out); a request already running
/// completes unless its handler opted into the caller's token.
/// </summary>
public class CancellationTests
{
    public abstract record Work;

    public sealed record Hold(TaskCompletionSource Gate, TaskCompletionSource Entered) : Work;

    public sealed record Count : Work, IRequest<int>;

    public sealed record Slow : Work, IRequest<int>;

    public sealed record Stall(TaskCompletionSource Gate, TaskCompletionSource Entered) : Work, IRequest<int>;

    public sealed record Tally : Work, IRequest<int>;

    public sealed record Nudge : Work;

    private static IMessageHandler<int, Work> Handler(TaskCompletionSource? slowEntered = null) => MessageHandler.For<int, Work>()
        .On<Hold>(async (count, hold, token) =>
        {
            hold.Entered.TrySetResult();
            await hold.Gate.Task.WaitAsync(token);
            return count;
        })
        .On<Count, int>((count, _) => (count + 1, count + 1))
        .On<Slow, int>(async (count, _, token) =>
        {
            slowEntered?.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);   // observes the caller's cancellation
            return (count + 1_000, count);
        })
        .On<Stall, int>((count, stall) =>
        {
            // No token: this handler cannot observe its caller's cancellation.
            stall.Entered.TrySetResult();
            stall.Gate.Task.Wait(TestTimeouts.Default);
            return (count + 1, count + 1);
        })
        .On<Tally, int>((count, _) => (count, count))
        .On<Nudge>((count, _) => count + 10)
        .Build();

    private static async Task<(Agent<int, Work> Agent, TaskCompletionSource Gate)> StartHeldAsync(
        AgentOptions<Work>? options = null, TaskCompletionSource? slowEntered = null)
    {
        var agent = Agent.Start(0, Handler(slowEntered), options);
        var gate = new TaskCompletionSource();
        var entered = new TaskCompletionSource();
        agent.TryPost(new Hold(gate, entered));
        await entered.Task.Within();
        return (agent, gate);
    }

    [Fact]
    public async Task A_request_cancelled_while_queued_is_skipped_and_counted()
    {
        var name = $"skip-{Guid.NewGuid():N}";
        using var skipped = new SkipCounter(name);
        var (agent, gate) = await StartHeldAsync(new AgentOptions<Work> { Name = name });
        await using var _ = agent;

        using var cancel = new CancellationTokenSource();
        var abandoned = agent.AskAsync(new Count(), cancel.Token);
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned.Within());   // the wait ends at once
        gate.SetResult();

        Assert.Equal(0, await agent.AskAsync(new Tally()).Within());   // the handler never ran
        Assert.Equal(1, skipped.Value);
    }

    [Fact]
    public async Task Agents_can_opt_out_so_every_accepted_request_is_applied()
    {
        var (agent, gate) = await StartHeldAsync(new AgentOptions<Work> { SkipCancelledRequests = false });
        await using var _ = agent;

        using var cancel = new CancellationTokenSource();
        var abandoned = agent.AskAsync(new Count(), cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned.Within());
        gate.SetResult();

        Assert.Equal(1, await agent.AskAsync(new Tally()).Within());   // applied; its reply was dropped
    }

    [Fact]
    public async Task A_handler_that_takes_a_token_stops_when_its_caller_cancels_without_failing_the_agent()
    {
        var failures = 0;
        var entered = new TaskCompletionSource();
        await using var agent = Agent.Start(0, Handler(entered), new AgentOptions<Work>
        {
            FailurePolicy = FailurePolicy.From<Work>((_, _, _) =>
            {
                Interlocked.Increment(ref failures);
                return FailureDecision.Stop;
            }),
        });

        using var cancel = new CancellationTokenSource();
        var slow = agent.AskAsync(new Slow(), cancel.Token);
        await entered.Task.Within();
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => slow.Within());
        Assert.Equal(0, await agent.AskAsync(new Tally()).Within());   // state not committed
        Assert.Equal(0, failures);                                      // not a failure
        Assert.Equal(AgentStatus.Running, agent.Status);
    }

    [Fact]
    public async Task A_handler_without_a_token_runs_to_completion_after_its_caller_cancels()
    {
        await using var agent = Agent.Start(0, Handler());
        var stall = new Stall(new TaskCompletionSource(), new TaskCompletionSource());

        using var cancel = new CancellationTokenSource();
        var request = agent.AskAsync(stall, cancel.Token);
        await stall.Entered.Task.Within();
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request.Within());   // the wait ends at once
        stall.Gate.SetResult();
        await TestTimeouts.Eventually(() => agent.CurrentState == 1);                      // the work still committed
    }

    [Fact]
    public async Task Cancelling_PostAsync_only_governs_waiting_for_space()
    {
        var (agent, gate) = await StartHeldAsync();
        await using var _ = agent;

        using var cancel = new CancellationTokenSource();
        await agent.PostAsync(new Nudge(), cancel.Token).Within();   // enqueued immediately
        cancel.Cancel();                                               // too late to matter
        gate.SetResult();

        Assert.Equal(10, await agent.AskAsync(new Tally()).Within());
    }

    [Fact]
    public async Task An_already_cancelled_token_never_enqueues()
    {
        await using var agent = Agent.Start(0, Handler());

        var cancelled = agent.AskAsync(new Count(), new CancellationToken(canceled: true));

        Assert.True(cancelled.IsCanceled);
        Assert.Equal(0, await agent.AskAsync(new Tally()).Within());
    }

    private sealed class SkipCounter : IDisposable
    {
        private readonly MeterListener _listener = new();
        private long _value;

        public SkipCounter(string agentName)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == AgentDiagnostics.MeterName && instrument.Name == "typewright.agent.messages.skipped")
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((_, measurement, tags, _) =>
            {
                foreach (var tag in tags)
                {
                    if (tag.Key == "typewright.agent" && (string?)tag.Value == agentName)
                    {
                        Interlocked.Add(ref _value, measurement);
                    }
                }
            });
            _listener.Start();
        }

        public long Value => Interlocked.Read(ref _value);

        public void Dispose() => _listener.Dispose();
    }
}
