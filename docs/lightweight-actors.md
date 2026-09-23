# Lightweight Actors

Typewright.Actor builds upon Typewright.Validation to provide strongly-typed actors, built on agents which work similarly to the F# Mailbox Processor, but with a modular architecture.

## Architecture

Architecturally, the goal is to keep the part that works like F# MailboxProcessor pretty much independent, so that while you *can* build a whole system of named/addressable actors that communicate with each other or other actor systems via address, you can also just easily start up a single anonymous agent that is only used directly by code which has a reference to the object, without the overhead or additional complexity of starting up a whole actor system.

### Components

`IMailbox<T>`​: The mailbox/message queue.  Strongly typed, unbounded FIFO for the default implementation but alternate implementations could use priority queues, implement bounded queues, etc.  Should probably implement `IAsyncEnumerable<T>`​; in C# using `System.Threading.Channels` seems like a good choice for a default implementation.  We could split this into reader/writer sides like `Channel` if that would be beneficial. 

`IDispatcher`​: The execution scheduler/dispatcher.  The default implementation would just await​ the processing of each message with `ConfigureAwait(false)`​, but alternative implementations could use a pinned single thread, a private thread pool, etc.

`IReplyChannel<T>`​: The callback channel for messages that expect a response.  The default implementation would just be a wrapper over `TaskCompletionSource<T>`​.  I'm thinking the reply channel would have a method that takes an Exception to invoke on failure, such that failure can propagate to the call-site like a task throwing an exception.

`Envelope<T>`​: An envelope for the message wrapping the payload and the reply channel as well as other metadata if needed (sender maybe).  Unlike F# I don't want to force the caller to embed the reply channel in the message payload because I want to make it possible to use this with different communication mechanisms and therefore having the reply channel in the payload wouldn't make much sense.   The payload presumably needs to be something you can serialize/deserialize cleanly.

`IMessageHandler<T>`: An interface with a single method for processing the message (`Envelope<T>`).  Convenience overloads would simply allow the caller to pass a lambda expression but having the interface means we can have more complex class-based handlers when needed (e.g. if the handler needs external dependencies via DI).  We can also have message handlers with pipelines to implement ASP.NET-style middlewares for logging/telemetry/etc. and potentially add Typewright.Validation as an out-of-the-box pipeline component when we get to the network-able versions that might serialize messages and send them over the wire.

`IFailurePolicy<T>`: An injection point for handling failures via a supervisor, or just logging them, etc.  

`IAgent<T>`: An interface for the surface area of the agent exposed to clients, enabling them to post messages.

Then the "Mailbox Processor" would be called "Agent" look something like (pseudocode for conceptual/demonstration purposes, not to be taken as a literal reference implementation):

```csharp
public class Agent<T>(IMailbox<T> inbox, IDispatcher dispatcher, IMessageHandler<T> messageHandler, IFailurePolicy<T> failurePolicy) : IAgent<T>
{
    public Task StartAsync()
    {
        return dispatcher.ExecuteAsync(async () => 
        {
            await foreach (var message in inbox)
            {
                try 
                {
                    await dispatcher.ExecuteAsync(() => messageHandler.ProcessMessageAsync(message));
                }
                catch (Exception ex)
                {
                    if (message.ReplyChannel is not null) 
                    {
                        message.ReplyChannel.Reply(ex);
                    }

                    var decision = failurePolicy.OnError(message, ex);

                    // Evaluate decision from the failure policy and decide whether to continue, restart, shutdown, etc.
                }
            }
        });
    }

    public void Post(T message) 
    {
        inbox.Enqueue(Envelope.Create(message));
    }

    public Task<TReply> PostAndReplyAsync<TReply>(T message, CancellationToken? cancellationToken = default)
    {
        var envelope = Envelope.Create<T, TReply>(message);
        inbox.Enqueue(envelope);
        await envelope.ReplyTo.WaitForReplyAsync(cancellationToken);
    }
}
```

There will also be a version with two type parameters `Agent<TState, TMessage>()`​ that takes an initial `TState`​ and threads it through each call to `ProcessMessageAsync`​ which changes to be a `Task<TState> ProcessMessageAsync(TState state, Envelope<TMessage> message)`​ that returns the next state.

The reply channel interface will need to have a non-generic base class that can be a member of the Envelope, since the envelop type signature can't carry multiple response types for the different requests an agent might process.

```csharp
public interface IReplyChannel
{
    bool TryFail(Exception error);
    bool TryCancel();
}

public interface IReplyChannel<in TReply> : IReplyChannel
{
    bool TryReply(TReply value);
}

public readonly record struct Envelope<T>(T Payload, IReplyChannel? ReplyTo, MessageHeaders Headers);
```

### Notes

**1. C# gotcha:** We may hit problems similar to MediatR in terms of handling the strongly-typed replies. We could maybe use a small extension method like `envelope.Reply(request, value)` to cast `IReplyChannel` to the correct `IReplyChannel<TReply>` using a marker interface.

**2. Create the default reply source with `TaskCreationOptions.RunContinuationsAsynchronously`.** Without it, calling `TrySetResult` runs the caller's `await` continuation inline, on the agent's loop. A slow or blocking caller then stalls the agent, and with a pinned-thread dispatcher this can deadlock.

**3. The dispatcher contract is really about where continuations run.** The subtler issue is that the processing loop itself must not use `ConfigureAwait(false)`. If a pinned-thread dispatcher installs a `SynchronizationContext`, a `ConfigureAwait(false)` anywhere in the loop sends continuations back to the thread pool, and the pinning silently stops working.

The cleanest contract: the dispatcher owns the entire loop and supplies the context. You may not need the per-message `ExecuteAsync` call at all. Letting a default implementation wrap any `TaskScheduler` would also let people plug in existing schedulers, like `ConcurrentExclusiveSchedulerPair`, for free.

## Additional Considerations

**A full, standalone message-target interface**. I currently have methods like `Post` and `PostAndReplyAsync` on the `IAgent<T>`, but it may be better to have a standalone message target/destination interface.  And we probably want variations like `Post`, `TryPost`, `PostAsync` (waits when the mailbox is full), and so on. This is the most important addition for the actor layer. As long as callers depend on the target interface rather than on a concrete `Agent<T>`, a remote actor proxy can implement the same interface, and code won't care whether the agent is local or across the network. A non-generic `Post(object)` could be helpful once routing delivers messages whose type isn't known at compile time, but we want to stay strongly typed as much as possible.

**Lifecycle.** The agent needs:
- a `Completion` task,
- a graceful stop that drains the mailbox,
- an abort that cancels in-flight work,
- `IAsyncDisposable`,
- a `CancellationToken` passed to the handler.

Supervision can't restart or stop an agent it can't observe.

**Mailbox capacity and overflow policy.** Bounded versus unbounded, and what happens when the mailbox is full: wait, drop the oldest, drop the newest, or reject. The default implementation over `Channel<T>` gets all of these nearly for free.

**Envelope headers.** Correlation ID, sender, timestamp, and room for more. Adding a headers slot now avoids a breaking change when the actor layer needs sender addresses and tracing context.

**An interface/abstraction for the interceptor pipeline around message handling, and its components.** This is where logging, metrics, tracing, and timeouts belong. It's also a natural place to validate payloads with Typewright.Validation before a handler ever sees them when they come in over the wire and are deserialized to a raw DTO.  We would want the pipeline to be extensible with custom components so that more complex actor systems can have all the cross-cutting concerns handled transparently on each message. An empty pipeline would be the default for local agents.

## Compatibility with the Actor layer

Nothing here conflicts with adding a more sophisticated Actor system with addressing on top of these Agents, as long as these things stay true:
- The reply channel is an interface in the envelope, not part of the payload. A remote reply channel can then implement the same interface and route the reply back by address.
- Serializability is required only at the remote boundary. Local agents shouldn't pay for it.
- Failure and lifecycle hooks exist from the start.
- Callers talk to the target interface rather than the concrete agent.
