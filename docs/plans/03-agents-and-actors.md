# Plan 03: Lightweight Agents and Actors

Status: phases 0–3 and 5 implemented (2026-09-23); phase 4 split into plan 04 (see §9)
Scope: `Typewright.Agents` (new), `Typewright.Actors` (replaces the `Typewright.Actor` placeholder)
Builds on: [docs/lightweight-actors.md](../lightweight-actors.md), [docs/vision.md](../vision.md), plan 02 (typed domain errors)
Feeds: [docs/object-database.md](../object-database.md) (Lattice), the REST document layer

## 1. Goal

A C# `MailboxProcessor` with replaceable parts, and an addressable actor layer built on it
without being required by it:

```csharp
// An anonymous agent: no system, no address, just a reference.
var counter = Agent.Start(0, MessageHandler.For<int, CounterMessage>()
    .On<Increment>(static (count, _) => count + 1)
    .On<GetCount, int>(static (count, _) => (count, count))
    .Build());

counter.TryPost(new Increment());
var total = await counter.AskAsync(new GetCount());   // Task<int>, inferred from the request type
```

Everything in the agent layer serves that snippet; everything in the actor layer serves
Lattice's "one actor per key, addressed by path, spawned on demand, read without
queueing" (object-database.md).

## 2. Review of the design notes

### 2.1 Agreed, and adopted as written

- **Interfaces for every part** (mailbox, dispatcher, handler, failure policy, reply
  channel) with defaults over `System.Threading.Channels`, `TaskCompletionSource`, and
  the thread pool.
- **The reply channel lives in the envelope, not the payload** (note 1 / compatibility
  section). This is what lets a remote proxy substitute a channel that routes the reply
  back by address, and it keeps payloads serializable without ceremony.
- **Non-generic `IReplyChannel` base with `TryFail`/`TryCancel`, generic `TryReply`**,
  with `in TReply` variance. Correct as sketched.
- **`RunContinuationsAsynchronously` on the reply source** (note 2). Mandatory; a test
  in phase 0 pins it (a caller that blocks after `await` must not stall the agent).
- **The dispatcher owns the whole loop** (note 3). The per-message `ExecuteAsync` in
  the pseudocode goes away; the loop never uses `ConfigureAwait(false)`; a
  `TaskScheduler`-wrapping dispatcher makes `ConcurrentExclusiveSchedulerPair` and
  friends free.
- **A standalone target interface**, headers slot, lifecycle surface, capacity and
  overflow policy, interceptor pipeline, and "serializability only at the remote
  boundary" — all from the "Additional Considerations" section, all in.

### 2.2 Changes, and why

**C1. Request/reply typing: type both ends instead of casting in the middle.** Note 1
proposes a MediatR-style `envelope.Reply(request, value)` cast. Typewright's answer is
to make the reply type flow from the message type at *both* ends so the one cast is
internal and correct by construction:

- Requests declare their reply: `record GetCount : CounterMessage, IRequest<int>;`
- Senders ask through the request: `Task<TReply> AskAsync<TReply>(IRequest<TReply>)` —
  `TReply` is inferred, so a sender cannot create a mismatched channel.
- Handlers are registered per request with the reply in the signature:
  `.On<GetCount, int>((state, msg) => (state, reply))` where
  `On<TRequest, TReply>` is constrained `TRequest : TMessage, IRequest<TReply>`. The
  handler cannot reply with the wrong type; the framework performs the single
  `IReplyChannel<TReply>` cast, which the sender-side typing guarantees.

What is *not* compile-checked in v1: that an `IRequest<TReply>` passed to
`IMessageTarget<TMessage>.AskAsync` is actually a `TMessage` (C# does not infer type
arguments from constraints, so a fully checked signature would force callers to spell
`AskAsync<GetCount, int>(...)`). It is a runtime check at post time. The fully checked
path is generating messages and a typed proxy from an actor *interface* with the
Typewright generator (Orleans-style, `Task<int> GetCount()`), which is a natural later
phase now that the generator infrastructure exists. Section 8.

**C2. Domain outcomes are replies, faults are failures.** `IFailurePolicy` and
`TryFail(Exception)` are exception-centric, which is right for *faults* (bugs, I/O).
But plan 02 made domain errors typed, and the actors are where `NotFound`/`Conflict`
will arise (the actor owning `/orders/42` is what knows the order is gone). Keep the
agent layer ignorant of domain errors, but do not force them through exceptions: add
`IReplyChannel.TryReject(IDomainError)` from day one and a `TryAskAsync` that returns
an `OperationResult<TReply>` (reply or `IDomainError`; faults still throw). The request
message type is then the natural unit to carry a `[Reports<...>]` error set, which is
exactly what the REST layer's exhaustive status mapping (plan 02 §8) needs.

**C3. The stateful form is the primary one, and state must be readable without
queueing.** `Agent<TState, TMessage>` is mentioned as a variant; for Lattice it is the
main event, and object-database.md adds a requirement the notes do not cover: GETs read
the actor's current state without going through the mailbox. So the stateful agent
exposes `CurrentState` — the last *committed* state, published with a volatile write
after each handler completes — and requires immutable state (records) for that to be
safe. One core loop, stateless agents as the `Unit`-state adapter.

**C4. Undelivered requests must not hang their callers.** Not in the notes: on stop,
abort, or a failure-policy `Stop`, every envelope still in the mailbox that carries a
reply channel gets `TryFail(new AgentStoppedException(...))`, and cancelling an
`AskAsync` marks its channel so a later `TryReply` is a no-op. Without this,
supervision-driven stops leak awaiting callers.

**C5. Split the packages.** The notes want the mailbox-processor part "pretty much
independent" of any actor system. Make that structural: `Typewright.Agents` (zero
dependencies beyond the BCL) and `Typewright.Actors` (depends on Agents, on
`Typewright.Validation` for address types, and on `Microsoft.Extensions.*`
abstractions for DI and hosting). The compiler then guarantees the agent layer never
grows an actor-system dependency. The existing `Typewright.Actor` placeholder is
renamed to `Typewright.Actors` (plural, matching the current wording).

**C6. Smaller corrections to the sketch.**

- `CancellationToken? cancellationToken = default` → non-nullable `CancellationToken`.
- `StartAsync` returning the loop task conflates "start" with "completion". Use
  `Start()` plus a `Completion` task; posting before `Start()` buffers (F# semantics).
- The mailbox holds envelopes, so `IMailbox<TMessage>` reads as
  `IAsyncEnumerable<Envelope<TMessage>>`; the writer side is what the target interface
  abstracts, so no separate reader/writer split is needed.
- `Envelope<T>` as a `readonly record struct` is right — `Channel<Envelope<T>>` stores
  it inline, no allocation per message beyond the reply channel for requests.
- An unrouted message (no `On<>` for its runtime type) is a failure (`TryFail` with
  `UnhandledMessageException`, policy decides; default `Resume`), not silently dropped.
- Reentrancy: an agent that `AskAsync`es itself deadlocks by construction. Documented,
  and the actor layer's `ActorContext` exposes `Self` as fire-and-forget only.

**C7. Cancelled requests are skipped at dequeue; in-flight cancellation is opt-in.**
(Resolved in review; revises D7.) A caller who cancels an ask gets
`OperationCanceledException` and does not know the outcome — true whether the agent
runs the work anyway (wait-only) or skips it. So skipping adds no new state for the
caller, matches .NET's cooperative-cancellation contract, and matches the remote
boundary, where a timed-out request genuinely may or may not have been applied. What it
changes is the agent: under overload (Lattice's per-key actor backed up, HTTP callers
timing out), it sheds abandoned work instead of grinding through it, which is what
prevents a backlog from sustaining itself.

- No new envelope field: the reply channel already registers on the caller's token, so
  it exposes `IsCompleted` (free on the default TCS channel) and the loop skips any
  envelope whose channel is already settled, *before* the interceptor pipeline runs.
  Skips are counted on the agent's meter so shedding is observable.
  `AgentOptions.SkipCancelledRequests` (default `true`) opts an agent out when "every
  accepted command is applied" matters more than shedding.
- Only requests are affected: `TryPost` carries no token, and `PostAsync`'s token
  governs only the wait for mailbox space — once enqueued, a fire-and-forget message
  runs.
- In-flight: the channel exposes the caller's token as `Cancellation`. Router handlers
  registered with a `CancellationToken` parameter receive a token that fires on agent
  stop *or* caller cancellation; the linked source is allocated only for those
  overloads and only when the caller's token can cancel. An `OperationCanceledException`
  caused by the caller's token is a cancellation, not a failure: state is not committed
  (safe because state is immutable and committed on return) and the failure policy is
  not consulted. Side effects the handler already performed are its own concern — hence
  opt-in by signature.
- Independent of this choice: a caller that retries a command after cancelling must be
  able to do so safely. `CorrelationId` is the idempotency key the Lattice layer will
  use.

## 3. Agents (`Typewright.Agents`)

### 3.1 Messages, envelopes, replies

```csharp
public interface IRequest<TReply>;                          // marker: this message expects a TReply

public readonly record struct Envelope<TMessage>(
    TMessage Message,
    IReplyChannel? ReplyTo,
    MessageHeaders Headers);

public sealed record MessageHeaders
{
    public CorrelationId CorrelationId { get; init; }        // Typewright domain value (Guid raw)
    public string? Sender { get; init; }                     // actor path once the actor layer sets it
    public DateTimeOffset Timestamp { get; init; }
    public string? TraceParent { get; init; }                // W3C trace context, primitive so it serializes
    public ImmutableDictionary<string, string> Extra { get; init; }
}

public interface IReplyChannel
{
    bool TryFail(Exception error);          // fault: surfaces as a thrown exception at the caller
    bool TryReject(IDomainError error);     // domain outcome: surfaces as a typed result (C2)
    bool TryCancel();
    bool IsCompleted { get; }               // settled already (e.g. caller cancelled): skipped at dequeue (C7)
    CancellationToken Cancellation { get; } // the caller's token, for opt-in in-flight cancellation (C7)
}

public interface IReplyChannel<in TReply> : IReplyChannel
{
    bool TryReply(TReply value);
}
```

Default channel: `TaskCompletionSource<TReply>` created with
`RunContinuationsAsynchronously`; the caller's cancellation token registers
`TryCancel`, after which every `Try*` returns false. `IsCompleted` reads the task's
state and `Cancellation` returns the caller's token as-is, so neither allocates.

### 3.2 The target interface

```csharp
public interface IMessageTarget<in TMessage>
{
    bool TryPost(TMessage message);                                   // never waits; false when full/stopped
    ValueTask PostAsync(TMessage message, CancellationToken ct = default);   // waits on a full bounded mailbox
    Task<TReply> AskAsync<TReply>(IRequest<TReply> request, CancellationToken ct = default);
    Task<OperationResult<TReply>> TryAskAsync<TReply>(IRequest<TReply> request, CancellationToken ct = default);
}
```

Contravariant, so an `IMessageTarget<CounterMessage>` is usable where an
`IMessageTarget<Increment>` is expected. Everything that sends — application code,
supervisors, remote proxies, the REST layer — depends on this, never on `Agent<,>`.

### 3.3 Mailbox

```csharp
public interface IMailbox<TMessage>
{
    bool TryPost(Envelope<TMessage> envelope);
    ValueTask PostAsync(Envelope<TMessage> envelope, CancellationToken ct);
    IAsyncEnumerable<Envelope<TMessage>> ReadAllAsync(CancellationToken ct);
    void Complete();                       // no further posts; readers drain what is queued
    IReadOnlyCollection<Envelope<TMessage>> DrainUndelivered();   // for C4 on abort/stop
    int Count { get; }                     // approximate, for metrics
}

public sealed record MailboxOptions(int? Capacity = null, MailboxFullMode FullMode = MailboxFullMode.Wait);
public enum MailboxFullMode { Wait, DropNewest, DropOldest, Reject }
```

Default: `ChannelMailbox<TMessage>` over `Channel<Envelope<TMessage>>`, unbounded unless
`Capacity` is set, `FullMode` mapping to `BoundedChannelFullMode`. A priority mailbox is
a second implementation and a test that the abstraction holds.

### 3.4 Dispatcher

```csharp
public interface IDispatcher
{
    Task RunAsync(Func<CancellationToken, Task> loop, CancellationToken ct);
}
```

- `ThreadPoolDispatcher` (default): `Task.Run(loop)`.
- `TaskSchedulerDispatcher(TaskScheduler)`: any scheduler, including
  `ConcurrentExclusiveSchedulerPair.ExclusiveScheduler`.
- `DedicatedThreadDispatcher`: one thread with a `SynchronizationContext` that pumps a
  work queue, so continuations stay on the thread. Its test asserts the thread id
  across awaits and documents that handler code using `ConfigureAwait(false)` opts out.

### 3.5 Handlers and the typed router

```csharp
public interface IMessageHandler<TState, in TMessage>
{
    ValueTask<TState> HandleAsync(TState state, Envelope<TMessage> envelope, CancellationToken ct);
}

public static class MessageHandler
{
    public static MessageHandlerBuilder<TState, TMessage> For<TState, TMessage>();
}

public sealed class MessageHandlerBuilder<TState, TMessage>
{
    // fire-and-forget (and requests handled without replying)
    public Builder On<TCase>(Func<TState, TCase, TState> handle) where TCase : TMessage;
    public Builder On<TCase>(Func<TState, TCase, CancellationToken, ValueTask<TState>> handle) where TCase : TMessage;

    // request/reply: reply type checked against the request's declaration (C1)
    public Builder On<TRequest, TReply>(Func<TState, TRequest, (TState State, TReply Reply)> handle)
        where TRequest : TMessage, IRequest<TReply>;
    public Builder On<TRequest, TReply>(Func<TState, TRequest, CancellationToken, ValueTask<(TState, TReply)>> handle)
        where TRequest : TMessage, IRequest<TReply>;
    public Builder On<TRequest, TReply>(Func<TState, TRequest, (TState State, OperationResult<TReply> Result)> handle)
        where TRequest : TMessage, IRequest<TReply>;      // reply or reject (C2)

    public Builder Otherwise(Func<TState, Envelope<TMessage>, ValueTask<TState>> handle);   // default: unhandled = failure
    public IMessageHandler<TState, TMessage> Build();
}
```

Routing is by runtime type, most-derived registration first; the builder rejects
duplicate registrations. Class-based handlers (DI, dependencies) implement
`IMessageHandler<TState, TMessage>` directly and may use the builder internally.
Stateless handlers are `IMessageHandler<Unit, TMessage>` behind a
`IMessageHandler<TMessage>` adapter.

### 3.6 Interceptor pipeline

```csharp
public delegate ValueTask MessageDelegate<TMessage>(Envelope<TMessage> envelope, CancellationToken ct);

public interface IMessageInterceptor<TMessage>
{
    ValueTask InvokeAsync(Envelope<TMessage> envelope, MessageDelegate<TMessage> next, CancellationToken ct);
}
```

Composed once at agent construction, ASP.NET-style; the innermost delegate threads
state through the handler. Empty by default. Built-ins in the agent package, all on BCL
diagnostics types: `TracingInterceptor` (`ActivitySource`, one span per message,
`TraceParent` header propagation), `MetricsInterceptor` (`Meter`: processed, failed,
handling duration, mailbox depth), `TimeoutInterceptor`. `ILogger`-based logging lives
in the actors package (it is the first `Microsoft.Extensions` dependency).

### 3.7 Failure policy

```csharp
public enum FailureDecision { Resume, Restart, Stop, Escalate }

public interface IFailurePolicy<TMessage>
{
    FailureDecision OnFailure(Envelope<TMessage> envelope, Exception exception, FailureContext context);
}
```

`FailureContext` carries consecutive-failure count and time since the last failure so
policies like "restart up to 3 times per minute, then stop" are a few lines. The loop
always `TryFail`s the message's reply channel first, then applies the decision:
`Resume` continues with the current state, `Restart` resets to the initial state (and
runs an optional `OnRestart` hook), `Stop` completes the agent and drains (C4),
`Escalate` is `Stop` plus a signal the actor layer's supervisor observes. Default
policy: `Resume`, with the exception surfaced on the `Completion` task's exception list
only for `Stop`.

### 3.8 Agent and lifecycle

```csharp
public sealed class Agent<TState, TMessage> : IAgent<TMessage>, IAsyncDisposable
{
    public TState CurrentState { get; }              // last committed state (C3); TState must be immutable
    public void Start();
    public Task Completion { get; }
    public Task StopAsync(CancellationToken ct = default);   // Complete mailbox, drain, then complete
    public Task AbortAsync();                                // cancel in-flight work, fail undelivered requests
    // IMessageTarget<TMessage> members
}

public static class Agent
{
    public static Agent<TState, TMessage> Start<TState, TMessage>(TState initial, IMessageHandler<TState, TMessage> handler, AgentOptions<TMessage>? options = null);
    public static Agent<TMessage> Start<TMessage>(IMessageHandler<TMessage> handler, AgentOptions<TMessage>? options = null);
}
```

`AgentOptions<TMessage>` bundles mailbox, dispatcher, failure policy, interceptors, and
a name (for diagnostics). `DisposeAsync` is `StopAsync` with a bounded grace period
then `AbortAsync`. The raw `IMessageHandler` receives the agent's stop token.
Per-request cancellation follows C7: the caller stops waiting immediately; a request
not yet dequeued is skipped (unless `SkipCancelledRequests` is off); a request already
running completes unless its handler opted into the caller's token, and its reply is
dropped either way.

Semantics the tests pin: FIFO ordering per agent; one message in flight at a time;
`TryPost` after `Complete` is false and `PostAsync` throws `AgentStoppedException`;
every queued request is failed on stop/abort; `CurrentState` is never a
partially-applied state.

## 4. Actors (`Typewright.Actors`)

An actor is an agent plus an address, a system that owns its lifecycle, and a
supervisor. Nothing in the agent package changes to support it.

### 4.1 Addressing

```csharp
[DomainValue<string>]
[Pattern(@"^/[A-Za-z0-9._~-]+(/[A-Za-z0-9._~-]+)*$")]
public readonly partial record struct ActorPath;     // dogfoods plan 01: parses, validates, binds from routes
```

`/orders`, `/orders/42`. Parent/child is path containment. Lattice's resource URIs and
the REST layer's routes are the same values, which is the point.

### 4.2 System, references, spawning

```csharp
public interface IActorSystem : IAsyncDisposable
{
    IActorRef<TMessage> Spawn<TState, TMessage>(ActorPath path, ActorDefinition<TState, TMessage> definition);
    IActorRef<TMessage> GetOrSpawn<TState, TMessage>(ActorPath path, ActorDefinition<TState, TMessage> definition);   // Lattice's per-key actors
    IActorRef<TMessage>? Resolve<TMessage>(ActorPath path);
    Task StopAsync(ActorPath path, CancellationToken ct = default);
}

public interface IActorRef<in TMessage> : IMessageTarget<TMessage>
{
    ActorPath Path { get; }
}
```

`ActorDefinition<TState, TMessage>` is the factory: initial state, handler (or a
`Func<IServiceProvider, IMessageHandler<,>>` for DI), and `AgentOptions`. The system
stamps `Sender` and `CorrelationId` headers, keeps the registry, and exposes
`CurrentState` reads for Lattice through `IActorRef<TMessage>` extensions on stateful
actors (`TryGetState<TState>()`).

### 4.3 Supervision

Supervisors are actors whose children are the paths beneath them. A child's
`Escalate` decision is delivered to the parent as a `ChildFailed` message; the parent's
handler (or a built-in `SupervisionStrategy`: one-for-one restart with limits, stop,
escalate further) decides. Restart re-creates the child from its definition at the same
path; references stay valid because `IActorRef` addresses the path, not the instance.

### 4.4 Hosting and DI

`Typewright.Actors` references `Microsoft.Extensions.DependencyInjection.Abstractions`
and `Microsoft.Extensions.Hosting.Abstractions`: `services.AddActorSystem(...)`
registers `IActorSystem` as a singleton and an `IHostedService` that stops the system
on shutdown; handlers resolved from DI get scoped services per message when they ask
for them. This is the seam the REST document layer plugs into.

### 4.5 The remote boundary

Deliberately last, and possibly its own plan. The vision scopes remoting to "a
communication actor that exchanges messages with the other system over HTTP", not
clustering. The design here only has to keep it possible:

- A `RemoteActorRef<TMessage>` implements `IMessageTarget<TMessage>` by serializing the
  envelope (payload as its DTO shape, headers as primitives) and posting it to an
  `IActorTransport`; a `RemoteReplyChannel` routes the reply back by `CorrelationId`.
- Inbound messages are raw DTOs. A `ValidationInterceptor` built on
  `Typewright.Validation` turns them into typed messages with `TryCreate` before any
  handler runs, rejecting with the accumulated errors — the same contract as HTTP
  binding in plan 01, and the reason payloads should be domain models.
- The HTTP transport is one actor pair per direction, exactly as the vision describes.

## 5. Repository layout

```
src/Typewright/
  Typewright.Agents/                 IRequest, Envelope, reply channels, mailbox, dispatcher,
                                     handler + typed router, interceptors, failure policy, Agent
  Typewright.Actors/                 ActorPath, IActorSystem, IActorRef, supervision, hosting/DI
                                     (renamed from the Typewright.Actor placeholder)
  tests/Typewright.Agents.Tests/     semantics tests (ordering, lifecycle, failure, dispatchers, C4)
  tests/Typewright.Actors.Tests/     system/supervision/DI tests
  benchmarks/Typewright.Agents.Benchmarks/   post throughput, ask latency, per-dispatcher
  samples/Ledger.Actors/             bank-ledger actor system: one actor per account, a
                                     supervisor, HTTP endpoints in domain types (plan 01)
                                     asking the actors, typed rejections mapped to responses (C2)
```

Packaging: `Typewright.Agents` references nothing beyond the BCL (`System.Threading.Channels`
and `System.Diagnostics.*` are inbox). `Typewright.Actors` references Agents, Validation,
and the two `Microsoft.Extensions.*.Abstractions` packages.

## 6. Phases

Each phase ends green, committed, and demonstrable.

**Phase 0: Agent core.** Contracts from §3.1–3.4 and §3.7–3.8 with the default
implementations (channel mailbox, three dispatchers, TCS reply channel, resume policy),
the stateful loop with `CurrentState`, stateless adapter, lifecycle, and C4 draining.
Handler is the raw `IMessageHandler<,>` only. Tests pin ordering, one-at-a-time,
buffering before `Start`, stop/abort semantics, undelivered-request failure,
`RunContinuationsAsynchronously`, dedicated-thread affinity. Benchmark: posts/sec and
ask round-trip on each dispatcher.

**Phase 1: Typed request/reply.** `IRequest<TReply>`, `AskAsync`/`TryAskAsync`,
`OperationResult<TReply>`, `TryReject`, the `MessageHandler.For<,>()` router with the
constrained `On<TRequest, TReply>` overloads, unhandled-message failure, and C7
cancellation (dequeue skip with its metric and opt-out, the linked token for
token-accepting router overloads, caller-cancellation OCE treated as cancellation).
The goal snippet in §1 compiles and runs.

**Phase 2: Pipeline and policies.** Interceptor pipeline, tracing/metrics/timeout
interceptors, `FailureContext`, restart with `OnRestart`, escalation signal, priority
mailbox as the second `IMailbox` implementation.

**Phase 3: Actor system.** Rename the placeholder; `ActorPath` (generated domain value),
`IActorSystem` with spawn/get-or-spawn/resolve/stop, `IActorRef`, header stamping,
`TryGetState`, one-for-one supervision, `AddActorSystem` hosting/DI. The Ledger sample:
HTTP endpoints in domain types ask account actors; `TryAskAsync` rejections
(`InsufficientFundsError`, `AccountNotFoundError`) become 409/404 through a mapping
that plan 02 §8's exhaustive machinery will later generate.

**Phase 4: Remote boundary.** Envelope serialization contract, `ValidationInterceptor`,
`RemoteActorRef` + `RemoteReplyChannel` by correlation id, the HTTP transport actor
pair, a two-process sample. Split into plan 04 if phase 3 lands before the REST layer's
needs are clearer. *(Split: see §9 and [plan 04](04-remote-actors.md).)*

**Phase 5: Hardening.** XML docs, README, allocation audit of the hot loop (target: zero
allocations per fire-and-forget message on the channel mailbox, one per request for the
reply channel), STJ source-gen for the remote DTOs, the interface-driven generated
proxies from C1 if wanted.

## 7. Decisions

Confirmed 2026-09-22 as recommended, except D7, revised during review (C7).

| # | Decision | Recommendation |
|---|----------|----------------|
| D1 | Packaging | Two packages: `Typewright.Agents` (BCL only) and `Typewright.Actors`; placeholder renamed to the plural. |
| D2 | Reply typing | `IRequest<TReply>` marker + constrained `On<TRequest, TReply>` registration; the one cast is internal (C1). Runtime check that a request is a `TMessage`. |
| D3 | Domain outcomes | `TryReject(IDomainError)` + `TryAskAsync` → `OperationResult<TReply>` from phase 1; faults stay exceptions (C2). |
| D4 | State | Stateful agent is the core; `CurrentState` is a volatile-published committed state; state types must be immutable (C3). |
| D5 | Dispatch | Dispatcher owns the loop; per-message `ExecuteAsync` dropped; three default dispatchers. |
| D6 | Lifecycle | `Start()` + `Completion`; buffering before start; stop drains, abort cancels; undelivered requests are failed (C4). |
| D7 | Request cancellation | Caller stops waiting immediately; not-yet-dequeued requests are skipped (default on, per-agent opt-out); in-flight cancellation is opt-in via token-accepting handlers (C7). |
| D8 | Failure decisions | `Resume` / `Restart` / `Stop` / `Escalate`; default `Resume`; reply channel always failed first. |
| D9 | Addressing | `ActorPath` is a generated Typewright domain value; parent/child by path containment. |
| D10 | Remoting | Last, HTTP-only, actor-pair transport per the vision; possibly its own plan. |

## 8. Forward hooks

- **Lattice**: `GetOrSpawn` per resource path, `CurrentState`/`TryGetState` for
  mailbox-free GETs, an interceptor that appends each command envelope to the
  append-only log (command sourcing), consistent-hash sharding as a later
  `IActorSystem` implementation whose `Resolve` may return a `RemoteActorRef`.
- **REST document layer**: routes are `ActorPath` values; requests are domain models
  validated at the HTTP boundary (plan 01), asked of the key's actor, rejected with
  typed errors mapped to statuses (plan 02 §8).
- **Generated actor interfaces (C1)**: `[Actor] interface IAccount { Task<Balance> Deposit(Amount a); }`
  → generated message records implementing `IRequest<Balance>`, a typed proxy over
  `IMessageTarget<AccountMessage>`, and a router — closing the last unchecked link
  with the generator infrastructure already in place.

## 9. Implementation notes (2026-09-23)

Phases 0–3 and 5 are implemented. There are 196 tests across the solution (52 for
agents, 33 for actors), and the concurrency-sensitive suites were run repeatedly to
check they are not flaky. The notes below record where the code departs from the text
above, and why.

**Packaging and vocabulary**

- `Typewright.Agents` references `Typewright.Validation`, not only the BCL: C2's
  `TryReject(IDomainError)` needs `IDomainError`. Validation is itself BCL-only, so the
  transitive closure is unchanged and the independence C5 wanted, meaning no
  actor-system or `Microsoft.Extensions` dependency, still holds.
- `OperationResult<T>`, `Rejection`, and `DomainErrorException` live in
  `Typewright.Validation` beside `IDomainError`, because the service layer (plan 02 §8)
  needs them without agents. `AskAsync` surfaces a rejection as `DomainErrorException`.
- The placeholder was renamed by moving its project file. The old
  `src/Typewright/Typewright.Actor/` directory could not be deleted while another
  process held its `bin`/`obj`; it contains only ignored build output.

**Contract shapes that differ from §3**

- `IDispatcher.Run(Func<Task> loop)`: the loop owns its tokens, so the dispatcher needs
  none.
- `IMailbox` is shaped like a channel reader: `TryPost`/`PostAsync` return a
  `PostResult` (`Posted`, `Full`, or `Closed`) so the agent can raise the right
  exception, and the loop uses `WaitToReadAsync`/`TryTake`. `ReadAllAsync` is a default
  interface method. `DrainUndelivered` was dropped because the agent drains with
  `TryTake`.
- `IMessageSink` (`TryPost`/`PostAsync`) was split out of `IMessageTarget`, which adds
  the asks. `ActorContext.Self` is a sink, so an actor cannot ask itself and deadlock.
- `IEnvelopeTarget` (`TryDeliver`/`DeliverAsync`) and the static `Delivery` helpers
  implement the message-level operations over envelopes. Agents, actor references, and
  a future remote proxy all send through them.
- Lifecycle hooks became `IAgentLifecycle<TState>`, with `OnStartedAsync`,
  `OnRestartingAsync`, and `OnStoppedAsync`. `OnStartedAsync` is the hydration point for
  Lattice, where an actor loads its key's state before its first message.
- `Completion` never ends as cancelled. A graceful stop or an abort completes it
  successfully; a `Stop` or `Escalate` decision, or a failing lifecycle hook, faults it.
  `AgentStatus` tells these cases apart. `StopAsync(token)` aborts when the token fires,
  matching `IHostedService.StopAsync`.
- Router: the `TCase : TMessage` constraint means interface routes need a message type
  that an interface can satisfy, such as an interface or `object`. The constraint is
  kept because it catches routes that can never match.

**Fixes made during implementation**

- The loop now suppresses `ExecutionContext` flow when the dispatcher starts it.
  Without this, the caller of `Start()` leaked its async-local values
  (`Activity.Current`, logging scopes) into every message the agent processed. An
  on-demand actor spawned inside an HTTP request would have parented all its later work
  to that request's trace.
- Dropping mailbox modes fail the dropped request's reply channel, so its caller does
  not hang (§3.3 implied this; it is now tested).

**Actors (§4)**

- Supervision strategies are functions on the parent's definition (`ChildSupervision`)
  or on the system (`DefaultSupervision`). §4.3 allowed "the parent's handler *or* a
  built-in strategy"; only strategies are implemented, and routing failures through the
  parent's mailbox is deferred.
- The `Resume` directive recreates the child from its last committed state; `Restart`
  recreates it from its definition. Either way the failed agent's queued requests have
  already failed, and the new agent starts with an empty mailbox. `Escalate` stops the
  child's subtree and treats the parent as failed. An escalation past the top level
  stops the actor and is logged at critical level.
- The default failure policy for actors stays `Resume`, the same as for agents.
  Escalating to supervision is opt-in per definition, because escalating fails every
  queued request.
- `ActorSystem` implements `IDisposable` as well as `IAsyncDisposable`, because hosts
  that dispose their container synchronously throw on services that are only
  async-disposable.

**Not done, and why**

- The queue-depth metric is not emitted. An interceptor never sees the mailbox, so depth
  is available only through the `MailboxCount` property.
- Requests the handler never replies to are not failed automatically. A deferred reply
  (keep the channel in state, reply from a later message) is a legitimate pattern. The
  interceptor contract documents that a short-circuit must settle the request.
- Generated actor interfaces (C1) remain a forward hook (§8).

**Measured (BenchmarkDotNet short job)**

| Operation | Time | Allocated |
|---|---|---|
| Fire-and-forget post, any dispatcher | 42–58 ns | 0 B |
| Agent ask round trip | 0.7–0.9 µs | 215 B |
| Actor ask round trip | about 1.0 µs | 472 B |

The actor figure includes stamped headers, a new correlation id, and the sender scope.
All three meet or approach the phase 5 target of no allocation per fire-and-forget
message and one reply channel per request.

**Phase 4 was split into plan 04.** The REST layer does not exist yet, so its needs are
no clearer than when this plan was written. Implementing phases 0–3 also exposed two
problems with §4.5, which [plan 04](04-remote-actors.md) addresses:

- A `ValidationInterceptor` inside the pipeline cannot turn raw DTOs into messages,
  because the mailbox is already typed. That conversion has to happen at the transport,
  before delivery.
- HTTP request/response already pairs an ask with its reply, so reply routing by
  correlation id is only needed for asynchronous transports.
