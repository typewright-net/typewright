using Typewright.Agents;
using Xunit;

namespace Typewright.Agents.Tests;

public class MailboxTests
{
    private static async Task<(Agent<int, TestMessage> Agent, TaskCompletionSource Gate)> StartBlockedAsync(MailboxOptions mailbox)
    {
        var gate = new TaskCompletionSource();
        var entered = new TaskCompletionSource();
        var agent = Agent.Start(0, new CounterHandler(), new AgentOptions<TestMessage>
        {
            Mailbox = () => new ChannelMailbox<TestMessage>(mailbox),
        });

        // The in-flight Block leaves the mailbox itself empty.
        agent.TryPost(new Block(gate, entered));
        await entered.Task.Within();
        return (agent, gate);
    }

    [Fact]
    public async Task Rejecting_mailbox_fails_posts_immediately_when_full()
    {
        var (agent, gate) = await StartBlockedAsync(new MailboxOptions(Capacity: 1, MailboxFullMode.Reject));
        await using var _ = agent;

        Assert.True(agent.TryPost(new Add(1)));
        Assert.False(agent.TryPost(new Add(1)));
        await Assert.ThrowsAsync<MailboxFullException>(() => agent.PostAsync(new Add(1)).Within());
        await Assert.ThrowsAsync<MailboxFullException>(() => agent.AskAsync(new Get()).Within());

        gate.SetResult();
        await TestTimeouts.Eventually(() => agent.CurrentState == 1 && agent.MailboxCount == 0);
        Assert.Equal(1, await agent.AskAsync(new Get()).Within());
    }

    [Fact]
    public async Task Waiting_mailbox_makes_PostAsync_wait_for_space()
    {
        var (agent, gate) = await StartBlockedAsync(new MailboxOptions(Capacity: 1, MailboxFullMode.Wait));
        await using var _ = agent;

        Assert.True(agent.TryPost(new Add(1)));
        Assert.False(agent.TryPost(new Add(1)));
        var waiting = agent.PostAsync(new Add(2)).AsTask();
        await Task.Delay(50);
        Assert.False(waiting.IsCompleted);

        gate.SetResult();
        await waiting.Within();
        Assert.Equal(3, await agent.AskAsync(new Get()).Within());
    }

    [Fact]
    public async Task Waiting_post_can_be_cancelled_without_enqueuing()
    {
        var (agent, gate) = await StartBlockedAsync(new MailboxOptions(Capacity: 1, MailboxFullMode.Wait));
        await using var _ = agent;
        agent.TryPost(new Add(1));

        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => agent.PostAsync(new Add(100), cancel.Token).Within());

        gate.SetResult();
        Assert.Equal(1, await agent.AskAsync(new Get()).Within());
    }

    [Fact]
    public async Task Dropping_mailbox_fails_the_dropped_request_so_its_caller_does_not_hang()
    {
        var (agent, gate) = await StartBlockedAsync(new MailboxOptions(Capacity: 1, MailboxFullMode.DropOldest));
        await using var _ = agent;

        var dropped = agent.AskAsync(new Get());
        Assert.True(agent.TryPost(new Add(5)));

        await Assert.ThrowsAsync<MessageDroppedException>(() => dropped.Within());
        gate.SetResult();

        // Wait for the drain: asking while Add(5) is still queued would drop *it* instead.
        await TestTimeouts.Eventually(() => agent.CurrentState == 5);
        Assert.Equal(5, await agent.AskAsync(new Get()).Within());
    }

    [Fact]
    public async Task Mailbox_reads_as_an_async_sequence_until_completed()
    {
        IMailbox<int> mailbox = new ChannelMailbox<int>();
        mailbox.TryPost(new Envelope<int>(1));
        mailbox.TryPost(new Envelope<int>(2));
        mailbox.Complete();

        var read = new List<int>();
        await foreach (var envelope in mailbox.ReadAllAsync())
        {
            read.Add(envelope.Message);
        }

        Assert.Equal([1, 2], read);
        Assert.Equal(PostResult.Closed, mailbox.TryPost(new Envelope<int>(3)));
    }
}
