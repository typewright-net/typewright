using System.Collections.Immutable;
using Typewright.Actors;
using Typewright.Agents;
using Typewright.Validation;

namespace Ledger.Actors.Domain;

/// <summary>One account's state. Immutable, so it is safe to read without the mailbox.</summary>
public sealed record AccountState(AccountId Id, AccountHolder? Holder, decimal Balance)
{
    public bool IsOpen => Holder is not null;

    public AccountSnapshot ToSnapshot() => new(Id.Value, Holder?.Value ?? "", Balance);
}

/// <summary>The wire shape of an account.</summary>
public sealed record AccountSnapshot(string Id, string Holder, decimal Balance);

public abstract record AccountMessage;

public sealed record OpenAccount(AccountHolder Holder) : AccountMessage, IRequest<AccountSnapshot>;

public sealed record Deposit(Amount Amount) : AccountMessage, IRequest<AccountSnapshot>;

public sealed record Withdraw(Amount Amount) : AccountMessage, IRequest<AccountSnapshot>;

/// <summary>
/// One actor per account at <c>/accounts/{id}</c>: every command for an account is
/// serialized through its mailbox, while different accounts proceed in parallel.
/// Domain outcomes are typed rejections; an unexpected fault escalates to the registry.
/// </summary>
public static class Accounts
{
    public static ActorPath PathOf(AccountId id) => Registry.Path.Child(id.Value);

    public static ActorDefinition<AccountState, AccountMessage> Definition { get; } = new()
    {
        InitialState = static context => new AccountState(AccountId.Create(context.Path.Name), null, 0m),
        Handler = static context => CreateHandler(context),
        Options = new AgentOptions<AccountMessage> { FailurePolicy = FailurePolicy.Escalate<AccountMessage>() },
    };

    private static IMessageHandler<AccountState, AccountMessage> CreateHandler(ActorContext<AccountMessage> context)
        => MessageHandler.For<AccountState, AccountMessage>()
            .On<OpenAccount, AccountSnapshot>((account, open) =>
            {
                if (account.IsOpen)
                {
                    return (account, OperationResult.Reject(new AccountAlreadyOpenError(account.Id)));
                }

                var opened = account with { Holder = open.Holder };

                // Actor-to-actor: the registry sees this account as the sender, under the
                // same correlation id as the HTTP request that opened it.
                context.System.Resolve<RegistryMessage>(Registry.Path)?.TryPost(new AccountOpened(account.Id));
                return (opened, opened.ToSnapshot());
            })
            .On<Deposit, AccountSnapshot>((account, deposit) =>
            {
                if (!account.IsOpen)
                {
                    return (account, OperationResult.Reject(new AccountNotFoundError(account.Id)));
                }

                var credited = account with { Balance = account.Balance + deposit.Amount.Value };
                return (credited, credited.ToSnapshot());
            })
            .On<Withdraw, AccountSnapshot>((account, withdraw) =>
            {
                if (!account.IsOpen)
                {
                    return (account, OperationResult.Reject(new AccountNotFoundError(account.Id)));
                }

                if (withdraw.Amount.Value > account.Balance)
                {
                    return (account, OperationResult.Reject(
                        new InsufficientFundsError(account.Id, account.Balance, withdraw.Amount.Value)));
                }

                var debited = account with { Balance = account.Balance - withdraw.Amount.Value };
                return (debited, debited.ToSnapshot());
            })
            .Build();
}

public abstract record RegistryMessage;

public sealed record AccountOpened(AccountId Id) : RegistryMessage;

/// <summary>
/// The <c>/accounts</c> actor: lists opened accounts and supervises them. A ledger must
/// never lose a balance, so a failed account resumes from its last committed state
/// instead of restarting from zero.
/// </summary>
public static class Registry
{
    public static readonly ActorPath Path = ActorPath.Create("/accounts");

    public static ActorDefinition<ImmutableSortedSet<string>, RegistryMessage> Definition { get; } =
        ActorDefinition.Create(
            ImmutableSortedSet<string>.Empty,
            MessageHandler.For<ImmutableSortedSet<string>, RegistryMessage>()
                .On<AccountOpened>(static (accounts, opened) => accounts.Add(opened.Id.Value))
                .Build())
        with
        {
            ChildSupervision = SupervisionStrategy.Resume,
        };
}
