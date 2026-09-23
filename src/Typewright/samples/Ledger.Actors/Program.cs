using System.Collections.Immutable;
using Ledger.Actors.Domain;
using Typewright.Actors;
using Typewright.Validation;
using Typewright.Validation.AspNetCore;

// A bank ledger as an actor system: one actor per account, a registry actor supervising
// them, HTTP endpoints written in domain types, and typed rejections mapped to responses.

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddActorSystem();

var app = builder.Build();
app.UseTypewright();

var actors = app.Services.GetRequiredService<IActorSystem>();
actors.Spawn(Registry.Path, Registry.Definition);

// Opening is the only operation that creates an account actor.
app.MapPut("/accounts/{id}", async (AccountId id, OpenAccountRequest request, IActorSystem system, CancellationToken cancellationToken) =>
{
    var account = system.GetOrSpawn(Accounts.PathOf(id), Accounts.Definition);
    var result = await account.TryAskAsync(new OpenAccount(request.Holder), cancellationToken);
    return result.Match<IResult>(
        snapshot => TypedResults.Created($"/accounts/{id}", snapshot),
        LedgerProblems.For);
});

app.MapPost("/accounts/{id}/deposits", (AccountId id, MoneyRequest request, IActorSystem system, CancellationToken cancellationToken)
    => AskAccountAsync(system, id, new Deposit(request.Amount), cancellationToken));

app.MapPost("/accounts/{id}/withdrawals", (AccountId id, MoneyRequest request, IActorSystem system, CancellationToken cancellationToken)
    => AskAccountAsync(system, id, new Withdraw(request.Amount), cancellationToken));

// Reads take the actor's last committed state without going through its mailbox.
app.MapGet("/accounts/{id}", (AccountId id, IActorSystem system)
    => system.Resolve<AccountMessage>(Accounts.PathOf(id)) is { } account
        && account.TryGetState<AccountState>(out var state)
        && state.IsOpen
            ? TypedResults.Ok(state.ToSnapshot())
            : LedgerProblems.For(new AccountNotFoundError(id)));

app.MapGet("/accounts", (IActorSystem system)
    => system.Resolve<RegistryMessage>(Registry.Path) is { } registry
        && registry.TryGetState<ImmutableSortedSet<string>>(out var ids)
            ? Results.Ok(ids)
            : Results.Ok(Array.Empty<string>()));

app.Run();

static async Task<IResult> AskAccountAsync(
    IActorSystem system, AccountId id, AccountMessage command, CancellationToken cancellationToken)
{
    if (system.Resolve<AccountMessage>(Accounts.PathOf(id)) is not { } account)
    {
        return LedgerProblems.For(new AccountNotFoundError(id));
    }

    var result = command switch
    {
        Deposit deposit => await account.TryAskAsync(deposit, cancellationToken),
        Withdraw withdraw => await account.TryAskAsync(withdraw, cancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(command)),
    };

    return result.Match<IResult>(snapshot => TypedResults.Ok(snapshot), LedgerProblems.For);
}

/// <summary>
/// Maps the ledger's domain errors to responses. Hand-written for now; plan 02 §8 is
/// where this becomes a generated, compiler-checked exhaustive mapping.
/// </summary>
internal static class LedgerProblems
{
    public static IResult For(IDomainError error) => error switch
    {
        AccountNotFoundError => Problem(StatusCodes.Status404NotFound, error),
        AccountAlreadyOpenError or InsufficientFundsError => Problem(StatusCodes.Status409Conflict, error),
        _ => Problem(StatusCodes.Status500InternalServerError, error),
    };

    private static IResult Problem(int status, IDomainError error) => TypedResults.Problem(
        title: error.DefaultMessage,
        statusCode: status,
        extensions: new Dictionary<string, object?> { ["code"] = error.Code });
}

/// <summary>Exposed for WebApplicationFactory-based end-to-end tests.</summary>
public partial class Program;
