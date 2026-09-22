using Billing.Fluent.Domain;
using Billing.Fluent.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Typewright.Validation.AspNetCore;
using Typewright.Validation.AspNetCore.OpenApi;

// The same application as Billing.Api, with the domain defined entirely through the
// fluent API: no attributes, no source generator. Binding, problem details, OpenAPI, and
// the error model all come from the same shared runtime.

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi(options => options.AddTypewright());
builder.Services.AddSingleton<IBillingService, InMemoryBillingService>();

var app = builder.Build();

app.UseTypewright();
app.MapOpenApi();

app.MapPut("/invoices/{id}", (InvoiceNumber id, BillingDocument document, IBillingService billing)
    => TypedResults.Ok(billing.Upsert(id, document)));

app.MapGet("/invoices/{id}", Results<Ok<BillingDocument>, NotFound> (InvoiceNumber id, IBillingService billing)
    => billing.Get(id) is { } document ? TypedResults.Ok(document) : TypedResults.NotFound());

app.MapGet("/invoices", (InvoiceDate begin, InvoiceDate end, AccountNumber account, IBillingService billing)
    => TypedResults.Ok(billing.Search(BillingDocumentSearch.Create(begin, end, account))));

app.Run();

/// <summary>Exposed for WebApplicationFactory-based end-to-end tests.</summary>
public partial class Program;
