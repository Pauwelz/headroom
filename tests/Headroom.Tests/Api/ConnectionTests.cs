using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Headroom.Client.Api;
using Headroom.Data;
using Headroom.Email;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.EntityFrameworkCore.Models;

namespace Headroom.Tests.Api;

// See and revoke Connections, erase and delete a Person.
public class ConnectionTests : IClassFixture<HeadroomApp>, IAsyncLifetime
{
    private const string Ann = "conn-ann@example.com";
    private const string Zed = "conn-zed@example.com";
    private const string Bob = "conn-bob@example.com";
    private readonly FakeEmailSender _mail = new();
    private readonly WebApplicationFactory<Program> _factory;
    private readonly OAuthFlow _flow;

    public ConnectionTests(HeadroomApp app)
    {
        _factory = app.Factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<IEmailSender>(_mail)));
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
        db.People.Add(new Person { Name = "Zed", Email = Zed });
        db.People.Add(new Person { Name = "Bob", Email = Bob });
        await db.SaveChangesAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HttpResponseMessage> SendAsync(HttpClient session, HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(AntiforgeryToken.HeaderName, await TestLogin.ApiTokenAsync(session));
        return await session.SendAsync(request, Ct);
    }

    private static async Task<List<JsonElement>> ListAsync(HttpClient client, string url) =>
        (await client.GetFromJsonAsync<List<JsonElement>>(url, Ct))!;

    private async Task<T> Run<T>(Func<HeadroomDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<HeadroomDbContext>());
    }

    private Task<Guid> AddPersonAsync(string email) => Run(async db =>
    {
        var person = new Person { Name = "Extra", Email = email };
        db.People.Add(person);
        await db.SaveChangesAsync(Ct);
        return person.Id;
    });

    private Task<int> CountAsync(Guid personId) =>
        Run(db => db.Set<OpenIddictEntityFrameworkCoreAuthorization>().CountAsync(a => a.Subject == personId.ToString(), Ct));

    private async Task<HttpStatusCode> McpStatusAsync(string token) =>
        (await _flow.BearerClient(token).PostAsync("/mcp", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"), Ct)).StatusCode;

    [Fact]
    public async Task APersonSeesOnlyTheirOwnConnectionsAndRevokesThem()
    {
        var app = await _flow.AddAppAsync("Mine");
        var tokens = await _flow.TokensAsync(app, Zed);
        var (access, refresh) = (tokens.GetProperty("access_token").GetString()!, tokens.GetProperty("refresh_token").GetString()!);
        await _flow.TokenAsync(app, Bob);
        var zed = await TestLogin.SignInAsync(_factory, _mail, Zed);

        var connection = (await ListAsync(zed, "/api/me/connections")).Single(c => c.GetProperty("connectedApp").GetString() == "Mine");

        Assert.Equal("Zed", connection.GetProperty("owner").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(zed, HttpMethod.Delete, "/api/me/connections/" + connection.GetProperty("id").GetString())).StatusCode);
        Assert.DoesNotContain(await ListAsync(zed, "/api/me/connections"), c => c.GetProperty("connectedApp").GetString() == "Mine");
        Assert.Equal(HttpStatusCode.Unauthorized, await McpStatusAsync(access));
        Assert.Equal(HttpStatusCode.BadRequest, (await _flow.RefreshAsync(app, refresh)).StatusCode);
    }

    [Fact]
    public async Task APersonCannotRevokeTheConnectionOfSomeoneElse()
    {
        var access = await _flow.TokenAsync(await _flow.AddAppAsync("Theirs"), Bob);
        var bob = await TestLogin.SignInAsync(_factory, _mail, Bob);
        var id = (await ListAsync(bob, "/api/me/connections")).Single(c => c.GetProperty("connectedApp").GetString() == "Theirs").GetProperty("id").GetString();
        var zed = await TestLogin.SignInAsync(_factory, _mail, Zed);

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(zed, HttpMethod.Delete, "/api/me/connections/" + id)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(zed, HttpMethod.Delete, "/api/settings/connections/" + id)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await zed.GetAsync("/api/settings/connections", Ct)).StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, await McpStatusAsync(access));
    }

    [Fact]
    public async Task APlannerSeesAllConnectionsAndRevokesAny()
    {
        var access = await _flow.TokenAsync(await _flow.AddAppAsync("Any"), Bob);
        var ann = await TestLogin.SignInAsync(_factory, _mail, Ann);

        var connection = (await ListAsync(ann, "/api/settings/connections")).Single(c => c.GetProperty("connectedApp").GetString() == "Any");

        Assert.Equal("Bob", connection.GetProperty("owner").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(ann, HttpMethod.Delete, "/api/settings/connections/" + connection.GetProperty("id").GetString())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await McpStatusAsync(access));
    }

    [Fact]
    public async Task AnOAuthTokenCannotListOrRevokeConnections()
    {
        var token = await _flow.TokenAsync(await _flow.AddAppAsync("Locked"), Ann, OAuthFlow.Api);
        var bearer = _flow.BearerClient(token);

        Assert.Equal(HttpStatusCode.Forbidden, (await bearer.GetAsync("/api/me/connections", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bearer.GetAsync("/api/settings/connections", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bearer.DeleteAsync("/api/me/connections/x", Ct)).StatusCode);
    }

    [Fact]
    public async Task ErasingAPersonDeletesTheirConnections()
    {
        var id = await AddPersonAsync("conn-erase@example.com");
        await _flow.TokenAsync(await _flow.AddAppAsync("Erased"), "conn-erase@example.com");
        await Run(db => db.People.Where(p => p.Id == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.EndDate, new DateOnly(2020, 1, 1)), Ct));
        var ann = await TestLogin.SignInAsync(_factory, _mail, Ann);

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(ann, HttpMethod.Post, $"/api/people/{id}/erase")).StatusCode);

        Assert.Equal(0, await CountAsync(id));
    }

    [Fact]
    public async Task DeletingAPersonDeletesTheirConnections()
    {
        var id = await AddPersonAsync("conn-delete@example.com");
        await _flow.TokenAsync(await _flow.AddAppAsync("Gone"), "conn-delete@example.com");
        var ann = await TestLogin.SignInAsync(_factory, _mail, Ann);

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(ann, HttpMethod.Delete, $"/api/people/{id}")).StatusCode);

        Assert.Equal(0, await CountAsync(id));
    }

    [Fact]
    public async Task TheListShowsCanChangeAndTheApiTokenOfARevokedConnectionFails()
    {
        var app = await _flow.AddAppAsync("Writer");
        var access = await _flow.TokenAsync(app, Ann, OAuthFlow.Api, canChange: true);
        await _flow.TokenAsync(await _flow.AddAppAsync("Reader"), Ann);
        var ann = await TestLogin.SignInAsync(_factory, _mail, Ann);

        var list = await ListAsync(ann, "/api/me/connections");

        Assert.True(list.Single(c => c.GetProperty("connectedApp").GetString() == "Writer").GetProperty("canChange").GetBoolean());
        Assert.False(list.Single(c => c.GetProperty("connectedApp").GetString() == "Reader").GetProperty("canChange").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await _flow.BearerClient(access).GetAsync("/api/me", Ct)).StatusCode);
        var id = list.Single(c => c.GetProperty("connectedApp").GetString() == "Writer").GetProperty("id").GetString();
        await SendAsync(ann, HttpMethod.Delete, "/api/me/connections/" + id);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _flow.BearerClient(access).GetAsync("/api/me", Ct)).StatusCode);
    }
}
