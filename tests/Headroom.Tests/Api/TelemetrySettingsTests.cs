using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Memory;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Headroom.Client.Api;
using Headroom.Data;
using Headroom.Telemetry;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Headroom.Tests.Api;

public class TelemetryPrivacyTests
{
    [Theory]
    [InlineData(null, null, true)]
    [InlineData(null, true, true)]
    [InlineData(null, false, false)]
    [InlineData(true, null, true)]
    [InlineData(true, false, true)]
    [InlineData(false, null, false)]
    [InlineData(false, true, false)]
    public void FollowsTheOrder(bool? stored, bool? environment, bool expected) =>
        Assert.Equal(expected, TelemetryPrivacy.Effective(stored, environment));

    [Fact]
    public async Task SendsNeitherValueWhenTheDatabaseCannotBeRead()
    {
        // Port 1: nothing listens, so the read fails at once.
        var options = new DbContextOptionsBuilder<HeadroomDbContext>().UseNpgsql("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1").Options;
        await using var db = new HeadroomDbContext(options, TimeProvider.System);
        var privacy = new TelemetryPrivacy(new MemoryCache(new MemoryCacheOptions()), new ConfigurationBuilder().Build());

        Assert.Equal((false, false), await privacy.GetAsync(db, TestContext.Current.CancellationToken));
    }
}

public class TelemetrySettingsTests(HeadroomApp app) : IClassFixture<HeadroomApp>, IAsyncLifetime
{
    private const string Url = "/api/settings/telemetry";
    private const string Ann = "telemetrysettings-ann@example.com";
    private readonly WebApplicationFactory<Program> _factory = app.Factory;

    public async ValueTask InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
        if (!db.People.Any())
        {
            var ann = new Person { Name = "Ann", Email = Ann, IsPlanner = true };
            ann.LastChangedBy = ann.Id;
            db.InstanceSettings.Add(new InstanceSettings { CompanyName = "Acme", LastChangedBy = ann.Id });
            db.People.Add(ann);
            db.People.Add(new Person { Name = "Flo", Email = "telemetrysettings-flo@example.com", LastChangedBy = ann.Id });
            await db.SaveChangesAsync();
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task NonPlannerGets403OnGetAndPut()
    {
        var client = await TestLogin.SignInAsync(_factory, app.Mail, "telemetrysettings-flo@example.com");
        var token = await TestLogin.ApiTokenAsync(client);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Url, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Put(client, token, true, true)).StatusCode);
    }

    [Fact]
    public async Task AnAccessTokenOfAPlannerNeverReachesTheTelemetrySettings()
    {
        var client = await TestLogin.SignInAsync(_factory, app.Mail, Ann);
        var create = new HttpRequestMessage(HttpMethod.Post, "/api/me/access-tokens") { Content = JsonContent.Create(new AccessTokenRequest("telemetry", CanChange: true)) };
        create.Headers.Add(AntiforgeryToken.HeaderName, await TestLogin.ApiTokenAsync(client));
        var created = await client.SendAsync(create, TestContext.Current.CancellationToken);
        var secret = (await created.Content.ReadFromJsonAsync<NewAccessTokenResponse>(TestContext.Current.CancellationToken))!.Secret;
        var bearer = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        bearer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);

        Assert.Equal(HttpStatusCode.Forbidden, (await bearer.GetAsync(Url, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bearer.PutAsJsonAsync(Url, new TelemetrySettingsRequest(false, false), TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task PlannerReadsTheDefaultsAndSavesBothValues()
    {
        var client = await TestLogin.SignInAsync(_factory, app.Mail, Ann);
        var token = await TestLogin.ApiTokenAsync(client);
        var before = await client.GetFromJsonAsync<TelemetrySettingsResponse>(Url, TestContext.Current.CancellationToken);
        Assert.Equal(new TelemetrySettingsResponse(true, true, false), before);

        Assert.Equal(HttpStatusCode.NoContent, (await Put(client, token, false, true)).StatusCode);

        var after = await client.GetFromJsonAsync<TelemetrySettingsResponse>(Url, TestContext.Current.CancellationToken);
        Assert.Equal(new TelemetrySettingsResponse(false, true, false), after);
        using var scope = _factory.Services.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<HeadroomDbContext>().InstanceSettings.AsNoTracking().FirstAsync(TestContext.Current.CancellationToken);
        Assert.Equal((false, true), (row.TelemetryEndUserId, row.TelemetryClientAddress));
        Assert.NotNull(row.LastChangedBy);
    }

    private static Task<HttpResponseMessage> Put(HttpClient client, string token, bool sendPersonId, bool sendClientAddress)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, Url) { Content = JsonContent.Create(new TelemetrySettingsRequest(sendPersonId, sendClientAddress)) };
        request.Headers.Add(AntiforgeryToken.HeaderName, token);
        return client.SendAsync(request);
    }
}
