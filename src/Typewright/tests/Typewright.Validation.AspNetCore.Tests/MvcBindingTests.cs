using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Typewright.Validation.AspNetCore.Tests;

/// <summary>
/// The MVC controller path: domain-typed action parameters bind through the Typewright
/// model binders, and invalid input produces the automatic [ApiController] 400 with the
/// same JSON-path error keys as the minimal API endpoints.
/// </summary>
public class MvcBindingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public MvcBindingTests(WebApplicationFactory<Program> factory)
        => _client = factory.CreateClient();

    private static object ValidBody(string invoiceNumber = "I300000001") => new
    {
        invoiceNumber,
        invoiceDate = "2026-04-01",
        accountNumber = "79927398713",
        lineItems = new[] { new { description = "Widget", quantity = 2, unitPrice = 9.99 } },
    };

    private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return document.RootElement.Clone();
    }

    [Fact]
    public async Task Put_with_valid_request_binds_and_stores()
    {
        var response = await _client.PutAsJsonAsync(
            "/mvc/invoices/I300000001", ValidBody(), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("I300000001", body.GetProperty("invoiceNumber").GetString());

        var fetched = await _client.GetAsync("/mvc/invoices/I300000001", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
    }

    [Fact]
    public async Task Invalid_route_id_reports_through_model_state()
    {
        var response = await _client.PutAsJsonAsync(
            "/mvc/invoices/nope", ValidBody(), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Single(problem.GetProperty("errors").GetProperty("id").EnumerateArray());
    }

    [Fact]
    public async Task Invalid_body_fields_report_every_error_at_its_json_path()
    {
        var body = new
        {
            invoiceNumber = "X123",
            accountNumber = "79927398713",
            invoiceDate = "2026-04-01",
            lineItems = new object[] { new { description = "ok", quantity = 0, unitPrice = -5.0 } },
        };

        var response = await _client.PutAsJsonAsync(
            "/mvc/invoices/I300000002", body, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        var errors = problem.GetProperty("errors");
        Assert.True(errors.TryGetProperty("invoiceNumber", out _));
        Assert.True(errors.TryGetProperty("lineItems[0].quantity", out _));
        Assert.True(errors.TryGetProperty("lineItems[0].unitPrice", out _));
    }

    [Fact]
    public async Task Missing_query_parameter_reports_required()
    {
        var response = await _client.GetAsync(
            "/mvc/invoices?begin=2026-01-01&end=2026-02-01", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.True(problem.GetProperty("errors").TryGetProperty("account", out _));
    }

    [Fact]
    public async Task Cross_field_rule_still_reports_through_the_middleware()
    {
        // The controller action builds the search from valid fields; the cross-field
        // failure throws and the Typewright middleware writes the RFC 9457 response.
        var response = await _client.GetAsync(
            "/mvc/invoices?begin=2026-02-01&end=2026-01-01&account=79927398713",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.True(problem.GetProperty("errors").TryGetProperty("", out _));
    }
}
