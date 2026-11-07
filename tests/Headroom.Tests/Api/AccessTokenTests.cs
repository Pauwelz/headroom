using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Headroom.Auth;
using Headroom.Client.Api;
using Headroom.Data;
using Headroom.Email;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Headroom.Tests.Api;

public class AccessTokenTests : IClassFixture<HeadroomApp>, IAsyncLifetime
{
    private const string Url = "/api/settings/access-tokens";
    private const string OwnUrl = "/api/me/access-tokens";
    private readonly FakeEmailSender _mail = new();
    private readonly WebApplicationFactory<Program> _factory;

    public AccessTokenTests(HeadroomApp app)
    {
        _factory = app.Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<IEmailSender>(_mail)));
    }

    public async ValueTask InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
        if (db.People.Any()) return;
        var ann = new Person { Name = "Ann", Email = "token-ann@example.com", IsPlanner = true };
        ann.LastChangedBy = ann.Id;
        db.InstanceSettings.Add(new InstanceSettings { CompanyName = "Acme", LastChangedBy = ann.Id });
        db.People.Add(ann);
        db.People.Add(new Person { Name = "Flo", Email = "token-flo@example.com", LastChangedBy = ann.Id });
        db.People.Add(new Person { Name = "Gus", Email = "token-gus@example.com", EndDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-3), LastChangedBy = ann.Id });
        db.People.Add(new Person { Name = "Hal", Email = "token-hal@example.com", EndDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-3), LastChangedBy = ann.Id });
        db.People.Add(new Person { Name = "Ivy", Email = "token-ivy@example.com", IsPlanner = true, LastChangedBy = ann.Id });
        db.People.Add(new Person { Name = "Jon", Email = "token-jon@example.com", IsPlanner = true, LastChangedBy = ann.Id });
        await db.SaveChangesAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task PersonCreatesATokenThatShowsOnceAndStoresOnlyAHash()
    {
        var (flo, xsrf) = await SignInAsync("token-flo@example.com");

        var created = await CreateAsync(flo, xsrf, "Claude");

        Assert.Equal(("Claude", "Flo"), (created.Name, created.Owner));
        var list = (await flo.GetFromJsonAsync<List<AccessTokenResponse>>(OwnUrl, cancellationToken: TestContext.Current.CancellationToken))!;
        Assert.Contains(list, t => t.Id == created.Id && t.Name == "Claude");
        Assert.DoesNotContain(created.Secret, await flo.GetStringAsync(OwnUrl, cancellationToken: TestContext.Current.CancellationToken));
        using var scope = _factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<HeadroomDbContext>().AccessTokens.SingleAsync(t => t.Id == created.Id, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(32, stored.TokenHash.Length);
    }

    [Fact]
    public async Task PersonSeesOnlyOwnTokensAndRevokesOnlyOwnTokens()
    {
        var (ann, annXsrf) = await PlannerAsync();
        var (flo, floXsrf) = await SignInAsync("token-flo@example.com");
        var annsToken = await CreateAsync(ann, annXsrf, "Ann only");

        var list = (await flo.GetFromJsonAsync<List<AccessTokenResponse>>(OwnUrl, cancellationToken: TestContext.Current.CancellationToken))!;

        Assert.DoesNotContain(list, t => t.Id == annsToken.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(flo, floXsrf, HttpMethod.Delete, $"{OwnUrl}/{annsToken.Id}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await BearerClient(annsToken.Secret).GetAsync("/api/instance", cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task PlannerListsAllTokensWithOwnerAndRevokesAnyOfThem()
    {
        var (ann, annXsrf) = await PlannerAsync();
        var (flo, floXsrf) = await SignInAsync("token-flo@example.com");
        var floToken = await CreateAsync(flo, floXsrf, "Flo's");

        var list = (await ann.GetFromJsonAsync<List<AccessTokenResponse>>(Url, cancellationToken: TestContext.Current.CancellationToken))!;

        Assert.Contains(list, t => t.Id == floToken.Id && t.Owner == "Flo");
        Assert.DoesNotContain(floToken.Secret, await ann.GetStringAsync(Url, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.NoContent, (await Send(ann, annXsrf, HttpMethod.Delete, $"{Url}/{floToken.Id}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await BearerClient(floToken.Secret).GetAsync("/api/instance", cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task NonPlannerCannotUseThePlannerList()
    {
        var (flo, xsrf) = await SignInAsync("token-flo@example.com");

        Assert.Equal(HttpStatusCode.Forbidden, (await flo.GetAsync(Url, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(flo, xsrf, HttpMethod.Delete, $"{Url}/{Guid.NewGuid()}", null)).StatusCode);
    }

    [Fact]
    public async Task TokenActsAsItsPersonAndReadsWhatThePersonCanRead()
    {
        var (ann, annXsrf) = await PlannerAsync();
        var (flo, floXsrf) = await SignInAsync("token-flo@example.com");
        var planner = BearerClient((await CreateAsync(ann, annXsrf, "Planner")).Secret);
        var person = BearerClient((await CreateAsync(flo, floXsrf, "Person")).Secret);

        foreach (var url in new[] { "/api/schedule?from=2026-03-02&to=2026-03-08", "/api/utilisation?from=2026-03-02&to=2026-03-08&period=week",
                     "/api/clients", "/api/projects/mine", "/api/instance" })
            Assert.Equal(HttpStatusCode.OK, (await person.GetAsync(url, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await planner.GetAsync("/api/people", cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await person.GetAsync("/api/people", cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal("Flo", (await person.GetFromJsonAsync<MeResponse>("/api/me", cancellationToken: TestContext.Current.CancellationToken))!.Name);
    }

    [Fact]
    public async Task TokenIsRefusedOnEveryWriteAndOnEveryTokenEndpoint()
    {
        var (ann, annXsrf) = await PlannerAsync();
        var bearer = BearerClient((await CreateAsync(ann, annXsrf, "Limited")).Secret);
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Forbidden, (await bearer.PostAsJsonAsync("/api/clients", new { name = "X" }, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bearer.DeleteAsync($"/api/clients/{id}", cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bearer.PostAsJsonAsync(OwnUrl, new AccessTokenRequest("x"), cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bearer.DeleteAsync($"{OwnUrl}/{id}", cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bearer.DeleteAsync($"{Url}/{id}", cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bearer.GetAsync(OwnUrl, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bearer.GetAsync(Url, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task RevokedAndUnknownTokensGet401()
    {
        var (flo, xsrf) = await SignInAsync("token-flo@example.com");
        var created = await CreateAsync(flo, xsrf, "Short lived");
        var bearer = BearerClient(created.Secret);
        Assert.Equal(HttpStatusCode.OK, (await bearer.GetAsync("/api/instance", cancellationToken: TestContext.Current.CancellationToken)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await Send(flo, xsrf, HttpMethod.Delete, $"{OwnUrl}/{created.Id}", null)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await bearer.GetAsync("/api/instance", cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await BearerClient("hr_nope").GetAsync("/api/instance", cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task TokenOfAPersonPastTheirEndDateGets401()
    {
        var secret = await SeedTokenAsync("token-gus@example.com");

        Assert.Equal(HttpStatusCode.Unauthorized, (await BearerClient(secret).GetAsync("/api/instance", cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
    }

    [Theory]
    [InlineData("30d", 30)]
    [InlineData("90d", 90)]
    [InlineData("1y", 365)]
    [InlineData("never", 0)]
    public async Task ServerStoresTheChosenExpiryAndListsIt(string choice, int days)
    {
        var (flo, xsrf) = await SignInAsync("token-flo@example.com");

        var response = await Send(flo, xsrf, HttpMethod.Post, OwnUrl, new AccessTokenRequest("Expiring", choice));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<NewAccessTokenResponse>(TestContext.Current.CancellationToken))!;
        var listed = (await flo.GetFromJsonAsync<List<AccessTokenResponse>>(OwnUrl, cancellationToken: TestContext.Current.CancellationToken))!.Single(t => t.Id == created.Id);
        if (days == 0) Assert.Null(listed.ExpiresAt);
        else
        {
            Assert.Equal(created.ExpiresAt!.Value, listed.ExpiresAt!.Value, TimeSpan.FromMilliseconds(1)); // PostgreSQL stores microseconds
            Assert.InRange(listed.ExpiresAt!.Value, DateTimeOffset.UtcNow.AddDays(days).AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(days).AddMinutes(1));
        }
    }

    [Fact]
    public async Task OmittedExpiryDefaultsTo90Days()
    {
        var (flo, xsrf) = await SignInAsync("token-flo@example.com");

        var created = await CreateAsync(flo, xsrf, "Default");

        Assert.InRange(created.ExpiresAt!.Value, DateTimeOffset.UtcNow.AddDays(90).AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(90).AddMinutes(1));
    }

    [Fact]
    public async Task AnyOtherExpiryChoiceIsRefusedWithAProblem()
    {
        var (flo, xsrf) = await SignInAsync("token-flo@example.com");

        var response = await Send(flo, xsrf, HttpMethod.Post, OwnUrl, new AccessTokenRequest("Odd", "7d"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("30 days, 90 days, 1 year or Never", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExpiredTokenGets401ButStaysInTheLists()
    {
        var (ann, _) = await PlannerAsync();
        var (flo, _) = await SignInAsync("token-flo@example.com");
        var secret = await SeedTokenAsync("token-flo@example.com", DateTimeOffset.UtcNow.AddMinutes(-1));

        Assert.Equal(HttpStatusCode.Unauthorized, (await BearerClient(secret).GetAsync("/api/instance", cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        foreach (var (client, url) in new[] { (flo, OwnUrl), (ann, Url) })
        {
            var list = (await client.GetFromJsonAsync<List<AccessTokenResponse>>(url, cancellationToken: TestContext.Current.CancellationToken))!;
            Assert.Contains(list, t => t.Owner == "Flo" && t.ExpiresAt < DateTimeOffset.UtcNow);
        }
    }

    [Fact]
    public async Task ErasingAPersonDeletesTheirTokens()
    {
        var secret = await SeedTokenAsync("token-hal@example.com");
        var (ann, xsrf) = await PlannerAsync();
        var hal = (await ann.GetFromJsonAsync<List<PersonResponse>>("/api/people", cancellationToken: TestContext.Current.CancellationToken))!.Single(p => p.Name == "Hal");

        Assert.Equal(HttpStatusCode.OK, (await Send(ann, xsrf, HttpMethod.Post, $"/api/people/{hal.Id}/erase", null)).StatusCode);

        using var scope = _factory.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<HeadroomDbContext>().AccessTokens.AnyAsync(t => t.PersonId == hal.Id, TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.Unauthorized, (await BearerClient(secret).GetAsync("/api/instance", cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task TokenFollowsTheCurrentPlannerFlagOfItsPerson()
    {
        var bearer = BearerClient(await SeedTokenAsync("token-ivy@example.com"));
        Assert.Equal(HttpStatusCode.OK, (await bearer.GetAsync("/api/people", cancellationToken: TestContext.Current.CancellationToken)).StatusCode);

        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<HeadroomDbContext>().People
                .Where(p => p.Email == "token-ivy@example.com").ExecuteUpdateAsync(s => s.SetProperty(p => p.IsPlanner, false), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, (await bearer.GetAsync("/api/people", cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task PlannerCreatesATokenThatCanChangeAndTheListsShowIt()
    {
        var (ann, xsrf) = await PlannerAsync();

        var created = await CreateAsync(ann, xsrf, "Writer", canChange: true);
        var readOnly = await CreateAsync(ann, xsrf, "Reader");

        Assert.True(created.CanChange);
        Assert.False(readOnly.CanChange);
        var own = (await ann.GetFromJsonAsync<List<AccessTokenResponse>>(OwnUrl, cancellationToken: TestContext.Current.CancellationToken))!;
        Assert.True(own.Single(t => t.Id == created.Id).CanChange);
        Assert.False(own.Single(t => t.Id == readOnly.Id).CanChange);
        var all = (await ann.GetFromJsonAsync<List<AccessTokenResponse>>(Url, cancellationToken: TestContext.Current.CancellationToken))!;
        Assert.True(all.Single(t => t.Id == created.Id).CanChange);
    }

    [Fact]
    public async Task PersonWhoIsNotAPlannerCannotAskForCanChange()
    {
        var (flo, xsrf) = await SignInAsync("token-flo@example.com");

        var response = await Send(flo, xsrf, HttpMethod.Post, OwnUrl, new AccessTokenRequest("Nope", CanChange: true));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReadOnlyPlannerTokenGets403OnChangesAndStillReadsThePeopleList()
    {
        var (ann, xsrf) = await PlannerAsync();
        var bearer = BearerClient((await CreateAsync(ann, xsrf, "Read only")).Secret);

        Assert.Equal(HttpStatusCode.Forbidden, (await bearer.PostAsJsonAsync("/api/clients", new ClientRequest("X"), cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bearer.PutAsJsonAsync($"/api/allocations/{Guid.NewGuid()}", new { }, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bearer.DeleteAsync($"/api/allocations/{Guid.NewGuid()}", cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bearer.GetAsync("/api/people", cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task PlannerTokenThatCanChangeCreatesAnAllocationRecordedForThePlanner()
    {
        var (ann, xsrf) = await PlannerAsync();
        var bearer = BearerClient((await CreateAsync(ann, xsrf, "Writer", canChange: true)).Secret);
        var clientId = (await (await Send(ann, xsrf, HttpMethod.Post, "/api/clients", new ClientRequest("Token client " + Guid.NewGuid())))
            .Content.ReadFromJsonAsync<ClientResponse>(TestContext.Current.CancellationToken))!.Id;
        var projectId = (await (await Send(ann, xsrf, HttpMethod.Post, $"/api/clients/{clientId}/projects", new CreateProjectRequest("Token project", "confirmed")))
            .Content.ReadFromJsonAsync<ProjectResponse>(TestContext.Current.CancellationToken))!.Id;
        Guid annId, floId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
            annId = (await db.People.SingleAsync(p => p.Email == "token-ann@example.com", TestContext.Current.CancellationToken)).Id;
            floId = (await db.People.SingleAsync(p => p.Email == "token-flo@example.com", TestContext.Current.CancellationToken)).Id;
        }
        Assert.Equal(HttpStatusCode.OK, (await Send(ann, xsrf, HttpMethod.Post, "/api/settings/webhooks", new WebhookRequest("Hook", "https://receiver.example.com/hook"))).StatusCode);

        var response = await bearer.PostAsJsonAsync("/api/allocations",
            new CreateAllocationRequest(floId, projectId, new(2026, 3, 2), new(2026, 3, 2), 50), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var check = _factory.Services.CreateScope();
        var checkDb = check.ServiceProvider.GetRequiredService<HeadroomDbContext>();
        Assert.Equal(annId, (await checkDb.Allocations.SingleAsync(a => a.ProjectId == projectId, TestContext.Current.CancellationToken)).LastChangedBy);
        Assert.Contains(await checkDb.Events.Select(e => e.Payload).ToListAsync(TestContext.Current.CancellationToken),
            payload => payload.Contains("headroom.Allocation.created") && payload.Contains(annId.ToString()));
    }

    [Fact]
    public async Task TokenThatCanChangeFailsOnItsNextChangeWhenThePersonLosesThePlannerRole()
    {
        var (ivy, xsrf) = await SignInAsync("token-jon@example.com");
        var bearer = BearerClient((await CreateAsync(ivy, xsrf, "Ivy writer", canChange: true)).Secret);
        Assert.Equal(HttpStatusCode.Created, (await bearer.PostAsJsonAsync("/api/clients", new ClientRequest("Ivy client " + Guid.NewGuid()), cancellationToken: TestContext.Current.CancellationToken)).StatusCode);

        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<HeadroomDbContext>().People
                .Where(p => p.Email == "token-jon@example.com").ExecuteUpdateAsync(s => s.SetProperty(p => p.IsPlanner, false), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, (await bearer.PostAsJsonAsync("/api/clients", new ClientRequest("Too late"), cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<HeadroomDbContext>().People
                .Where(p => p.Email == "token-jon@example.com").ExecuteUpdateAsync(s => s.SetProperty(p => p.IsPlanner, true), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CookieWritesStillNeedTheAntiforgeryToken()
    {
        var (flo, _) = await SignInAsync("token-flo@example.com");

        var response = await flo.PostAsJsonAsync(OwnUrl, new AccessTokenRequest("No header"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<string> SeedTokenAsync(string email, DateTimeOffset? expiresAt = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
        var secret = AccessTokenAuth.NewSecret();
        db.AccessTokens.Add(new AccessToken
        {
            Name = "Seeded", ExpiresAt = expiresAt, TokenHash = AccessTokenAuth.Hash(secret),
            PersonId = (await db.People.SingleAsync(p => p.Email == email)).Id,
        });
        await db.SaveChangesAsync();
        return secret;
    }

    private async Task<(HttpClient Client, string Xsrf)> SignInAsync(string email)
    {
        var client = await TestLogin.SignInAsync(_factory, _mail, email);
        return (client, await TestLogin.ApiTokenAsync(client));
    }

    private Task<(HttpClient Client, string Xsrf)> PlannerAsync() => SignInAsync("token-ann@example.com");

    private HttpClient BearerClient(string secret)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
            { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        return client;
    }

    private static async Task<NewAccessTokenResponse> CreateAsync(HttpClient client, string xsrf, string name, bool canChange = false)
    {
        var response = await Send(client, xsrf, HttpMethod.Post, OwnUrl, new AccessTokenRequest(name, CanChange: canChange));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<NewAccessTokenResponse>())!;
    }

    private static Task<HttpResponseMessage> Send(HttpClient client, string xsrf, HttpMethod method, string url, object? body)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(Headroom.Client.Api.AntiforgeryToken.HeaderName, xsrf);
        if (body is not null) request.Content = JsonContent.Create(body);
        return client.SendAsync(request);
    }
}
