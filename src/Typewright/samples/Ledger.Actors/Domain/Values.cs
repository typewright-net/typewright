using Typewright.Validation;

namespace Ledger.Actors.Domain;

/// <summary>An account number: two uppercase letters and six digits.</summary>
[DomainValue<string>]
[Pattern(@"^[A-Z]{2}\d{6}$")]
public readonly partial record struct AccountId;

/// <summary>A positive amount of money.</summary>
[DomainValue<decimal>]
[Range("0.01", "1000000")]
public readonly partial record struct Amount;

/// <summary>The account holder's name.</summary>
[DomainValue<string>]
[Length(1, 100), NotEmpty, Normalize(Trim = true)]
public readonly partial record struct AccountHolder;

/// <summary>Body of <c>PUT /accounts/{id}</c>.</summary>
[DomainModel]
public sealed partial record OpenAccountRequest
{
    public required AccountHolder Holder { get; init; }
}

/// <summary>Body of deposits and withdrawals.</summary>
[DomainModel]
public sealed partial record MoneyRequest
{
    public required Amount Amount { get; init; }
}

/// <summary>No open account has this id. Code <c>account_not_found</c>.</summary>
public sealed record AccountNotFoundError(AccountId Account) : IDomainError
{
    public string DefaultMessage => $"Account {Account} does not exist.";
}

/// <summary>The account is already open. Code <c>account_already_open</c>.</summary>
public sealed record AccountAlreadyOpenError(AccountId Account) : IDomainError
{
    public string DefaultMessage => $"Account {Account} is already open.";
}

/// <summary>A withdrawal exceeds the balance. Code <c>insufficient_funds</c>.</summary>
public sealed record InsufficientFundsError(AccountId Account, decimal Balance, decimal Requested) : IDomainError
{
    public string DefaultMessage => $"Account {Account} holds {Balance}, less than the {Requested} requested.";
}
