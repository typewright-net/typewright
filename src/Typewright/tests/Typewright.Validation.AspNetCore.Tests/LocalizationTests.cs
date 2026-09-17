using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Typewright.Validation;
using Xunit;

namespace Typewright.Validation.AspNetCore.Tests;

/// <summary>
/// Registering an <see cref="IDomainErrorRenderer"/> controls (or localizes) the messages
/// in the problem-details response; paths and codes stay stable and the domain never
/// produces presentation text.
/// </summary>
public class LocalizationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public LocalizationTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private sealed class CodeEchoRenderer : IDomainErrorRenderer
    {
        public string Render(ValidationFailure failure) => $"LOC:{failure.Error.Code}";
    }

    [Fact]
    public async Task Problem_details_messages_flow_through_the_renderer()
    {
        var client = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
                services.AddSingleton<IDomainErrorRenderer, CodeEchoRenderer>()))
            .CreateClient();

        var response = await client.PutAsJsonAsync(
            "/invoices/not-an-invoice",
            new { },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var messages = document.RootElement.GetProperty("errors").GetProperty("id");
        Assert.Equal("LOC:pattern", messages.EnumerateArray().Single().GetString());
    }

    [Fact]
    public async Task Mvc_model_state_messages_flow_through_the_renderer()
    {
        var client = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
                services.AddSingleton<IDomainErrorRenderer, CodeEchoRenderer>()))
            .CreateClient();

        var response = await client.GetAsync(
            "/mvc/invoices?begin=2026-01-01&end=2026-02-01", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var messages = document.RootElement.GetProperty("errors").GetProperty("account");
        Assert.Equal("LOC:required", messages.EnumerateArray().Single().GetString());
    }
}
