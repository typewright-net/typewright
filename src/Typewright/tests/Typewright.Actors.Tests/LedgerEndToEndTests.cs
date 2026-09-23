using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Typewright.Actors.Tests;

/// <summary>
/// The Ledger sample over HTTP: one actor per account behind endpoints written in domain
/// types; typed rejections become 404/409 problem responses; reads bypass the mailbox.
/// </summary>
public class LedgerEndToEndTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public LedgerEndToEndTests(WebApplicationFactory<Program> factory) => _client = factory.CreateClient();

    private static int s_nextAccount = Random.Shared.Next(0, 900_000);

    private static string NewAccountId() => $"AC{Interlocked.Increment(ref s_nextAccount):D6}";

    private async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    [Fact]
    public async Task Open_deposit_withdraw_and_read_an_account()
    {
        var id = NewAccountId();

        var opened = await _client.PutAsJsonAsync($"/accounts/{id}", new { holder = "  Ada Lovelace " });
        Assert.Equal(HttpStatusCode.Created, opened.StatusCode);
        Assert.Equal("Ada Lovelace", (await ReadJsonAsync(opened)).GetProperty("holder").GetString());

        var deposited = await _client.PostAsJsonAsync($"/accounts/{id}/deposits", new { amount = 100.50m });
        Assert.Equal(100.50m, (await ReadJsonAsync(deposited)).GetProperty("balance").GetDecimal());

        var withdrew = await _client.PostAsJsonAsync($"/accounts/{id}/withdrawals", new { amount = 40m });
        Assert.Equal(60.50m, (await ReadJsonAsync(withdrew)).GetProperty("balance").GetDecimal());

        var read = await _client.GetAsync($"/accounts/{id}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(60.50m, (await ReadJsonAsync(read)).GetProperty("balance").GetDecimal());

        var listed = await _client.GetFromJsonAsync<string[]>("/accounts");
        Assert.Contains(id, listed!);
    }

    [Fact]
    public async Task Domain_rejections_become_typed_problem_responses()
    {
        var id = NewAccountId();
        await _client.PutAsJsonAsync($"/accounts/{id}", new { holder = "Grace" });

        var again = await _client.PutAsJsonAsync($"/accounts/{id}", new { holder = "Grace" });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("account_already_open", (await ReadJsonAsync(again)).GetProperty("code").GetString());

        var overdrawn = await _client.PostAsJsonAsync($"/accounts/{id}/withdrawals", new { amount = 1m });
        Assert.Equal(HttpStatusCode.Conflict, overdrawn.StatusCode);
        Assert.Equal("insufficient_funds", (await ReadJsonAsync(overdrawn)).GetProperty("code").GetString());

        var missing = await _client.PostAsJsonAsync($"/accounts/{NewAccountId()}/deposits", new { amount = 1m });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("account_not_found", (await ReadJsonAsync(missing)).GetProperty("code").GetString());

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/accounts/{NewAccountId()}")).StatusCode);
    }

    [Fact]
    public async Task Invalid_input_never_reaches_an_actor()
    {
        var badId = await _client.PutAsJsonAsync("/accounts/not-an-id", new { holder = "X" });
        Assert.Equal(HttpStatusCode.BadRequest, badId.StatusCode);

        var badAmount = await _client.PostAsJsonAsync($"/accounts/{NewAccountId()}/deposits", new { amount = -5m });
        Assert.Equal(HttpStatusCode.BadRequest, badAmount.StatusCode);
        var codes = (await ReadJsonAsync(badAmount)).GetProperty("errorCodes");
        Assert.Equal("range", codes.GetProperty("amount")[0].GetString());
    }

    [Fact]
    public async Task Concurrent_commands_on_one_account_are_serialized()
    {
        var id = NewAccountId();
        await _client.PutAsJsonAsync($"/accounts/{id}", new { holder = "Many Hands" });

        await Task.WhenAll(Enumerable.Range(0, 50).Select(_ =>
            _client.PostAsJsonAsync($"/accounts/{id}/deposits", new { amount = 2m })));

        var read = await _client.GetAsync($"/accounts/{id}");
        Assert.Equal(100m, (await ReadJsonAsync(read)).GetProperty("balance").GetDecimal());
    }
}
