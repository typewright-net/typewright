using System.Collections.Concurrent;
using Billing.Api.Domain;

namespace Billing.Api.Services;

/// <summary>Stores and searches billing documents.</summary>
public interface IBillingService
{
    /// <summary>Creates or replaces the document stored under <paramref name="id"/>.</summary>
    BillingDocument Upsert(InvoiceNumber id, BillingDocument document);

    /// <summary>Returns the document stored under <paramref name="id"/>, or null.</summary>
    BillingDocument? Get(InvoiceNumber id);

    /// <summary>Returns the account's documents whose invoice date falls in the search range.</summary>
    IReadOnlyList<BillingDocument> Search(BillingDocumentSearch search);
}

/// <summary>In-memory implementation for the sample.</summary>
public sealed class InMemoryBillingService : IBillingService
{
    private readonly ConcurrentDictionary<InvoiceNumber, BillingDocument> _documents = new();

    /// <inheritdoc />
    public BillingDocument Upsert(InvoiceNumber id, BillingDocument document)
    {
        _documents[id] = document;
        return document;
    }

    /// <inheritdoc />
    public BillingDocument? Get(InvoiceNumber id)
        => _documents.TryGetValue(id, out var document) ? document : null;

    /// <inheritdoc />
    public IReadOnlyList<BillingDocument> Search(BillingDocumentSearch search)
        => _documents.Values
            .Where(document => document.AccountNumber == search.AccountNumber
                && document.InvoiceDate >= search.BeginDate
                && document.InvoiceDate <= search.EndDate)
            .OrderBy(document => document.InvoiceDate.Value)
            .ToList();
}
