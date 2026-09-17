using Billing.Api.Domain;
using Billing.Api.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Typewright.Validation.AspNetCore;
using Typewright.Validation.AspNetCore.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi(options => options.AddTypewright());
builder.Services.AddControllers(options => options.AddTypewright());
builder.Services.AddSingleton<IBillingService, InMemoryBillingService>();

var app = builder.Build();

app.MapOpenApi();
app.MapControllers();

// Turns DomainValidationException from binding (or handlers) into an RFC 9457
// HttpValidationProblemDetails response carrying every failure.
app.UseTypewright();

// The endpoint signatures are written entirely in domain types; if the request cannot be
// turned into those types, the caller gets a 400 with all failures and the body never runs.
app.MapPut("/invoices/{id}", (InvoiceNumber id, BillingDocument document, IBillingService billing)
    => TypedResults.Ok(billing.Upsert(id, document)));

app.MapGet("/invoices/{id}", Results<Ok<BillingDocument>, NotFound> (InvoiceNumber id, IBillingService billing)
    => billing.Get(id) is { } document ? TypedResults.Ok(document) : TypedResults.NotFound());

app.MapGet("/invoices", (InvoiceDate begin, InvoiceDate end, AccountNumber account, IBillingService billing)
    => TypedResults.Ok(billing.Search(BillingDocumentSearch.Create(begin, end, account))));

app.Run();

/// <summary>Exposed for WebApplicationFactory-based end-to-end tests.</summary>
public partial class Program;
