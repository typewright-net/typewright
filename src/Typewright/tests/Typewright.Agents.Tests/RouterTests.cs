using Typewright.Agents;
using Typewright.Validation;
using Xunit;

namespace Typewright.Agents.Tests;

// The plan's goal snippet, verbatim.
public abstract record CounterMessage;

public sealed record Increment : CounterMessage;

public sealed record GetCount : CounterMessage, IRequest<int>;

public class RouterTests
{
    [Fact]
    public async Task The_goal_snippet_compiles_and_runs()
    {
        // An anonymous agent: no system, no address, just a reference.
        var counter = Agent.Start(0, MessageHandler.For<int, CounterMessage>()
            .On<Increment>(static (count, _) => count + 1)
            .On<GetCount, int>(static (count, _) => (count, count))
            .Build());

        counter.TryPost(new Increment());
        var total = await counter.AskAsync(new GetCount());   // Task<int>, inferred from the request type

        Assert.Equal(1, total);
        await counter.DisposeAsync();
    }

    public abstract record Shape;

    public interface IRound;

    public record Circle(double Radius) : Shape, IRound;

    public sealed record Ring(double Radius) : Circle(Radius);

    public sealed record Square(double Side) : Shape;

    public sealed record Oval : Shape, IRound;

    public sealed record Describe : Shape, IRequest<string>;

    [Fact]
    public async Task Routes_by_runtime_type_with_the_most_derived_registration_winning()
    {
        await using var agent = Agent.Start(new List<string>(), MessageHandler.For<List<string>, Shape>()
            .On<Circle>((seen, _) => [.. seen, "circle"])
            .On<Ring>((seen, _) => [.. seen, "ring"])
            .On<Shape>((seen, shape) => [.. seen, $"shape:{shape.GetType().Name}"])
            .On<Describe, string>((seen, _) => (seen, string.Join(",", seen)))
            .Build());

        agent.TryPost(new Ring(1));      // exact route beats its Circle base
        agent.TryPost(new Circle(1));
        agent.TryPost(new Square(1));    // no exact route: falls back to the Shape base

        Assert.Equal("ring,circle,shape:Square", await agent.AskAsync(new Describe()).Within());
    }

    public sealed record Last : IRequest<string>;

    public sealed record Probe : IRequest<int>;

    [Fact]
    public async Task Interface_routes_resolve_to_the_most_derived_match_and_unrouted_messages_fail()
    {
        // Interface routes need a message type they can be (here, object): the
        // TCase : TMessage constraint rejects routes that could never match.
        await using var agent = Agent.Start("", MessageHandler.For<string, object>()
            .On<IRound>((_, _) => "round")
            .On<Circle>((_, _) => "circle")
            .On<Last, string>((last, _) => (last, last))
            .Build());

        agent.TryPost(new Ring(1));      // Circle beats IRound, because Circle implements it
        Assert.Equal("circle", await agent.AskAsync(new Last()).Within());
        agent.TryPost(new Oval());
        Assert.Equal("round", await agent.AskAsync(new Last()).Within());

        var unrouted = await Assert.ThrowsAsync<UnhandledMessageException>(() => agent.AskAsync(new Probe()).Within());
        Assert.Equal(typeof(Probe), unrouted.MessageType);
    }

    public interface IA;

    public interface IB;

    public sealed record Both : IA, IB, IRequest<int>;

    [Fact]
    public async Task Matching_two_unrelated_routes_is_reported_as_ambiguous()
    {
        await using var agent = Agent.Start(0, MessageHandler.For<int, object>()
            .On<IA>((count, _) => count)
            .On<IB>((count, _) => count)
            .Build());

        var ambiguous = await Assert.ThrowsAsync<UnhandledMessageException>(() => agent.AskAsync(new Both()).Within());
        Assert.Contains("several routes", ambiguous.Message);
    }

    [Fact]
    public void Duplicate_routes_are_rejected_at_registration()
    {
        var builder = MessageHandler.For<int, CounterMessage>().On<Increment>((count, _) => count);

        Assert.Throws<InvalidOperationException>(() => builder.On<Increment>((count, _) => count + 1));
    }

    [Fact]
    public async Task Otherwise_catches_unrouted_messages()
    {
        await using var agent = Agent.Start(0, MessageHandler.For<int, CounterMessage>()
            .On<GetCount, int>((count, _) => (count, count))
            .Otherwise((count, _, _) => new ValueTask<int>(count + 100))
            .Build());

        agent.TryPost(new Increment());

        Assert.Equal(100, await agent.AskAsync(new GetCount()).Within());
    }

    public abstract record AccountMessage;

    public sealed record Deposit(decimal Amount) : AccountMessage, IRequest<decimal>;

    public sealed record Withdraw(decimal Amount) : AccountMessage, IRequest<decimal>;

    public sealed record InsufficientFundsError(decimal Balance, decimal Requested) : IDomainError;

    private static IMessageHandler<decimal, AccountMessage> Account() => MessageHandler.For<decimal, AccountMessage>()
        .On<Deposit, decimal>(async (balance, deposit, token) =>
        {
            await Task.Yield();
            var next = balance + deposit.Amount;
            return (next, next);
        })
        .On<Withdraw, decimal>((balance, withdraw) => withdraw.Amount > balance
            ? (balance, OperationResult.Reject(new InsufficientFundsError(balance, withdraw.Amount)))
            : (balance - withdraw.Amount, balance - withdraw.Amount))
        .Build();

    [Fact]
    public async Task Rejections_reach_TryAskAsync_as_typed_results()
    {
        await using var agent = Agent.Start(0m, Account());
        await agent.AskAsync(new Deposit(10)).Within();

        var rejected = await agent.TryAskAsync(new Withdraw(25)).Within();
        var accepted = await agent.TryAskAsync(new Withdraw(4)).Within();

        Assert.False(rejected.IsSuccess);
        var error = Assert.IsType<InsufficientFundsError>(rejected.Error);
        Assert.Equal((10m, 25m), (error.Balance, error.Requested));
        Assert.Equal(6m, accepted.Value);
        Assert.Equal(6m, agent.CurrentState);
    }

    [Fact]
    public async Task Rejections_reach_AskAsync_as_a_typed_carrier_exception()
    {
        await using var agent = Agent.Start(0m, Account());

        var thrown = await Assert.ThrowsAsync<DomainErrorException>(() => agent.AskAsync(new Withdraw(1)).Within());

        Assert.IsType<InsufficientFundsError>(thrown.Error);
        Assert.Equal(AgentStatus.Running, agent.Status);   // a rejection is an outcome, not a failure
    }

    [Fact]
    public async Task Builder_lifecycle_hooks_run()
    {
        var stopped = new TaskCompletionSource<int>();
        var agent = Agent.Start(0, MessageHandler.For<int, CounterMessage>()
            .OnStarted((_, _) => new ValueTask<int>(41))
            .On<Increment>((count, _) => count + 1)
            .On<GetCount, int>((count, _) => (count, count))
            .OnStopped((final, _) =>
            {
                stopped.SetResult(final);
                return ValueTask.CompletedTask;
            })
            .Build());

        agent.TryPost(new Increment());
        Assert.Equal(42, await agent.AskAsync(new GetCount()).Within());
        await agent.StopAsync().Within();

        Assert.Equal(42, await stopped.Task.Within());
    }

    public sealed record Shout(string Text) : IRequest<string>;

    public sealed record Whisper(string Text) : IRequest<string>;

    public sealed record Note(string Text);

    [Fact]
    public async Task Stateless_builder_routes_messages_and_requests()
    {
        var notes = new List<string>();
        await using var agent = Agent.Start(MessageHandler.For<object>()
            .On<Note>(note => notes.Add(note.Text))
            .On<Shout, string>(shout => shout.Text.ToUpperInvariant())
            .On<Whisper, string>(whisper => whisper.Text.Length > 3
                ? OperationResult.Reject(new TooLongError(whisper.Text.Length))
                : whisper.Text.ToLowerInvariant())
            .Build());

        agent.TryPost(new Note("hi"));

        Assert.Equal("HEY", await agent.AskAsync(new Shout("hey")).Within());
        Assert.Equal("ok", await agent.AskAsync(new Whisper("OK")).Within());
        Assert.IsType<TooLongError>((await agent.TryAskAsync(new Whisper("LONGER")).Within()).Error);
        Assert.Equal(["hi"], notes);
    }

    public sealed record TooLongError(int Length) : IDomainError;

    private sealed class WrongChannel : IReplyChannel<string>
    {
        public Exception? Failure { get; private set; }

        public bool TryReply(string value) => true;

        public bool TryFail(Exception error)
        {
            Failure = error;
            return true;
        }

        public bool TryReject(IDomainError error) => true;

        public bool TryCancel() => true;

        public bool IsCompleted => Failure is not null;

        public CancellationToken Cancellation => CancellationToken.None;
    }

    [Fact]
    public async Task A_reply_channel_of_the_wrong_type_is_failed_rather_than_ignored()
    {
        await using var agent = Agent.Start(0, MessageHandler.For<int, CounterMessage>()
            .On<GetCount, int>((count, _) => (count, count))
            .Build());
        var channel = new WrongChannel();

        await agent.DeliverAsync(new Envelope<CounterMessage>(new GetCount(), channel)).Within();

        await TestTimeouts.Eventually(() => channel.Failure is not null);
        Assert.IsType<InvalidOperationException>(channel.Failure);
    }
}
