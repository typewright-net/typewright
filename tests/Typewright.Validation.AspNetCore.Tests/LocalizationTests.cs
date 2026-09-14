using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Typewright.Validation;
using Xunit;

namespace Typewright.Validation.AspNetCore.Tests;

/// <summary>
/// Registering an <see cref="IValidationMessageLocalizer"/> localizes the messages in the
/// problem-details response; paths and codes stay stable.
/// </summary>
public class LocalizationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public LocalizationTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private sealed class CodeEchoLocalizer : IValidationMessageLocalizer
    {
        public string Localize(ValidationError error) => $"LOC:{error.Code}";
    }

    [Fact]
    public async Task Problem_details_messages_flow_through_the_localizer()
    {
        var client = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
                services.AddSingleton<IValidationMessageLocalizer, CodeEchoLocalizer>()))
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
}
