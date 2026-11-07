using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Headroom.Data;
using Headroom.Email;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Headroom.Tests.Api;

// Token lifetimes, refresh rotation, and the checks on the Person, with a fake clock.
public class OAuthRefreshTests : IClassFixture<HeadroomApp>, IAsyncLifetime
{
    private const string Ann = "refresh-ann@example.com";
    private readonly FakeEmailSender _mail = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero));
    private readonly HeadroomApp _app;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly OAuthFlow _flow;

    public OAuthRefreshTests(HeadroomApp app)
    {
        _app = app;
        _factory = app.Factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddSingleton<IEmailSender>(_mail);
            s.AddSingleton<TimeProvider>(_time);
        }));
        _flow = new OAuthFlow(_factory, _mail);
    }

    public async ValueTask InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
        if (db.People.Any()) return;
        var ann = new Person { Name = "Ann Planner", Email = Ann, IsPlanner = true };
        db.InstanceSettings.Add(new InstanceSettings { CompanyName = "Acme", LastChangedBy = ann.Id });
        db.People.Add(ann);
        await db.SaveChangesAsync();
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync(); // stops this test's host and its background services

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Str(JsonElement tokens, string name) => tokens.GetProperty(name).GetString()!;

    private async Task<HttpStatusCode> McpStatusAsync(string token, WebApplicationFactory<Program>? factory = null)
    {
        var http = new OAuthFlow(factory ?? _factory, _mail).BearerClient(token);
        return (await http.PostAsync("/mcp", new StringContent("{}", Encoding.UTF8, "application/json"), Ct)).StatusCode;
    }

    [Fact]
    public async Task AnAccessTokenIsValidForOneHourAndRefusedAfter()
    {
        var token = Str(await _flow.TokensAsync(await _flow.AddAppAsync(), Ann), "access_token");

        _time.Advance(TimeSpan.FromMinutes(59));
        Assert.NotEqual(HttpStatusCode.Unauthorized, await McpStatusAsync(token));
        _time.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(HttpStatusCode.Unauthorized, await McpStatusAsync(token));
    }

    [Fact]
    public async Task ARefreshGivesNewTokensAndTheOldRefreshTokenIsRefused()
    {
        var app = await _flow.AddAppAsync();
        var first = await _flow.TokensAsync(app, Ann);

        var response = await _flow.RefreshAsync(app, Str(first, "refresh_token"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var second = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.NotEqual(Str(first, "refresh_token"), Str(second, "refresh_token"));
        Assert.NotEqual(HttpStatusCode.Unauthorized, await McpStatusAsync(Str(second, "access_token")));
        Assert.Equal(HttpStatusCode.BadRequest, (await _flow.RefreshAsync(app, Str(first, "refresh_token"))).StatusCode);
    }

    [Fact]
    public async Task ReusingAnOldRefreshTokenRevokesTheNewOne()
    {
        var app = await _flow.AddAppAsync();
        var first = await _flow.TokensAsync(app, Ann);
        var second = await (await _flow.RefreshAsync(app, Str(first, "refresh_token"))).Content.ReadFromJsonAsync<JsonElement>(Ct);

        await _flow.RefreshAsync(app, Str(first, "refresh_token"));

        Assert.Equal(HttpStatusCode.BadRequest, (await _flow.RefreshAsync(app, Str(second, "refresh_token"))).StatusCode);
    }

    [Fact]
    public async Task ARefreshTokenExpiresAfter30DaysWithoutUseAndEachUseRestartsTheCount()
    {
        var app = await _flow.AddAppAsync();
        var first = await _flow.TokensAsync(app, Ann);

        _time.Advance(TimeSpan.FromDays(20));
        var second = await (await _flow.RefreshAsync(app, Str(first, "refresh_token"))).Content.ReadFromJsonAsync<JsonElement>(Ct);
        _time.Advance(TimeSpan.FromDays(20));
        var third = await _flow.RefreshAsync(app, Str(second, "refresh_token"));
        Assert.Equal(HttpStatusCode.OK, third.StatusCode);

        _time.Advance(TimeSpan.FromDays(31));
        Assert.Equal(HttpStatusCode.BadRequest, (await _flow.RefreshAsync(app, Str(await third.Content.ReadFromJsonAsync<JsonElement>(Ct), "refresh_token"))).StatusCode);
    }

    [Theory]
    [InlineData("ended")]
    [InlineData("erased")]
    public async Task ARefreshForAPersonWhoLeftIsRefused(string how)
    {
        var app = await _flow.AddAppAsync();
        var refresh = Str(await _flow.TokensAsync(app, Ann), "refresh_token");
        await UpdateAnnAsync(how == "ended" ? s => s.SetProperty(p => p.EndDate, new DateOnly(2026, 10, 1)) : s => s.SetProperty(p => p.ErasedNumber, 1));

        try
        {
            var response = await _flow.RefreshAsync(app, refresh);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("invalid_grant", await response.Content.ReadAsStringAsync(Ct));
        }
        finally
        {
            await UpdateAnnAsync(s => s.SetProperty(p => p.EndDate, (DateOnly?)null).SetProperty(p => p.ErasedNumber, (int?)null));
        }
    }

    private async Task UpdateAnnAsync(Action<Microsoft.EntityFrameworkCore.Query.UpdateSettersBuilder<Person>> set)
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<HeadroomDbContext>().People.Where(p => p.Email == Ann).ExecuteUpdateAsync(set, Ct);
    }

    [Fact]
    public async Task TokensIssuedBeforeARestartStayValid()
    {
        var app = await _flow.AddAppAsync();
        var tokens = await _flow.TokensAsync(app, Ann);

        // A new host on the same database is a restart: only the database carries over.
        await using var restarted = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Headroom"] = _app.ConnectionString }));
            b.ConfigureServices(s => s.AddSingleton<TimeProvider>(_time));
        });

        Assert.NotEqual(HttpStatusCode.Unauthorized, await McpStatusAsync(Str(tokens, "access_token"), restarted));
        Assert.Equal(HttpStatusCode.OK, (await new OAuthFlow(restarted, _mail).RefreshAsync(app, Str(tokens, "refresh_token"))).StatusCode);
    }
}
