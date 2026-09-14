using Billing.Api.Domain;
using Billing.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Billing.Api.Controllers;

/// <summary>
/// The same operations as the minimal API endpoints, as an MVC controller: domain-typed
/// action parameters bind through the Typewright model binders, and invalid input yields
/// the automatic <c>[ApiController]</c> 400 with the same <c>errors</c> shape.
/// </summary>
[ApiController]
[Route("mvc/invoices")]
public sealed class InvoicesController : ControllerBase
{
    private readonly IBillingService _billing;

    public InvoicesController(IBillingService billing) => _billing = billing;

    /// <summary>Creates or replaces an invoice.</summary>
    [HttpPut("{id}")]
    public ActionResult<BillingDocument> Upsert(InvoiceNumber id, BillingDocument document)
        => _billing.Upsert(id, document);

    /// <summary>Returns one invoice.</summary>
    [HttpGet("{id}")]
    public ActionResult<BillingDocument> Get(InvoiceNumber id)
        => _billing.Get(id) is { } document ? document : NotFound();

    /// <summary>Searches an account's invoices by date range.</summary>
    [HttpGet]
    public ActionResult<IReadOnlyList<BillingDocument>> Search(InvoiceDate begin, InvoiceDate end, AccountNumber account)
        => Ok(_billing.Search(BillingDocumentSearch.Create(begin, end, account)));
}
