using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Typewright.Agents;
using Typewright.Validation;
using Xunit;

namespace Typewright.Agents.Tests;

public class PipelineTests
{
    private sealed class Recording(string name, ConcurrentQueue<string> log) : IMessageInterceptor<TestMessage>
    {
        public async ValueTask InvokeAsync(MessageContext<TestMessage> context, MessageDelegate<TestMessage> next)
        {
            log.Enqueue($"{name}>");
            await next(context);
            log.Enqueue($"<{name}");
        }
    }

    public sealed record BlockedError : IDomainError;

    /// <summary>Short-circuits Add messages; settles requests it blocks, as the contract asks.</summary>
    private sealed class Gatekeeper : IMessageInterceptor<TestMessage>
    {
        public ValueTask InvokeAsync(MessageContext<TestMessage> context, MessageDelegate<TestMessage> next)
        {
            if (context.Envelope.Message is Add { Amount: < 0 })
            {
                context.Envelope.ReplyTo?.TryReject(new BlockedError());
                return ValueTask.CompletedTask;
            }

            return next(context);
        }
    }

    [Fact]
    public async Task Interceptors_wrap_the_handler_outermost_first()
    {
        var log = new ConcurrentQueue<string>();
        await using var agent = Agent.Start(0, new CounterHandler(), new AgentOptions<TestMessage>
        {
            Interceptors = [new Recording("outer", log), new Recording("inner", log)],
        });

        await agent.AskAsync(new Get()).Within();

        Assert.Equal(["outer>", "inner>", "<inner", "<outer"], log);
    }

    [Fact]
    public async Task A_short_circuiting_interceptor_leaves_the_state_unchanged()
    {
        await using var agent = Agent.Start(0, new CounterHandler(), new AgentOptions<TestMessage>
        {
            Interceptors = [new Gatekeeper()],
        });

        agent.TryPost(new Add(5));
        agent.TryPost(new Add(-100));

        Assert.Equal(5, await agent.AskAsync(new Get()).Within());
    }

    [Fact]
    public async Task Timeout_interceptor_turns_an_overrun_into_a_failure()
    {
        var decisions = new ConcurrentQueue<Exception>();
        var entered = new TaskCompletionSource();
        await using var agent = Agent.Start(0, new CounterHandler(), new AgentOptions<TestMessage>
        {
            Interceptors = [new TimeoutInterceptor<TestMessage>(TimeSpan.FromMilliseconds(50))],
            FailurePolicy = FailurePolicy.From<TestMessage>((_, exception, _) =>
            {
                decisions.Enqueue(exception);
                return FailureDecision.Resume;
            }),
        });

        agent.TryPost(new Block(new TaskCompletionSource(), entered));   // never released
        await entered.Task.Within();
        agent.TryPost(new Add(1));

        Assert.Equal(1, await agent.AskAsync(new Get()).Within());   // the overrun did not stall the agent
        Assert.IsType<TimeoutException>(Assert.Single(decisions));
    }

    [Fact]
    public async Task Tracing_interceptor_continues_the_senders_trace_and_marks_failures()
    {
        var stopped = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == AgentDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped.Enqueue,
        };
        ActivitySource.AddActivityListener(listener);

        var name = $"traced-{Guid.NewGuid():N}";
        await using var agent = Agent.Start(0, new CounterHandler(), new AgentOptions<TestMessage>
        {
            Name = name,
            Interceptors = [new TracingInterceptor<TestMessage>()],
        });

        var traceParent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";
        var headers = new MessageHeaders { TraceParent = traceParent, CorrelationId = CorrelationId.New() };
        await Delivery.AskAsync(agent, new Get(), headers).Within();
        await Assert.ThrowsAsync<InvalidOperationException>(() => agent.AskAsync(new Fail("traced")).Within());

        var ours = stopped.Where(activity => (string?)activity.GetTagItem("messaging.destination.name") == name).ToArray();
        Assert.Equal(2, ours.Length);

        var get = ours.Single(activity => activity.DisplayName == "process Get");
        Assert.Equal("0af7651916cd43dd8448eb211c80319c", get.TraceId.ToHexString());
        Assert.Equal("b7ad6b7169203331", get.ParentSpanId.ToHexString());
        Assert.Equal(ActivityKind.Consumer, get.Kind);
        Assert.Equal(headers.CorrelationId.ToString(), get.GetTagItem("messaging.message.conversation_id"));

        var fail = ours.Single(activity => activity.DisplayName == "process Fail");
        Assert.Equal(ActivityStatusCode.Error, fail.Status);
    }

    [Fact]
    public async Task Metrics_interceptor_counts_processed_and_failed_messages_with_durations()
    {
        var name = $"metered-{Guid.NewGuid():N}";
        var measurements = new ConcurrentQueue<(string Instrument, string MessageType)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == AgentDiagnostics.MeterName)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            },
        };
        void Record(Instrument instrument, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            string? agent = null, type = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "typewright.agent")
                {
                    agent = (string?)tag.Value;
                }
                else if (tag.Key == "typewright.message.type")
                {
                    type = (string?)tag.Value;
                }
            }

            if (agent == name)
            {
                measurements.Enqueue((instrument.Name, type!));
            }
        }

        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) => Record(instrument, tags));
        listener.SetMeasurementEventCallback<double>((instrument, _, tags, _) => Record(instrument, tags));
        listener.Start();

        await using var agent = Agent.Start(0, new CounterHandler(), new AgentOptions<TestMessage>
        {
            Name = name,
            Interceptors = [new MetricsInterceptor<TestMessage>()],
        });

        agent.TryPost(new Add(1));
        await agent.AskAsync(new Get()).Within();
        await Assert.ThrowsAsync<InvalidOperationException>(() => agent.AskAsync(new Fail("x")).Within());

        Assert.Equal(2, measurements.Count(m => m.Instrument == "typewright.agent.messages.processed"));
        Assert.Equal(("typewright.agent.messages.failed", "Fail"), Assert.Single(measurements, m => m.Instrument.EndsWith("failed", StringComparison.Ordinal)));
        Assert.Equal(3, measurements.Count(m => m.Instrument == "typewright.agent.message.duration"));
    }

    private static readonly AsyncLocal<string?> Ambient = new();

    public sealed record ReadAmbient : TestMessage, IRequest<string?>;

    [Fact]
    public async Task The_loop_does_not_inherit_the_starters_ambient_context()
    {
        Ambient.Value = "from the starter";
        using var activity = new Activity("starter").Start();

        await using var agent = Agent.Start(0, MessageHandler.For<int, TestMessage>()
            .On<ReadAmbient, string?>((count, _) => (count, $"{Ambient.Value}|{Activity.Current?.OperationName}"))
            .Build());

        Assert.Equal("|", await agent.AskAsync(new ReadAmbient()).Within());
        Ambient.Value = null;
    }
}

public class PriorityMailboxTests
{
    public sealed record Job(string Name, int Priority) : TestMessage;

    public sealed record Done : TestMessage, IRequest<string>;

    private static IMessageHandler<string, TestMessage> Recorder() => MessageHandler.For<string, TestMessage>()
        .On<Block>(async (log, block, token) =>
        {
            block.Entered?.TrySetResult();
            await block.Gate.Task.WaitAsync(token);
            return log;
        })
        .On<Job>((log, job) => log + job.Name)
        .On<Done, string>((log, _) => (log, log))
        .Build();

    private static int PriorityOf(Envelope<TestMessage> envelope) => envelope.Message switch
    {
        Job job => job.Priority,
        _ => int.MaxValue,   // control messages go last
    };

    [Fact]
    public async Task Urgent_messages_overtake_the_backlog_and_ties_stay_fifo()
    {
        var gate = new TaskCompletionSource();
        var entered = new TaskCompletionSource();
        await using var agent = Agent.Start("", Recorder(), new AgentOptions<TestMessage>
        {
            Mailbox = () => new PriorityMailbox<TestMessage, int>(PriorityOf),
        });
        agent.TryPost(new Block(gate, entered));
        await entered.Task.Within();

        agent.TryPost(new Job("a", 5));
        agent.TryPost(new Job("B", 1));
        agent.TryPost(new Job("c", 5));
        agent.TryPost(new Job("D", 1));
        var done = agent.AskAsync(new Done());
        gate.SetResult();

        Assert.Equal("BDac", await done.Within());
    }

    [Fact]
    public async Task Bounded_priority_mailbox_rejects_or_waits_when_full()
    {
        var gate = new TaskCompletionSource();
        var entered = new TaskCompletionSource();
        await using var agent = Agent.Start("", Recorder(), new AgentOptions<TestMessage>
        {
            Mailbox = () => new PriorityMailbox<TestMessage, int>(PriorityOf, options: new MailboxOptions(Capacity: 1)),
        });
        agent.TryPost(new Block(gate, entered));
        await entered.Task.Within();

        Assert.True(agent.TryPost(new Job("x", 1)));
        Assert.False(agent.TryPost(new Job("y", 1)));
        var waiting = agent.PostAsync(new Job("y", 1)).AsTask();
        await Task.Delay(50);
        Assert.False(waiting.IsCompleted);

        gate.SetResult();
        await waiting.Within();
        await TestTimeouts.Eventually(() => agent.CurrentState == "xy");
    }

    [Fact]
    public void Drop_modes_are_not_supported_by_the_priority_mailbox()
        => Assert.Throws<NotSupportedException>(() => new PriorityMailbox<TestMessage, int>(
            PriorityOf, options: new MailboxOptions(Capacity: 1, MailboxFullMode.DropOldest)));
}

public class FailureContextTests
{
    private sealed class ManualTime : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Interlocked.Read(ref _ticks);

        public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
    }

    [Fact]
    public async Task Restart_limits_apply_to_a_sliding_window()
    {
        var time = new ManualTime();
        var seen = new ConcurrentQueue<(int Consecutive, int Total, int WithinMinute)>();
        var restartLimit = FailurePolicy.Restart<TestMessage>(maxRestarts: 2, within: TimeSpan.FromMinutes(1));
        await using var agent = Agent.Start(0, new CounterHandler(), new AgentOptions<TestMessage>
        {
            TimeProvider = time,
            FailurePolicy = FailurePolicy.From<TestMessage>((envelope, exception, context) =>
            {
                seen.Enqueue((context.ConsecutiveFailures, context.TotalFailures, context.FailuresWithin(TimeSpan.FromMinutes(1))));
                return restartLimit.OnFailure(envelope, exception, context);
            }),
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => agent.AskAsync(new Fail("1")).Within());
        await Assert.ThrowsAsync<InvalidOperationException>(() => agent.AskAsync(new Fail("2")).Within());
        time.Advance(TimeSpan.FromMinutes(2));   // both earlier restarts leave the window
        agent.TryPost(new Add(1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => agent.AskAsync(new Fail("3")).Within());

        Assert.Equal(AgentStatus.Running, agent.Status);
        Assert.Equal([(1, 1, 1), (2, 2, 2), (1, 3, 1)], seen);
    }
}
