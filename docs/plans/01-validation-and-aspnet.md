# Plan 01: Model Validation Framework + ASP.NET Integration

Status: implemented through phase 5 (2026-09-14); see "Implementation notes" at the end
Scope: `Typewright.Validation`, `Typewright.Validation.Generators`, `Typewright.Validation.AspNetCore`
Builds on: [docs/model-validation.md](../model-validation.md)

## 1. Goal

Make invalid domain data unrepresentable in application code, and make the HTTP
boundary the place where raw input is turned into domain types. An endpoint's
signature is written entirely in domain types; if the request cannot be turned
into those types, the caller gets a 400 with every validation failure, and the
endpoint body never runs.

```csharp
app.MapPut("/invoices/{id}", (InvoiceNumber id, BillingDocument doc, IBilling svc)
    => svc.Upsert(id, doc));
```

Everything below serves that one line.

## 2. Core concepts

### 2.1 Field values (`readonly record struct`)

A field value wraps exactly one raw value (`string`, `int`, `decimal`, `DateOnly`,
`Guid`, ...) and enforces that value's invariants. Contract:

```csharp
public interface IDomainValue<TSelf, TRaw> where TSelf : struct, IDomainValue<TSelf, TRaw>
{
    TRaw Value { get; }
    static abstract ValidationResult<TSelf> TryCreate(TRaw? value);   // never throws
    static abstract TSelf Create(TRaw? value);                        // throws DomainValidationException
}
```

Design changes from the mockup, and why:

- **Non-throwing `TryCreate` is the primitive; `Create` is sugar.** The HTTP
  layer must report *all* failures in a request, not the first one. That is only
  possible if validation returns errors instead of throwing. `Create` calls
  `TryCreate` and throws.
- **One exception type, many error codes.** `DomainValidationException` carries a
  `ValidationErrors` collection. Per-type exceptions like
  `InvalidInvoiceNumberException` multiply boilerplate and cannot represent
  several failures at once. Users who want a specific exception can still catch
  and inspect by code.
- **Nullable input, non-null output.** `TryCreate(string? value)` accepts null so
  "missing" is an ordinary validation error (`required`), not an
  `ArgumentNullException`. This is what lets a DTO with nullable fields feed the
  domain type directly.

### 2.2 Validation errors

```csharp
public readonly record struct ValidationError(string Path, string Code, string Message);
public sealed class ValidationErrors : IReadOnlyList<ValidationError> { ... }   // accumulator with path scoping
public readonly struct ValidationResult<T> { bool IsValid; T Value; ValidationErrors Errors; }
public sealed class DomainValidationException : Exception { ValidationErrors Errors { get; } }
```

- `Path` uses the JSON convention of the input shape: `invoiceNumber`,
  `lineItems[2].quantity`. It is what ends up in the `errors` dictionary of
  `HttpValidationProblemDetails` (RFC 9457), so it must match what the client sent.
- `Code` is stable and machine-readable (`required`, `pattern`, `range`,
  `date_range.invalid`); `Message` is human-readable and may be localized later.
- `ValidationErrors` supports `PushPath("lineItems[2]")` scoping so nested
  models prefix their child errors without knowing where they sit.

### 2.3 Domain models (`record`)

A domain model composes field values (and other domain models, and collections
of them). It has a serialization-friendly **DTO** shape whose properties are the
raw types, all nullable. Contract:

```csharp
public interface IDomainModel<TSelf, TDto> where TSelf : IDomainModel<TSelf, TDto>
{
    static abstract ValidationResult<TSelf> TryCreate(TDto? dto);
    static abstract TSelf Create(TDto? dto);
    TDto ToDto();
}
```

`TryCreate(dto)`:

1. For every property, call the field/model `TryCreate` on the DTO's raw value,
   scoped to the property's JSON path. Collect all errors.
2. If any field failed, return the errors. Do not run cross-field rules on a
   partially valid object.
3. Construct the record privately, run the optional cross-field `Validate` hook,
   and return either the instance or the cross-field errors.

Why DTO-based construction instead of deserializing straight into the domain
record with custom `JsonConverter`s:

- `System.Text.Json` stops at the first failure and throws; there is no way to
  accumulate errors across properties.
- JSON is only one input source. Route values, query strings, headers, forms,
  and (later) Lattice/REST-DB storage all need the same "raw shape to domain"
  transform. The DTO is that raw shape, whatever transport carried it.
- The DTO is also the natural OpenAPI request/response schema.

Constructor rules follow the mockup: a model with no cross-field rules gets a
public constructor (any combination of valid fields is valid); a model with a
cross-field `Validate` hook gets a private constructor so it can only be built
through the factory.

### 2.4 The `default(T)` and `with` holes

Two ways to get an invalid instance without calling a constructor:

- `default(InvoiceNumber)` / `new InvoiceNumber()` yields `Value == null`.
  Mitigation: an analyzer error on `new`/`default` of any `[DomainValue]` type,
  plus a runtime guard in the `Value` getter for reference raw types so a
  slipped-through default fails loudly at first use rather than propagating.
- `search with { EndDate = x }` bypasses cross-field validation. Mitigation: the
  analyzer reports an error on `with` expressions whose target has a cross-field
  `Validate` hook. Models without cross-field rules keep `with`, because their
  fields are individually valid.

The framework is usable without the analyzer, but the analyzer ships in the
same package as the generator so the default install closes both holes.

## 3. Source generator

Hand-writing the pattern is roughly forty lines per field type. The generator is
what makes this a framework rather than a convention. It is an incremental
generator (netstandard2.0, Roslyn 4.x) triggered by attributes; the contracts in
`Typewright.Validation` work with or without it.

### 3.1 Field values

```csharp
[DomainValue<string>]
[Pattern(@"^I\d{9}$")]
public readonly partial record struct InvoiceNumber;

[DomainValue<DateOnly>(Comparable = true)]
public readonly partial record struct InvoiceDate;

[DomainValue<string>]
[Length(1, 64), Normalize(Trim = true, Case = Casing.Upper)]
public readonly partial record struct AccountNumber
{
    // optional custom rule; runs after attribute rules
    static partial void Validate(string value, ValidationErrors errors)
    {
        if (!Luhn.IsValid(value)) errors.Add("checksum", "Account number checksum is invalid.");
    }
}
```

Generated per type:

- `Value` property, private constructor, `TryCreate`, `Create`, `ToString()`.
- `IDomainValue<TSelf, TRaw>` implementation.
- `IParsable<TSelf>` / `ISpanParsable<TSelf>` when `TRaw` is parsable, so the
  type binds from route/query/header in minimal APIs and works with `TryParse`.
- `IComparable<TSelf>` and comparison operators when `Comparable = true` (the
  mockup's `endDate < beginDate`).
- `explicit operator TRaw` (explicit, not implicit, so the domain type does not
  silently decay to a primitive).
- A `JsonConverter<TSelf>` that writes `Value` and reads via `Create`, plus the
  `[JsonConverter]` attribute. Compatible with STJ source generation and AOT.
- A `TypeConverter` for the odd corners of ASP.NET that still use it.
- A static `DomainTypeInfo` descriptor (name, raw type, rules) so OpenAPI
  transformers and, later, Lattice schema validation can introspect without
  reflection.

Built-in rule attributes for the first cut: `Pattern`, `Length`, `Range`,
`NotEmpty`, `Normalize`. Each rule knows how to emit its check and how to
describe itself as OpenAPI schema metadata (`pattern`, `minLength`, `minimum`).

### 3.2 Domain models

```csharp
[DomainModel]
public partial record BillingDocument
{
    public required InvoiceNumber InvoiceNumber { get; init; }
    public required InvoiceDate InvoiceDate { get; init; }
    public required AccountNumber AccountNumber { get; init; }
    public Memo? Memo { get; init; }                                   // optional field
    public required IReadOnlyList<LineItem> LineItems { get; init; }   // nested model collection
}
```

Generated:

- Nested `BillingDocument.Dto` sealed record: `string? InvoiceNumber`,
  `DateOnly? InvoiceDate`, `string? AccountNumber`, `string? Memo`,
  `List<LineItem.Dto>? LineItems`. Property names and `[JsonPropertyName]`
  attributes are carried over so paths line up.
- `TryCreate(Dto)`, `Create(Dto)`, `ToDto()`, `IDomainModel<TSelf, Dto>`.
- A `JsonConverter<BillingDocument>` routed through the DTO, so a domain record
  can still be (de)serialized directly when the data is trusted (Lattice log
  replay, tests).
- Private parameterless constructor when a
  `static partial void Validate(BillingDocument candidate, ValidationErrors errors)`
  hook is declared; public otherwise.
- `DomainTypeInfo` descriptor listing fields and their types.

Nullability drives required-ness: a non-nullable property is required (null raw
value yields a `required` error); a nullable property maps null through as null.

### 3.3 Analyzers (same package)

- TW0001 error: `new`/`default` of a `[DomainValue]` type.
- TW0002 error: `with` on a `[DomainModel]` that has cross-field validation.
- TW0003 error: `[DomainValue]`/`[DomainModel]` type is not `partial`.
- TW0004 warning: property on a `[DomainModel]` whose type is not a domain value,
  domain model, or collection thereof (escape hatch: `[Raw]` attribute).

## 4. ASP.NET integration

### 4.1 Minimal APIs (first)

Binding hooks that minimal APIs already honor, with no per-endpoint ceremony:

- **Body parameters** (`BillingDocument doc`): the generator emits
  `IBindableFromHttpContext<BillingDocument>`. `BindAsync` reads the body as
  `BillingDocument.Dto` with the app's `JsonSerializerOptions`, calls
  `TryCreate`, and throws `DomainValidationException` with the full error set
  if it fails. Malformed JSON becomes a single error at path `$` with code
  `json.invalid` rather than a bare 400.
- **Route/query/header parameters** (`InvoiceNumber id`): the generator emits
  `IBindableFromHttpContext<InvoiceNumber>` too, not just `IParsable`. Reason:
  minimal APIs turn a `TryParse` failure into a generic "failed to bind
  parameter" 400 that loses the domain message. `BindAsync` receives the
  `ParameterInfo`, resolves the raw string the same way the framework does
  (`[FromRoute]`/`[FromQuery]`/`[FromHeader]` attribute, else route value by
  name, else query by name), and throws `DomainValidationException` scoped to
  the parameter name on failure. Missing optional parameters (`InvoiceNumber?`)
  bind as null.
- **Turning the exception into a response**: `app.UseTypewright()` middleware
  catches `DomainValidationException` and writes `HttpValidationProblemDetails`
  (status 400, `errors: { "invoiceNumber": ["..."] }`, plus an extension
  member carrying the error codes). It composes with `AddProblemDetails()` when
  present. An `IExceptionHandler` registration is offered as an alternative for
  apps that already use `UseExceptionHandler`.

Why throw-and-catch rather than a `Validated<T>` wrapper or a filter that
returns `default`:

- The endpoint signature stays in pure domain types, which is the whole point.
- It fails closed. If someone forgets `UseTypewright()`, invalid input produces
  a 500, never a handler running with default-valued domain types.
- Cost is one exception per rejected request, which is fine.

Known limitation: minimal APIs bind parameters in order and stop at the first
exception, so errors are complete *within* a body but not *across* multiple
invalid parameters. The body carries nearly all fields in practice. If this
ever matters, an opt-in endpoint filter on a route group can do the binding
itself and accumulate; keep it out of the first cut.

### 4.2 OpenAPI

Parameters bound via `IBindableFromHttpContext` get no schema by default.
`Typewright.Validation.AspNetCore` registers an `IOpenApiOperationTransformer`
and `IOpenApiSchemaTransformer` for `Microsoft.AspNetCore.OpenApi` that use the
generated `DomainTypeInfo`:

- Field values describe as their raw primitive with `pattern`, `minLength`,
  `maxLength`, `minimum`, `maximum`, `format` from the rule attributes.
- Domain models describe as their DTO schema.
- Every endpoint with domain-typed parameters gains a 400
  `HttpValidationProblemDetails` response.

This is also what makes the eventual auto-CRUD REST layer self-describing.

### 4.3 MVC controllers (second)

- `IModelBinderProvider` for domain values (route/query/header/form) and a
  provider for domain models that reads the body as the DTO. Both add errors to
  `ModelState` instead of throwing, so the `[ApiController]` automatic 400
  behavior produces the same `HttpValidationProblemDetails` shape.
- Same OpenAPI transformers apply.

### 4.4 Relationship to built-in .NET 10 validation

.NET 10 minimal APIs ship `AddValidation()` (DataAnnotations via
`Microsoft.Extensions.Validation`), which runs as an endpoint filter *after*
binding. Typewright validates *during* binding because its types cannot exist
in an invalid state, so the two are not layered. Endpoints can still use both
for parameters that are not domain types.

## 5. Repository layout

```
Typewright.slnx
Directory.Build.props            # net10.0, nullable, warnings-as-errors, LangVersion latest
Directory.Packages.props         # central package management
.editorconfig
.gitignore                       # bin/ obj/ .vs/ (currently missing; .vs/ and obj/ are on disk)
src/
  Typewright.Validation/                     # contracts, errors, results, rule primitives, base JSON support
  Typewright.Validation.Generators/          # incremental generator + analyzers (netstandard2.0)
  Typewright.Validation.AspNetCore/          # binding, middleware, ProblemDetails, OpenAPI, MVC binders
  Typewright.Actor/                          # (later plan)
  Typewright.Lattice/                        # (later plan)
tests/
  Typewright.Validation.Tests/               # xUnit v3
  Typewright.Validation.Generators.Tests/    # Roslyn test host + Verify snapshot tests
  Typewright.Validation.AspNetCore.Tests/    # WebApplicationFactory end-to-end
samples/
  Billing.Api/                               # the invoice example as a running minimal API
```

Packaging: `Typewright.Validation` references nothing beyond the BCL.
`Typewright.Validation.Generators` ships as an analyzer package that depends on
`Typewright.Validation`. `Typewright.Validation.AspNetCore` uses the shared
framework reference and `Microsoft.AspNetCore.OpenApi`.

## 6. Phases

Each phase ends green, committed, and demonstrable.

**Phase 0: Walking skeleton (hand-written, no generator).**
Core types (`ValidationError`, `ValidationErrors`, `ValidationResult<T>`,
`DomainValidationException`, the two interfaces). Hand-written `InvoiceNumber`,
`InvoiceDate`, `AccountNumber`, `BillingDocument`, `BillingDocumentSearch`
exactly as the generator will later emit them. `Billing.Api` sample with one
`PUT /invoices/{id}` and one `GET /invoices?begin=&end=&account=`, the
`IBindableFromHttpContext` implementations, and `UseTypewright()`. End-to-end
tests asserting the 400 body shape. This proves the whole architecture before
any generator code exists and gives the generator a reference target.

**Phase 1: Field value generator.**
`[DomainValue<T>]`, rule attributes, custom `Validate` hook, all generated
members from 3.1. Snapshot tests. Replace the hand-written field types in the
sample.

**Phase 2: Domain model generator.**
`[DomainModel]`, DTO generation, nested models and collections, path scoping,
cross-field hook, constructor rules. Replace hand-written models in the sample.

**Phase 3: Analyzers.**
TW0001 through TW0004, with code fixes where cheap.

**Phase 4: ASP.NET polish.**
Generated `IBindableFromHttpContext` (moves out of hand-written sample code),
`ProblemDetails` integration options, OpenAPI transformers, error code
extension member. MVC model binders.

**Phase 5: Hardening.**
STJ source-gen and AOT sample, localization hook for messages, benchmarks
(bind + validate a 20-field document well under a microsecond excluding JSON
parsing), XML docs, README for the validation packages.

## 7. Decisions to confirm

Recommendations are already applied above; flag any you disagree with.

| # | Decision | Recommendation |
|---|----------|----------------|
| D1 | Error primitive | Non-throwing `TryCreate` returning `ValidationResult<T>`; `Create` throws one `DomainValidationException`. |
| D2 | Boilerplate | Source generator + analyzers; contracts usable by hand. |
| D3 | Complex type input | Generated nested `Dto` record; all errors accumulated; JSON converters route through it. |
| D4 | HTTP binding | Generated `IBindableFromHttpContext` for both values and models; exception caught by `UseTypewright()` middleware. |
| D5 | Rule authoring | Declarative attributes for common rules plus a `static partial void Validate` hook. |
| D6 | `default`/`with` holes | Analyzer errors plus runtime guard on `Value`. |
| D7 | Test stack | xUnit v3, Verify for generator snapshots, `Microsoft.AspNetCore.Mvc.Testing`. |
| D8 | Naming | Nested `Model.Dto` for the raw shape; `TryCreate`/`Create`/`ToDto` as the verbs. |

## 8. Forward hooks for later components

- `DomainTypeInfo` descriptors are the schema Lattice will validate against at
  the key level, and what the REST document layer will use to map a resource
  URI segment to an id type and a body to a model.
- `IParsable` on field values is what turns `/invoices/{id}` into an
  `InvoiceNumber` before routing to the actor for that key.
- DTO-routed JSON converters are the initial wire and log format for the
  append-only stream; a binary format can replace them without touching domain
  types.

## 9. Implementation notes (2026-09-14)

Phases 0-5 are implemented and green (77 tests). Deviations from the text
above, discovered during implementation:

- **Field value structs cannot implement `IBindableFromHttpContext<TSelf>`** —
  the interface has a `class` constraint. Minimal APIs honor the equivalent
  pattern-based static `BindAsync`, so the generator emits that for structs and
  the interface plus `BindAsync` for models. Behavior is as designed.
- **`Range` bounds** come in three attribute forms: `(int, int)`,
  `(double, double)`, and `(string, string)` parsed with the raw type's
  invariant `Parse` (for `DateOnly`, `decimal`, and friends, whose values
  cannot be attribute constants).
- **TW0004 vs. generation**: because TW0004 is a warning, the model generator
  copies non-domain properties between model and DTO unvalidated (passthrough)
  rather than erroring; `[Raw]` on the property silences the warning. The
  interim generator error TW1005 was retired. Generator-side shape errors are
  TW1001-TW1004; the missing-`partial` case is reported only by analyzer
  TW0003.
- **Code fixes were not shipped**: a fix provider requires a separate
  Workspaces-referencing assembly (RS1038 forbids Workspaces in a generator
  assembly), which is not the cheap add hoped for. Future work.
- **Snapshot tests use a small built-in helper**
  (`tests/.../SnapshotAssert.cs`, committed `Snapshots/*.verified.cs`,
  `TYPEWRIGHT_UPDATE_SNAPSHOTS=1` to regenerate) instead of Verify: Verify 33
  now requires a SponsorCheck license declaration in the build, which is a
  licensing statement for the repo owner to make, not tooling.
- **Cross-field hook models also get typed `TryCreate`/`Create` factories**
  over their domain-typed properties — with a private constructor they are the
  only way to build such a model from already-valid fields (the mockup's
  `Create(beginDate, endDate, accountNumber)`).
- **`UseTypewright()` additionally maps `BadHttpRequestException`** to its
  status code (used for 415 on non-JSON bodies), so binder-originated statuses
  are deterministic across environments.
- **Localization** (`IValidationMessageLocalizer`) applies at the
  problem-details boundary; MVC `ModelState` messages are not yet routed
  through it. *(Superseded by plan 02: `IDomainErrorRenderer` replaced the
  localizer and covers both paths.)*
- **Known gaps**: a native AOT publish has not been exercised (STJ source-gen
  compatibility is tested); optional nullable-struct parameters
  (`InvoiceNumber?`) bind as null by design but have no end-to-end test; the
  `dotnet test` MTP opt-in lives in `global.json` (required by the .NET 10
  SDK).
