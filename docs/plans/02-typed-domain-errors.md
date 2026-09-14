# Plan 02: Typed Domain Errors

Status: draft (2026-09-14)
Scope: reworks the error model of `Typewright.Validation`, `Typewright.Validation.Generators`, `Typewright.Validation.AspNetCore`
Supersedes: the string `Code`/`Message` pair on `ValidationError` from plan 01

## 1. Motivation

Plan 01 represented a failure as `(Path, Code, Message)` — two unconstrained strings.
That contradicts the Typewright philosophy in three ways:

- **The set of possible errors is not finite or discoverable.** Nothing enumerates what
  can go wrong with an `AccountNumber`, so nothing can verify that every possible error
  is mapped to a specific failure response.
- **Handling is stringly typed.** `error.Code == "checksum"` cannot be checked by the
  compiler, cannot be pattern-matched, and silently breaks when a code changes.
- **Domain types produce user-facing prose.** A `Validate` hook writing
  `"The account number checksum is invalid."` mixes presentation into the domain. The
  domain should report *what* happened as structured data; the boundary decides how to
  say it.

The original mockup solved this with one exception type per error
(`InvalidInvoiceNumberException`), which gives strong typing but cannot accumulate
several failures. An F# discriminated union gives exhaustiveness but centralizes every
error into one type, making each addition a cross-cutting edit.

The resolution: **error types are open; error *sets* are closed per scope.**

- Every domain error is its own standalone `sealed record` — declared next to whatever
  produces it, added without touching anything central. (Extensible, unlike the DU.)
- Every domain type has a *finite, generated* set of the errors it can produce — implied
  by its rule attributes, declared for its custom hooks, and derived for models as the
  union over their fields. (Discoverable and verifiable, unlike strings.)
- Exhaustive handling is per-scope and **compiler-enforced**: the generator emits a
  `MatchError` method per domain type with one delegate parameter per possible error
  type. When the set grows, every call site fails to compile until the new case is
  handled — the F# guarantee, decentralized.

This also sets up the next step beyond validation: `NotFound`, `Conflict`, and other
domain-logic errors from repositories and services will implement the same contract, so
"every error maps to a defined response" extends to whole operations (section 8).

## 2. The error contract

```csharp
public interface IDomainError
{
    /// <summary>
    /// Stable machine-readable code for wire formats; derived from the type name by
    /// default (strip the "Error" suffix, snake_case: DateRangeError -> "date_range").
    /// Override the property for a custom stable code.
    /// </summary>
    string Code => DomainErrorCode.For(GetType());

    /// <summary>
    /// Invariant default text, used by the default renderer. Override to give a custom
    /// error a better default; leave it to renderers/localizers otherwise.
    /// </summary>
    string DefaultMessage => $"The value is invalid ({Code}).";
}
```

Errors are **data**. They carry the structured facts of the failure, never prose:

```csharp
// Built-ins, produced by the generated rule checks and the binding layer:
public sealed record RequiredError : IDomainError;                                   // "required"
public sealed record FormatError(string Value, string RawType) : IDomainError;       // "format"
public sealed record PatternError(string Pattern) : IDomainError;                    // "pattern"
public sealed record LengthError(int Min, int Max, int Actual) : IDomainError;       // "length"
public sealed record RangeError(string Min, string Max, string Actual) : IDomainError; // "range"
public sealed record NotEmptyError : IDomainError;                                   // "not_empty"
public sealed record JsonInvalidError(string Detail) : IDomainError;                 // "json_invalid"
```

A user error is one line, declared wherever it belongs:

```csharp
public sealed record ChecksumError : IDomainError;
public sealed record DateRangeError(DateOnly Begin, DateOnly End) : IDomainError;
```

The accumulator pairs errors with their JSON path; the message is gone from the domain:

```csharp
public readonly record struct ValidationFailure(string Path, IDomainError Error);

public sealed class ValidationErrors : IReadOnlyList<ValidationFailure>
{
    public void Add(IDomainError error);                       // at the current scope
    public void AddAt(string relativePath, IDomainError error);
    public void Merge(string relativePath, ValidationErrors other);
    public PathScope PushPath(string segment);

    public bool Contains<TError>() where TError : IDomainError;
    public IEnumerable<ValidationFailure> OfType<TError>() where TError : IDomainError;
}
```

`DomainValidationException` still exists — it is what lets one throw carry *all*
failures — but the errors inside it are typed, so handling is exactly the strongly-typed
story the exception-per-error mockup wanted, without giving up accumulation:

```csharp
catch (DomainValidationException e) when (e.Errors.Contains<ChecksumError>()) { ... }

switch (failure.Error)
{
    case RangeError { Actual: var actual }: ...
    case ChecksumError: ...
}
```

## 3. Hooks report typed errors

```csharp
[DomainValue<string>]
[Length(1, 64), Normalize(Trim = true, Case = Casing.Upper)]
[Reports<ChecksumError>]
public readonly partial record struct AccountNumber
{
    static partial void Validate(string value, ValidationErrors errors)
    {
        if (!Luhn.IsValid(value))
        {
            errors.Add(new ChecksumError());
        }
    }
}

[DomainModel]
[Reports<DateRangeError>]
public sealed partial record BillingDocumentSearch
{
    ...
    static partial void Validate(BillingDocumentSearch candidate, ValidationErrors errors)
    {
        if (candidate.EndDate < candidate.BeginDate)
        {
            errors.Add(new DateRangeError(candidate.BeginDate.Value, candidate.EndDate.Value));
        }
    }
}
```

`[Reports<TError>]` (AllowMultiple) declares what a custom hook may add. Rule attributes
imply their built-in error types automatically, so `[Reports]` is only ever needed for
hook-produced errors. The declaration is what makes the set *finite by construction*
rather than by inspection; an analyzer keeps it honest (section 5).

## 4. The generated, per-scope error set

For every domain type the generator derives the complete set of possible errors:

- **Field value**: `RequiredError` (nullable input), `FormatError` (parse from string at
  the boundary), the errors implied by its rule attributes, plus its `[Reports]`.
- **Domain model**: `RequiredError` and `JsonInvalidError` (body binding), the union of
  every property's set (collections contribute their element type's set), plus its own
  `[Reports]`.

The set is emitted twice:

- **Runtime**: `DomainTypeInfo.ErrorTypes : IReadOnlyList<Type>` — what makes "is every
  possible error mapped?" *verifiable* by tooling, tests, and the future response-mapping
  layer.
- **Compile time**: a generated exhaustive matcher, one delegate per error type in the
  set, deterministic (alphabetical) order:

```csharp
// generated on AccountNumber:
public static TResult MatchError<TResult>(
    IDomainError error,
    Func<ChecksumError, TResult> checksum,
    Func<LengthError, TResult> length,
    Func<RequiredError, TResult> required) => ...;
```

Adding `[Reports<NewError>]` (or a new rule attribute) adds a parameter, and **the
compiler** breaks every call site that chose exhaustive handling, naming exactly what is
unhandled. That is the F# exhaustiveness guarantee with none of the central-DU coupling:
the new error is declared on one type; the compiler finds the affected consumers.

Callers that do not need exhaustiveness keep ordinary pattern matching over
`IDomainError`.

## 5. Analyzer support

- **TW0005 (error)**: a `Validate` hook passes an error to `ValidationErrors.Add`/`AddAt`
  whose static type is not in the domain type's declared set. (Checked on the static type
  of the argument, so helper methods that return a typed error are fine; an argument
  typed only as `IDomainError` is flagged as undeclarable.)
- **TW0006 (warning)**: `[Reports<TError>]` on a type with no `Validate` hook — a
  declaration that nothing can produce.

The compiler enforces exhaustive *handling* (via `MatchError`); the analyzer enforces an
honest *declaration*. Together the set is closed in both directions.

## 6. Messages move to the boundary

`IValidationMessageLocalizer` (a message *transformer*) is replaced by a renderer that
is the single place presentation text is produced:

```csharp
public interface IDomainErrorRenderer
{
    string Render(ValidationFailure failure);
}
```

The default renderer produces today's invariant messages for the built-ins (so the wire
output of plan 01 is preserved) and falls back to `IDomainError.DefaultMessage` for
custom errors. Localization = registering a renderer; it now covers both the
problem-details path and the MVC `ModelState` path, closing the gap noted in plan 01.

The RFC 9457 response shape is unchanged: `errors` carries rendered messages,
`errorCodes` carries `failure.Error.Code`. Codes remain on the wire because HTTP clients
need stable strings — but they are now a *projection of the types*, derived by one
convention, not hand-maintained literals. (Two codes change: `json.invalid` becomes
`json_invalid`, and the sample's `date_range.invalid` becomes `date_range`.)

## 7. What changes, concretely

| Area | Change |
|---|---|
| `ValidationError(Path, Code, Message)` | becomes `ValidationFailure(Path, IDomainError)` |
| `ValidationErrors.Add(code, message)` | becomes `Add(IDomainError)` / `AddAt(path, IDomainError)` |
| `ValidationErrorCodes` constants | deleted; codes derive from error types |
| Generated rule checks | construct typed errors carrying the rule facts (`new LengthError(1, 64, value.Length)`) |
| Generated members | `DomainTypeInfo.ErrorTypes`, `MatchError`, unchanged otherwise |
| `[Reports<TError>]` attribute | new, in `Typewright.Validation` |
| Analyzers | TW0005/TW0006 added |
| `IValidationMessageLocalizer` | deleted; `IDomainErrorRenderer` + default implementation |
| Problem details / MVC binders | render through `IDomainErrorRenderer` |
| Sample hooks | report `ChecksumError` / `DateRangeError` |
| OpenAPI | untouched (schemas derive from rule attributes, not errors) |

Phases, each ending green (the core model, generator output, and boundary compile
against each other, so they land together):

- **A. Error model end to end**: `IDomainError`, built-ins, code derivation,
  `ValidationFailure`, reworked `ValidationErrors`, exception helpers, renderer;
  generators emit typed rule errors, parse `[Reports]`, and generate
  `ErrorTypes`/`MatchError`; problem details and MVC render through the renderer;
  sample, tests, and snapshots updated.
- **B. Analyzers**: TW0005/TW0006.
- **C. Docs**: README, plan notes.

## 8. Forward: beyond validation errors

`IDomainError` is deliberately not validation-specific. The service/repository layer
plan builds on it:

- Operation errors (`InvoiceNotFoundError`, `DuplicateInvoiceError`, ...) implement
  `IDomainError` and are declared per operation with the same `[Reports]` mechanism.
- Operations return a result type (or throw a typed carrier) whose error set is finite
  and generated, exactly like `TryCreate` today.
- The HTTP layer maps error types to responses (404, 409, ...) through a generated
  exhaustive mapper per operation — so "is every domain error mapped to a specific
  failure response?" is a compile error when the answer is no, which is the end goal
  this plan exists to enable.

## 9. Decisions to confirm

| # | Decision | Recommendation |
|---|----------|----------------|
| D1 | Error identity | One sealed record per error, `IDomainError` contract; codes derived from type names, overridable. |
| D2 | Finite set | Implied by rules + explicit `[Reports<TError>]` for hooks; analyzer-enforced; derived for models. |
| D3 | Exhaustiveness | Generated per-type `MatchError` (compiler-enforced); plain pattern matching stays available. |
| D4 | Messages | None in the domain; `IDomainErrorRenderer` at the boundary with `DefaultMessage` as the per-type default hook. |
| D5 | Exceptions | Keep the single `DomainValidationException` carrier (accumulation requires it); typed errors restore `when`-guard ergonomics. |
| D6 | Wire codes | Kept, as a derived projection of the types; response shape unchanged. |
