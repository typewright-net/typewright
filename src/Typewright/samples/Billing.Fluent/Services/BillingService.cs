using System.Collections.Concurrent;
using Billing.Fluent.Domain;

namespace Billing.Fluent.Services;

/// <summary>Stores and searches billing documents.</summary>
public interface IBillingService
{
    BillingDocument Upsert(InvoiceNumber id, BillingDocument document);

    BillingDocument? Get(InvoiceNumber id);

    IReadOnlyList<BillingDocument> Search(BillingDocumentSearch search);
}

/// <summary>In-memory implementation for the sample.</summary>
public sealed class InMemoryBillingService : IBillingService
{
    private readonly ConcurrentDictionary<InvoiceNumber, BillingDocument> _documents = new();

    public BillingDocument Upsert(InvoiceNumber id, BillingDocument document)
    {
        _documents[id] = document;
        return document;
    }

    public BillingDocument? Get(InvoiceNumber id)
        => _documents.TryGetValue(id, out var document) ? document : null;

    public IReadOnlyList<BillingDocument> Search(BillingDocumentSearch search)
        => _documents.Values
            .Where(document => document.AccountNumber == search.AccountNumber
                && document.InvoiceDate >= search.BeginDate
                && document.InvoiceDate <= search.EndDate)
            .OrderBy(document => document.InvoiceDate.Value)
            .ToList();
}
