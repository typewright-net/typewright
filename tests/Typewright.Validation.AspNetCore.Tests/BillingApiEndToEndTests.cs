using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Typewright.Validation.AspNetCore.Tests;

/// <summary>
/// End-to-end tests over the Billing.Api sample: endpoint signatures are pure domain
/// types, invalid input produces an RFC 9457 response carrying every failure, and the
/// endpoint body never runs on invalid input.
/// </summary>
public class BillingApiEndToEndTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public BillingApiEndToEndTests(WebApplicationFactory<Program> factory)
        => _client = factory.CreateClient();

    private static object ValidBody(
        string invoiceNumber = "I123456789",
        string invoiceDate = "2026-01-15",
        string accountNumber = "79927398713") => new
        {
            invoiceNumber,
            invoiceDate,
            accountNumber,
            lineItems = new[]
            {
                new { description = "Widget", quantity = 2, unitPrice = 9.99 },
            },
        };

    private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return document.RootElement.Clone();
    }

    private static string[] Strings(JsonElement element)
        => [.. element.EnumerateArray().Select(item => item.GetString()!)];

    [Fact]
    public async Task Put_with_valid_request_stores_and_returns_the_document()
    {
        var response = await _client.PutAsJsonAsync(
            "/invoices/I000000001", ValidBody(invoiceNumber: "I000000001"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("I000000001", body.GetProperty("invoiceNumber").GetString());
        Assert.Equal("2026-01-15", body.GetProperty("invoiceDate").GetString());
        Assert.Equal("79927398713", body.GetProperty("accountNumber").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("memo").ValueKind);
        Assert.Equal(1, body.GetProperty("lineItems").GetArrayLength());

        var fetched = await _client.GetAsync("/invoices/I000000001", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
    }

    [Fact]
    public async Task Put_with_invalid_route_id_reports_the_parameter()
    {
        var response = await _client.PutAsJsonAsync(
            "/invoices/not-an-invoice", ValidBody(), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Equal(400, problem.GetProperty("status").GetInt32());
        Assert.Equal(["pattern"], Strings(problem.GetProperty("errorCodes").GetProperty("id")));
        Assert.Single(problem.GetProperty("errors").GetProperty("id").EnumerateArray());
    }

    [Fact]
    public async Task Put_with_many_invalid_body_fields_reports_every_error_at_its_json_path()
    {
        var body = new
        {
            invoiceNumber = "X123",
            accountNumber = " 79927398714 ",
            memo = "",
            lineItems = new object[]
            {
                new { description = "ok", quantity = 1, unitPrice = 1.0 },
                new { quantity = 0, unitPrice = -5.0 },
            },
        };

        var response = await _client.PutAsJsonAsync("/invoices/I000000002", body, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        var codes = problem.GetProperty("errorCodes");
        Assert.Equal(["pattern"], Strings(codes.GetProperty("invoiceNumber")));
        Assert.Equal(["required"], Strings(codes.GetProperty("invoiceDate")));
        Assert.Equal(["checksum"], Strings(codes.GetProperty("accountNumber")));
        Assert.Equal(["length"], Strings(codes.GetProperty("memo")));
        Assert.Equal(["required"], Strings(codes.GetProperty("lineItems[1].description")));
        Assert.Equal(["range"], Strings(codes.GetProperty("lineItems[1].quantity")));
        Assert.Equal(["range"], Strings(codes.GetProperty("lineItems[1].unitPrice")));
        Assert.Equal(7, problem.GetProperty("errors").EnumerateObject().Count());
    }

    [Fact]
    public async Task Put_with_malformed_json_reports_a_single_body_error()
    {
        using var content = new StringContent("{ this is not json", Encoding.UTF8, "application/json");
        var response = await _client.PutAsync("/invoices/I000000003", content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Equal(["json_invalid"], Strings(problem.GetProperty("errorCodes").GetProperty("$")));
    }

    [Fact]
    public async Task Put_with_non_json_content_type_is_unsupported_media_type()
    {
        using var content = new StringContent("invoice", Encoding.UTF8, "text/plain");
        var response = await _client.PutAsync("/invoices/I000000004", content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task Get_by_unknown_id_is_not_found()
    {
        var response = await _client.GetAsync("/invoices/I999999998", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Search_returns_documents_in_range_for_the_account()
    {
        // Seed two documents on one account and one on another.
        (await _client.PutAsJsonAsync("/invoices/I100000001",
            ValidBody("I100000001", "2026-03-10", "18"), TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        (await _client.PutAsJsonAsync("/invoices/I100000002",
            ValidBody("I100000002", "2026-03-20", "18"), TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        (await _client.PutAsJsonAsync("/invoices/I100000003",
            ValidBody("I100000003", "2026-03-15", "26"), TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();

        var response = await _client.GetAsync(
            "/invoices?begin=2026-03-01&end=2026-03-15&account=18", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var numbers = body.EnumerateArray().Select(doc => doc.GetProperty("invoiceNumber").GetString()).ToArray();
        Assert.Equal(["I100000001"], numbers);
    }

    [Fact]
    public async Task Search_with_inverted_range_reports_the_cross_field_rule_at_object_level()
    {
        var response = await _client.GetAsync(
            "/invoices?begin=2026-02-01&end=2026-01-01&account=79927398713", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Equal(["date_range"], Strings(problem.GetProperty("errorCodes").GetProperty("")));
    }

    [Fact]
    public async Task Search_with_unparsable_date_reports_a_format_error()
    {
        var response = await _client.GetAsync(
            "/invoices?begin=not-a-date&end=2026-01-01&account=79927398713", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Equal(["format"], Strings(problem.GetProperty("errorCodes").GetProperty("begin")));
    }

    [Fact]
    public async Task Search_with_missing_parameter_reports_it_as_required()
    {
        var response = await _client.GetAsync(
            "/invoices?begin=2026-01-01&end=2026-02-01", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Equal(["required"], Strings(problem.GetProperty("errorCodes").GetProperty("account")));
    }
}
