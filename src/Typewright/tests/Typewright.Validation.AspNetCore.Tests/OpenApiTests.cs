using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Typewright.Validation.AspNetCore.Tests;

/// <summary>
/// The OpenAPI document describes domain types from their generated descriptors: field
/// values as raw primitives with rule metadata, models as their DTO schema, and every
/// domain-typed operation with the 400 validation response.
/// </summary>
public class OpenApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public OpenApiTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private async Task<JsonElement> GetDocumentAsync()
    {
        var response = await _factory.CreateClient().GetAsync(
            "/openapi/v1.json", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return document.RootElement.Clone();
    }

    private static JsonElement Resolve(JsonElement document, JsonElement node)
    {
        // Follows a $ref into components/schemas when present.
        if (node.TryGetProperty("$ref", out var reference))
        {
            var parts = reference.GetString()!.Split('/');
            return document.GetProperty("components").GetProperty("schemas").GetProperty(parts[^1]);
        }

        return node;
    }

    [Fact]
    public async Task Route_parameter_describes_raw_primitive_with_rules()
    {
        var document = await GetDocumentAsync();
        var put = document.GetProperty("paths").GetProperty("/invoices/{id}").GetProperty("put");

        var id = put.GetProperty("parameters").EnumerateArray()
            .Single(parameter => parameter.GetProperty("name").GetString() == "id");
        Assert.Equal("path", id.GetProperty("in").GetString());
        Assert.True(id.GetProperty("required").GetBoolean());

        var schema = Resolve(document, id.GetProperty("schema"));
        Assert.Equal("string", schema.GetProperty("type").GetString());
        Assert.Equal(@"^I\d{9}$", schema.GetProperty("pattern").GetString());
    }

    [Fact]
    public async Task Query_parameters_describe_their_raw_primitives()
    {
        var document = await GetDocumentAsync();
        var get = document.GetProperty("paths").GetProperty("/invoices").GetProperty("get");
        var parameters = get.GetProperty("parameters").EnumerateArray().ToArray();

        var begin = parameters.Single(parameter => parameter.GetProperty("name").GetString() == "begin");
        Assert.Equal("query", begin.GetProperty("in").GetString());
        var beginSchema = Resolve(document, begin.GetProperty("schema"));
        Assert.Equal("string", beginSchema.GetProperty("type").GetString());
        Assert.Equal("date", beginSchema.GetProperty("format").GetString());

        var account = parameters.Single(parameter => parameter.GetProperty("name").GetString() == "account");
        var accountSchema = Resolve(document, account.GetProperty("schema"));
        Assert.Equal(64, accountSchema.GetProperty("maxLength").GetInt32());
    }

    [Fact]
    public async Task Body_and_response_describe_the_dto_shape()
    {
        var document = await GetDocumentAsync();
        var put = document.GetProperty("paths").GetProperty("/invoices/{id}").GetProperty("put");

        var bodySchema = Resolve(document, put
            .GetProperty("requestBody").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema"));
        var properties = bodySchema.GetProperty("properties");

        var invoiceNumber = Resolve(document, properties.GetProperty("invoiceNumber"));
        Assert.Equal(@"^I\d{9}$", invoiceNumber.GetProperty("pattern").GetString());

        Assert.True(properties.TryGetProperty("memo", out _));
        var lineItems = properties.GetProperty("lineItems");

        var lineItemSchema = Resolve(document, Resolve(document, lineItems).GetProperty("items"));
        var quantity = Resolve(document, lineItemSchema.GetProperty("properties").GetProperty("quantity"));
        Assert.Equal(1m, quantity.GetProperty("minimum").GetDecimal());
        Assert.Equal(10000m, quantity.GetProperty("maximum").GetDecimal());
    }

    [Fact]
    public async Task Domain_typed_operations_gain_the_400_validation_response()
    {
        var document = await GetDocumentAsync();
        var paths = document.GetProperty("paths");

        foreach (var (path, verb) in new[] { ("/invoices/{id}", "put"), ("/invoices/{id}", "get"), ("/invoices", "get") })
        {
            var operation = paths.GetProperty(path).GetProperty(verb);
            var badRequest = operation.GetProperty("responses").GetProperty("400");
            Assert.True(badRequest.GetProperty("content").TryGetProperty("application/problem+json", out _));
        }
    }
}
