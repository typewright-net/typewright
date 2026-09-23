using System.Collections.Concurrent;
using System.Collections.Immutable;
using Typewright.Actors;
using Typewright.Agents;

namespace Typewright.Actors.Tests;

internal static class Waits
{
    public static readonly TimeSpan Default = TimeSpan.FromSeconds(10);

    public static Task<T> Within<T>(this Task<T> task) => task.WaitAsync(Default);

    public static Task Within(this Task task) => task.WaitAsync(Default);

    /// <summary>
    /// Asks until the actor answers, retrying while it is between agents (stopped by a
    /// failure, not yet recreated by its supervisor), as a client would.
    /// </summary>
    public static async Task<TReply> AskEventually<TReply>(this IActorRef<CounterMessage> actor, IRequest<TReply> request)
    {
        var deadline = DateTime.UtcNow + Default;
        while (true)
        {
            try
            {
                return await actor.AskAsync(request).WaitAsync(Default);
            }
            catch (AgentStoppedException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(5);
            }
        }
    }

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

public abstract record CounterMessage;

public sealed record Add(int Amount) : CounterMessage;

public sealed record Read : CounterMessage, IRequest<int>;

public sealed record Boom(string Reason) : CounterMessage, IRequest<int>;

public sealed record Hold(TaskCompletionSource Gate, TaskCompletionSource Entered) : CounterMessage;

/// <summary>A counter actor whose Boom message is an unexpected fault.</summary>
internal static class Counter
{
    public static IMessageHandler<int, CounterMessage> Handler(ConcurrentQueue<string>? stops = null, string? label = null)
        => MessageHandler.For<int, CounterMessage>()
            .On<Add>(static (count, add) => count + add.Amount)
            .On<Read, int>(static (count, _) => (count, count))
            .On<Boom, int>(static (_, boom) => throw new InvalidOperationException(boom.Reason))
            .On<Hold>(static async (count, hold, token) =>
            {
                hold.Entered.TrySetResult();
                await hold.Gate.Task.WaitAsync(token);
                return count;
            })
            .OnStopped((_, _) =>
            {
                if (stops is not null && label is not null)
                {
                    stops.Enqueue(label);
                }

                return ValueTask.CompletedTask;
            })
            .Build();

    public static ActorDefinition<int, CounterMessage> Definition(
        FailureDecision onFailure = FailureDecision.Resume,
        int initial = 0,
        SupervisionStrategy? childSupervision = null,
        ConcurrentQueue<string>? stops = null,
        string? label = null)
        => ActorDefinition.Create(
            initial,
            Handler(stops, label),
            new AgentOptions<CounterMessage> { FailurePolicy = FailurePolicy.From<CounterMessage>((_, _, _) => onFailure) })
        with
        {
            ChildSupervision = childSupervision,
        };
}

public sealed record Heard(object Message, MessageHeaders Headers);

public sealed record Forward(ActorPath To, object Message) : IRequest<bool>;

public sealed record Dump : IRequest<ImmutableList<Heard>>;

/// <summary>
/// A raw handler (it needs the envelope) that records every message's headers and can
/// forward a message to another actor, so tests can observe header stamping.
/// </summary>
internal sealed class Recorder(ActorContext<object> context) : IMessageHandler<ImmutableList<Heard>, object>
{
    public ValueTask<ImmutableList<Heard>> HandleAsync(
        ImmutableList<Heard> heard, Envelope<object> envelope, CancellationToken cancellationToken)
    {
        var next = heard.Add(new Heard(envelope.Message, envelope.Headers));
        switch (envelope.Message)
        {
            case Forward forward:
                var target = context.System.Resolve<object>(forward.To);
                envelope.ReplyTo.TryReply(target?.TryPost(forward.Message) ?? false);
                break;
            case Dump:
                envelope.ReplyTo.TryReply(next);
                break;
        }

        return new(next);
    }

    public static ActorDefinition<ImmutableList<Heard>, object> Definition { get; } = new()
    {
        InitialState = static _ => [],
        Handler = static context => new Recorder(context),
    };
}
