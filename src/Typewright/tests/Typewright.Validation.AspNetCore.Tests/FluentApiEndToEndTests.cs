extern alias fluent;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Typewright.Validation.AspNetCore.Tests;

/// <summary>
/// The fluent sample runs the same HTTP pipeline — binding, accumulation, RFC 9457
/// responses, OpenAPI — with no source generator or attributes anywhere in its domain.
/// </summary>
public class FluentApiEndToEndTests : IClassFixture<WebApplicationFactory<fluent::Program>>
{
    private readonly HttpClient _client;

    public FluentApiEndToEndTests(WebApplicationFactory<fluent::Program> factory)
        => _client = factory.CreateClient();

    private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return document.RootElement.Clone();
    }

    [Fact]
    public async Task Put_with_valid_request_stores_and_round_trips()
    {
        var response = await _client.PutAsJsonAsync("/invoices/I500000001", new
        {
            invoiceNumber = "I500000001",
            invoiceDate = "2026-05-01",
            accountNumber = "79927398713",
            lineItems = new[] { new { description = "Widget", quantity = 2, unitPrice = 9.99 } },
        }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("I500000001", body.GetProperty("invoiceNumber").GetString());
        Assert.Equal("79927398713", body.GetProperty("accountNumber").GetString());

        var fetched = await _client.GetAsync("/invoices/I500000001", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
    }

    [Fact]
    public async Task Invalid_body_reports_every_error_at_its_json_path()
    {
        var response = await _client.PutAsJsonAsync("/invoices/I500000002", new
        {
            invoiceNumber = "X123",
            accountNumber = " 79927398714 ",
            memo = "",
            lineItems = new object[]
            {
                new { description = "ok", quantity = 1, unitPrice = 1.0 },
                new { quantity = 0, unitPrice = -5.0 },
            },
        }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        var codes = problem.GetProperty("errorCodes");
        Assert.Equal("pattern", codes.GetProperty("invoiceNumber")[0].GetString());
        Assert.Equal("required", codes.GetProperty("invoiceDate")[0].GetString());
        Assert.Equal("checksum", codes.GetProperty("accountNumber")[0].GetString());
        Assert.Equal("length", codes.GetProperty("memo")[0].GetString());
        Assert.Equal("required", codes.GetProperty("lineItems[1].description")[0].GetString());
        Assert.Equal("range", codes.GetProperty("lineItems[1].quantity")[0].GetString());
        Assert.Equal("range", codes.GetProperty("lineItems[1].unitPrice")[0].GetString());
    }

    [Fact]
    public async Task Cross_field_rule_reports_at_object_level()
    {
        var response = await _client.GetAsync(
            "/invoices?begin=2026-02-01&end=2026-01-01&account=79927398713",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Equal("date_range", problem.GetProperty("errorCodes").GetProperty("")[0].GetString());
    }

    [Fact]
    public async Task OpenApi_documents_the_fluent_domain_from_its_descriptors()
    {
        var response = await _client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var put = document.RootElement.GetProperty("paths").GetProperty("/invoices/{id}").GetProperty("put");

        var id = put.GetProperty("parameters").EnumerateArray()
            .Single(parameter => parameter.GetProperty("name").GetString() == "id");
        var schema = id.GetProperty("schema");
        if (schema.TryGetProperty("$ref", out var reference))
        {
            var parts = reference.GetString()!.Split('/');
            schema = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(parts[^1]);
        }

        Assert.Equal(@"^I\d{9}$", schema.GetProperty("pattern").GetString());
        Assert.True(put.GetProperty("responses").TryGetProperty("400", out _));
    }
}
