# Plan 04: Remote Actors over HTTP

Status: draft (2026-09-23), for review
Scope: a new `Typewright.Actors.Remote` package (name open, see D2)
Builds on: plan 03 (agents and actors), plan 01 (HTTP binding), plan 02 (typed errors)
Split from: plan 03, phase 4 (see plan 03 §9)

## 1. Goal

Two processes, each with its own actor system, send messages to each other's actors
through the same `IActorRef<TMessage>` they use locally. This is the vision's
"communication actor that exchanges messages with the other system over a normal
protocol like HTTP". It is not clustering, sharding, or location-transparent migration.

```csharp
// Process A: a reference to an actor that lives in process B.
IActorRef<AccountMessage> account = remote.Resolve<AccountMessage>(
    new Uri("https://ledger.internal"), ActorPath.Create("/accounts/AC000042"));

OperationResult<AccountSnapshot> result = await account.TryAskAsync(new Deposit(amount));
```

Code that depends on `IMessageTarget<TMessage>` cannot tell the difference, which was
plan 03's compatibility requirement.

## 2. What implementing plan 03 taught us

**Validation happens at the transport, not in the pipeline.** Plan 03 §4.5 proposed a
`ValidationInterceptor` that turns raw DTOs into typed messages. That cannot work: an
agent's mailbox holds `Envelope<TMessage>`, so by the time a message reaches the
pipeline it is already typed. The inbound endpoint must turn the wire DTO into a
`TMessage` with `TryCreate` first, which is exactly what plan 01's HTTP binding already
does for bodies, and reject invalid input with a 400 before anything reaches a mailbox.

**Request/response HTTP already pairs replies.** Plan 03 §4.5 proposed a
`RemoteReplyChannel` that routes replies back by correlation id. Over HTTP, the ask is
a request and the reply is its response, so no routing is needed. Correlation-based
reply routing only matters for one-way transports, which are out of scope (D5).

## 3. Design

### 3.1 Wire contract

- One endpoint per process: `POST /_actors/{**path}`. The body is a JSON object with
  `messageType` (a registered name, never a CLR type name), `message` (the payload as
  JSON), and `headers` (the `MessageHeaders` fields as primitives). `ask: true` marks a
  request.
- Fire-and-forget: `202 Accepted` once the message is enqueued.
- Ask: `200` with the reply as JSON, or `409` with a problem body carrying the rejection's
  `code` and payload, or `404` if no actor exists at the path, or `400` with the
  validation errors if the payload is invalid. A fault in the remote handler returns
  `500` with a problem body, which the caller surfaces as a `RemoteActorException`.
- The inbound endpoint delivers with `SkipCancelledRequests` semantics. The HTTP
  request's `RequestAborted` token is the caller's cancellation, so a client that gives
  up is skipped if its request is still queued.

### 3.2 The message and error registry

Serialization needs a closed mapping from wire names to types, for security (no
arbitrary type loading) and for stability (renaming a class must not break the wire):

```csharp
builder.Services.AddRemoteActors(remote => remote
    .Message<AccountMessage, Deposit>("ledger.deposit")
    .Message<AccountMessage, Withdraw>("ledger.withdraw")
    .Reply<AccountSnapshot>()
    .Error<InsufficientFundsError>()      // wire name defaults to the error's Code
    .Error<AccountNotFoundError>());
```

Messages that are `[DomainModel]`s travel as their DTO and are rebuilt with `TryCreate`
on arrival, so invalid input is rejected with every error, exactly as at the HTTP
boundary. Plain records travel through System.Text.Json directly. Errors travel as their
`Code` plus their payload, and are rebuilt into the registered `IDomainError` type, so
`TryAskAsync` on the calling side returns the same typed rejection it would get locally.

### 3.3 Components

- `RemoteActorRef<TMessage>`: implements `IActorRef<TMessage>` by posting to the remote
  endpoint through an `HttpClient`. It stamps headers like a local reference and maps
  responses back to replies, rejections, or exceptions.
- `RemoteActors.Resolve<TMessage>(Uri host, ActorPath path)`: creates references. There
  is no discovery; addresses are configured.
- `MapActorEndpoint()`: the inbound endpoint. It resolves the local actor (it never
  spawns, see D4), rebuilds the message, and delivers it.

## 4. Phases

**Phase A: Wire contract and registry.** The envelope JSON shape, the registry, and
round-trip tests for messages, replies, and errors, including `[DomainModel]` messages
rebuilt with `TryCreate`.

**Phase B: Inbound endpoint.** `MapActorEndpoint()` with the status mapping of §3.1,
tested with `WebApplicationFactory`.

**Phase C: Remote references.** `RemoteActorRef` and `Resolve`, tested against a second
in-memory server.

**Phase D: Two-process sample.** An example where one process's actors ask another
process's actors, plus a test that starts both.

## 5. Decisions to confirm

| # | Decision | Recommendation |
|---|----------|----------------|
| D1 | Transport | HTTP request/response only; one endpoint per process. |
| D2 | Package | A separate `Typewright.Actors.Remote` package, so local actors never depend on ASP.NET Core or `HttpClient` configuration. |
| D3 | Type identity | An explicit registry of wire names for messages, replies, and errors; never CLR type names. |
| D4 | Remote spawning | Not allowed: the endpoint only resolves existing actors. Spawning on demand across processes is a Lattice concern and belongs in that plan. |
| D5 | One-way transports | Out of scope; correlation-based reply routing is added only if a queue transport is needed. |
| D6 | Security | Out of scope for this plan, but the endpoint is added through ASP.NET routing, so standard authentication and authorization apply to it. |
| D7 | Scope | Only build this when the REST document layer or a concrete two-process need calls for it. |
