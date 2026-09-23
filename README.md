# Typewright

Everything is a Type.

Typewright makes invalid domain data unrepresentable in application code and makes the
HTTP boundary the place where raw input is turned into domain types. An endpoint's
signature is written entirely in domain types; if the request cannot be turned into those
types, the caller gets a 400 carrying *every* validation failure, and the endpoint body
never runs:

```csharp
app.MapPut("/invoices/{id}", (InvoiceNumber id, BillingDocument doc, IBilling svc)
    => svc.Upsert(id, doc));
```

## Packages

| Project | What it is |
|---|---|
| `Typewright.Validation` | Contracts and primitives: `IDomainValue<TSelf, TRaw>`, `IDomainModel<TSelf, TDto>`, typed `IDomainError` records, `ValidationResult<T>`, `OperationResult<T>`, path-scoped `ValidationErrors`, `DomainValidationException`, rule attributes, DTO-routed JSON converters, `DomainTypeInfo` descriptors. No dependencies beyond the BCL; usable entirely by hand. |
| `Typewright.Validation.Generators` | Incremental source generator plus analyzers. Turns `[DomainValue<T>]` / `[DomainModel]` declarations into complete implementations and closes the `new`/`default`/`with` construction holes. |
| `Typewright.Validation.AspNetCore` | Minimal API binding helpers, `UseTypewright()` middleware writing RFC 9457 `HttpValidationProblemDetails`, an `IExceptionHandler` alternative, OpenAPI transformers, and MVC model binders. |
| `Typewright.Agents` | A C# `MailboxProcessor` with replaceable parts: mailbox, dispatcher, handler, reply channel, failure policy, interceptor pipeline. Typed request/reply, typed rejections, lifecycle, and failure policies. Depends only on `Typewright.Validation`, so nothing beyond the BCL. |
| `Typewright.Actors` | Addressable actors on top of agents: `ActorPath` addressing, an actor system with get-or-spawn per key, header stamping, supervision, and hosting/DI integration. |

## Field values

A field value wraps exactly one raw value and enforces its invariants. Declare the shape;
the generator emits everything else:

```csharp
[DomainValue<string>]
[Pattern(@"^I\d{9}$")]
public readonly partial record struct InvoiceNumber;

[DomainValue<DateOnly>(Comparable = true)]
public readonly partial record struct InvoiceDate;

public sealed record ChecksumError : IDomainError;

[DomainValue<string>]
[Length(1, 64), Normalize(Trim = true, Case = Casing.Upper)]
[Reports<ChecksumError>]
public readonly partial record struct AccountNumber
{
    // Optional custom rule; runs after the attribute rules pass.
    static partial void Validate(string value, ValidationErrors errors)
    {
        if (!Luhn.IsValid(value))
        {
            errors.Add(new ChecksumError());
        }
    }
}
```

Generated per type: the `Value` accessor (with a runtime guard against `default` instances
for reference raws), a private constructor, non-throwing `TryCreate` returning
`ValidationResult<TSelf>`, throwing `Create`, `IParsable`/`ISpanParsable`,
`IComparable` and operators when `Comparable = true`, an explicit conversion to the raw
type, a JSON converter, a `TypeConverter`, a reflection-free `DomainTypeInfo` descriptor,
and (when the project references the ASP.NET package) `BindAsync` for minimal API
binding.

Built-in rules: `Pattern`, `Length`, `Range` (integer, floating-point, or
invariant-string bounds parsed with the raw type — for example
`[Range("2000-01-01", "2099-12-31")]` on a `DateOnly`), `NotEmpty`, and `Normalize`.

`TryCreate` is the primitive and never throws, so callers can accumulate every failure;
`Create` is sugar that throws one `DomainValidationException` carrying all of them. Input
is nullable — "missing" is an ordinary `required` error, which is what lets a DTO with
nullable fields feed the domain type directly.

## Domain models

A domain model composes field values, other models, and collections of them:

```csharp
[DomainModel]
public sealed partial record BillingDocument
{
    public required InvoiceNumber InvoiceNumber { get; init; }
    public required InvoiceDate InvoiceDate { get; init; }
    public required AccountNumber AccountNumber { get; init; }
    public Memo? Memo { get; init; }                                 // optional
    public required IReadOnlyList<LineItem> LineItems { get; init; } // nested collection
}
```

The generator emits a nested `Dto` record — the raw, serialization-friendly shape whose
properties are all nullable — plus `TryCreate(Dto)`, `Create`, `ToDto()`, and a JSON
converter routed through the DTO. Nullability drives required-ness. Validation collects
*all* errors, keyed by the JSON path of the input shape (`invoiceNumber`,
`lineItems[2].quantity`), before any cross-field rule runs.

A model with a cross-field rule gets a private constructor, so it can only be built
through the factories, plus typed `TryCreate`/`Create` factories over its fields:

```csharp
public sealed record DateRangeError(DateOnly Begin, DateOnly End) : IDomainError;

[DomainModel]
[Reports<DateRangeError>]
public sealed partial record BillingDocumentSearch
{
    public required InvoiceDate BeginDate { get; init; }
    public required InvoiceDate EndDate { get; init; }
    public required AccountNumber AccountNumber { get; init; }

    static partial void Validate(BillingDocumentSearch candidate, ValidationErrors errors)
    {
        if (candidate.EndDate < candidate.BeginDate)
        {
            errors.Add(new DateRangeError(candidate.BeginDate.Value, candidate.EndDate.Value));
        }
    }
}
```

Non-domain-typed properties are copied between model and DTO unvalidated; analyzer
TW0004 warns unless the property is explicitly marked `[Raw]`.

## Typed domain errors

Every failure is a type, not a string. An error is one sealed record implementing
`IDomainError`, declared next to whatever produces it and carrying the structured facts
of the failure (`LengthError(Min, Max, Actual)`), never prose. Wire codes derive from
the type name (`DateRangeError` → `date_range`), and the invariant `DefaultMessage` is
only the presentation fallback.

Error *types* are open, but each domain type's error *set* is finite: rule attributes
imply their built-in errors, hooks declare theirs with `[Reports<TError>]`, and models
derive the union over their fields. The generator emits the set twice:

- `DomainTypeInfo.ErrorTypes` — the runtime enumeration, so tooling can verify every
  possible error is mapped to a response.
- A generated `MatchError` with one handler per possible error — compiler-enforced
  exhaustiveness. Add `[Reports<NewError>]` and every exhaustive call site fails to
  compile until the new case is handled, without any central union type:

```csharp
var status = AccountNumber.MatchError(failure.Error,
    checksum: _ => 422,
    length:   _ => 400,
    required: _ => 400);
```

Ordinary handling stays strongly typed too — pattern matching and `when` guards work,
restoring what per-error exception types offered without giving up accumulation:

```csharp
catch (DomainValidationException e) when (e.Errors.Contains<ChecksumError>()) { ... }
```

## Fluent definitions (no generator required)

Attributes plus the source generator are one *front end* over the runtime — not the only
way in. `Typewright.Validation.Fluent` defines the same aspects by hand, against the
same primitives, error types, descriptors, binding, JSON, and OpenAPI support:

```csharp
public readonly record struct AccountNumber : IDomainValue<AccountNumber, string>
{
    private static readonly DomainValueDefinition<AccountNumber, string> Definition =
        DomainValue.Define<AccountNumber, string>(static value => new(value))
            .Normalize(trim: true, casing: Casing.Upper)
            .Length(1, 64)
            .Must(Luhn.IsValid, static _ => new ChecksumError())
            .Build();

    private readonly string _value;
    private AccountNumber(string value) => _value = value;
    public string Value => _value ?? throw new InvalidOperationException("...");
    public static ValidationResult<AccountNumber> TryCreate(string? value) => Definition.TryCreate(value);
    public static AccountNumber Create(string? value) => Definition.Create(value);
    public static DomainTypeInfo DomainTypeInfo => Definition.TypeInfo;
}
```

Models compose fields through typed handles, so construction stays compiler-checked at
any arity, with the same JSON paths and error accumulation as generated models:

```csharp
var builder = DomainModel.Define<BillingDocument, BillingDocument.Dto>();
var number = builder.Field(static dto => dto.InvoiceNumber, InvoiceNumber.TryCreate);
var memo   = builder.OptionalValue(static dto => dto.Memo, Memo.TryCreate);
var items  = builder.Collection(static dto => dto.LineItems, LineItem.TryCreate);
var definition = builder
    .Must(static d => ..., static d => new SomeCrossFieldError(...))
    .Construct(fields => new BillingDocument
    {
        InvoiceNumber = fields.Get(number),
        Memo = fields.Get(memo),
        LineItems = fields.Get(items),
    });
```

The guarantees carry over, enforced by different means:

- String-only rules (`Pattern`, `Length`, `NotEmpty`, `Normalize`) are extension methods
  on `Builder<TSelf, string>` — the compiler does what TW1002 does for attributes.
- `Must<TError>(predicate, errorFactory)` declares the error in the set *by
  construction*; free-form `Validate(...)` hooks pair with `Reports<TError>()`, and an
  undeclared report throws at validation time — the runtime equivalent of TW0005.
- `Definition.TypeInfo` yields the same `DomainTypeInfo` (rules, fields, `ErrorTypes`
  derived through `DomainTypeRegistry`), so OpenAPI transformers, MVC binders, and
  problem details work unchanged.

What manual types don't get: the compile-time `MatchError` matcher (that is codegen by
nature — `ErrorTypes` remains the runtime enumeration) and the TW0001/TW0002
construction-hole analyzers, which key off the attributes.

`samples/Billing.Fluent` is the whole Billing domain and API defined this way — no
generator reference at all — and `FluentParityTests` asserts both front ends produce
identical failures, wire output, and descriptors.

## Analyzers

The analyzers ship in the generator package, so the default install closes the holes the
type system cannot:

| Rule | Severity | Meaning |
|---|---|---|
| TW0001 | error | `new X()` or `default` of a `[DomainValue]` type bypasses validation |
| TW0002 | error | `with` on a `[DomainModel]` with a cross-field rule bypasses it |
| TW0003 | error | a `[DomainValue]`/`[DomainModel]` type is not `partial` |
| TW0004 | warning | a model property is not domain-typed (silence with `[Raw]`) |
| TW0005 | error | a `Validate` hook reports an error type not in the type's error set |
| TW0006 | warning | `[Reports<TError>]` on a type with no `Validate` hook |

## ASP.NET Core

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi(options => options.AddTypewright());     // OpenAPI transformers
builder.Services.AddControllers(options => options.AddTypewright()); // MVC model binders

var app = builder.Build();
app.UseTypewright();   // DomainValidationException -> RFC 9457 response

app.MapPut("/invoices/{id}", (InvoiceNumber id, BillingDocument doc, IBillingService svc)
    => TypedResults.Ok(svc.Upsert(id, doc)));
```

Domain values bind from route/query/header, domain models from the JSON body via their
DTO with every error accumulated. A failed bind throws; `UseTypewright()` (or the
`AddTypewrightExceptionHandler()` alternative) turns it into a 400
`HttpValidationProblemDetails`:

```json
{
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "invoiceNumber": ["The value must match the pattern '^I\\d{9}$'."],
    "lineItems[1].quantity": ["The value must be between 1 and 10000."]
  },
  "errorCodes": {
    "invoiceNumber": ["pattern"],
    "lineItems[1].quantity": ["range"]
  }
}
```

Because binding fails closed, forgetting `UseTypewright()` produces a 500 — never a
handler running with default-valued domain types. Malformed JSON becomes a single
`json_invalid` error at path `$`. Messages are produced only at this boundary: register
an `IDomainErrorRenderer` in DI to control or localize them (it covers both minimal API
problem details and MVC `ModelState`); paths and codes stay stable.

The OpenAPI transformers use the generated `DomainTypeInfo` descriptors: field values are
documented as their raw primitive with `pattern`/`minLength`/`minimum`/`format`, models
as their DTO schema, and every domain-typed operation gains the 400 response. MVC
controllers get the same behavior through model binders that report into `ModelState`, so
the `[ApiController]` automatic 400 has the same shape.

## Agents

An agent processes one message at a time, in order, threading its state through a
handler: F#'s `MailboxProcessor`, with every part replaceable. No actor system is
needed; an agent is just an object you hold a reference to:

```csharp
public abstract record CounterMessage;
public sealed record Increment : CounterMessage;
public sealed record GetCount : CounterMessage, IRequest<int>;   // a request declares its reply

var counter = Agent.Start(0, MessageHandler.For<int, CounterMessage>()
    .On<Increment>(static (count, _) => count + 1)
    .On<GetCount, int>(static (count, _) => (count, count))      // must reply with an int
    .Build());

counter.TryPost(new Increment());
int total = await counter.AskAsync(new GetCount());              // reply type inferred from the request
```

- **Typed request/reply at both ends.** `IRequest<TReply>` fixes the reply type once:
  `AskAsync` infers it, and `On<TRequest, TReply>` is constrained to it, so a handler
  cannot reply with the wrong type.
- **Domain outcomes are values, faults are exceptions.** A handler may return
  `OperationResult.Reject(new InsufficientFundsError(...))`; `TryAskAsync` hands the
  caller a typed `OperationResult<TReply>`, and `AskAsync` throws a `DomainErrorException`
  carrying it. A rejection is not a failure: the agent carries on.
- **State is readable without the mailbox.** `agent.CurrentState` is the last committed
  state, never a partially applied one (state commits only when handling succeeds).
- **Cancellation.** Cancelling an ask stops the wait immediately. A request still queued
  is skipped (counted on a metric; `SkipCancelledRequests = false` opts out), and
  handlers that take a `CancellationToken` also observe the caller's cancellation.
- **Lifecycle.** Messages posted before `Start` are buffered; `StopAsync` drains, then
  stops; `AbortAsync` cancels in-flight work. Every termination fails the requests still
  queued, so no caller waits forever. Optional `IAgentLifecycle<TState>` hooks run at
  start (e.g. to load state), on restart, and at stop.
- **Failure policies** decide `Resume` (default), `Restart`, `Stop`, or `Escalate`, with
  sliding-window context for limits such as "restart at most 3 times a minute".
- **Replaceable parts.** Mailboxes: unbounded, bounded (wait, reject, drop oldest or
  newest), or priority. Dispatchers: thread pool, any `TaskScheduler` (such as an
  exclusive scheduler shared by several agents), or a dedicated thread. Interceptors wrap
  every message like ASP.NET middleware; built-ins add tracing, metrics, and timeouts.

A fire-and-forget post costs about 50 ns and allocates nothing; an ask round trip takes
about 0.8 microseconds and allocates 215 bytes.

## Actors

Actors are agents with an address, a system that owns their lifecycle, and a supervisor.
Addresses are `ActorPath` domain values such as `/accounts/AC000042`, and parent/child
is path containment:

```csharp
builder.Services.AddActorSystem();   // drains every actor when the host stops

var account = actors.GetOrSpawn(ActorPath.Create($"/accounts/{id}"), Accounts.Definition);
OperationResult<AccountSnapshot> result = await account.TryAskAsync(new Deposit(amount));

// The CQRS read path: the last committed state, without queueing behind writes.
if (account.TryGetState<AccountState>(out var state)) { ... }
```

- `GetOrSpawn` creates exactly one actor per path, however many callers race for it: the
  per-key pattern where every command for a resource is serialized while different
  resources proceed in parallel.
- References stamp every send with a correlation id (carried over from the message being
  handled), the sending actor's path, a timestamp, and W3C trace context.
- Supervision is one-for-one: when a child's failure policy escalates, its parent's
  `ChildSupervision` (or the system default) decides to resume it from its last committed
  state, restart it from its definition, stop its subtree, or escalate further.
  References stay valid across recreation.
- Handlers can come from dependency injection (`ActorDefinition.FromServices<...>`).

Addressing adds about 0.2 microseconds and 257 bytes to an ask, for the stamped headers.
`samples/Ledger.Actors` is a bank ledger built this way: one actor per account under a
supervising registry, endpoints written in domain types, and typed rejections mapped to
404 and 409 responses.

## Repository layout

```
src/Typewright/                        solution root (Typewright.slnx, shared build props)
  Typewright.Validation/               contracts, errors, results, rules, JSON support
  Typewright.Validation.Generators/    incremental generator + analyzers (netstandard2.0)
  Typewright.Validation.AspNetCore/    binding, middleware, ProblemDetails, OpenAPI, MVC
  Typewright.Agents/                   mailbox processor, router, pipeline, policies
  Typewright.Actors/                   addressing, actor system, supervision, hosting
  Typewright.Lattice/                  (later plan)
  tests/                               xUnit v3 (dotnet test runs in MTP mode)
  samples/Billing.Api/                 the invoice example as a minimal API + MVC app
  samples/Billing.Fluent/              the same domain defined without the generator
  samples/Ledger.Actors/               a bank ledger: one actor per account
  benchmarks/                          BenchmarkDotNet: validation, agents, actors
docs/plans/                            design plans (01 validation, 02 errors, 03 agents and actors)
```

Build and test, from `src/Typewright`:

```
dotnet build Typewright.slnx
dotnet test --solution Typewright.slnx
dotnet run -c Release --project benchmarks/Typewright.Validation.Benchmarks -- --filter *
dotnet run -c Release --project benchmarks/Typewright.Agents.Benchmarks -- --filter *
```

The DTO-to-domain transform for a fully valid 20-field document runs in roughly 160 ns
(about 1.5 KB allocated); the accumulate-everything failure case with eight invalid
fields runs in roughly 330 ns.
