using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Headroom.Client.Api;
using Headroom.Data;
using Headroom.Email;
using Headroom.Endpoints;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Headroom.Tests.Api;

public class EmailServerApp : HeadroomApp
{
    // A short limit, so that a hanging test email ends fast.
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureServices(services => services.AddSingleton(new EmailTestTimeout(TimeSpan.FromMilliseconds(300))));
}

public class EmailServerResolveTests
{
    [Theory]
    [InlineData(true, "smtp.env", true, EmailServer.Source.Database)]
    [InlineData(true, null, false, EmailServer.Source.Database)]
    [InlineData(false, "smtp.env", false, EmailServer.Source.Environment)]
    [InlineData(false, "smtp.env", true, EmailServer.Source.Environment)] // the local preview sends to Mailpit
    [InlineData(false, "", true, EmailServer.Source.Console)]
    [InlineData(false, null, true, EmailServer.Source.Console)]
    public void FollowsTheOrder(bool hasStored, string? environmentHost, bool development, EmailServer.Source expected) =>
        Assert.Equal(expected, EmailServer.Resolve(hasStored, environmentHost, development));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void ThrowsWithoutAnyServerOutsideDevelopment(string? environmentHost) =>
        Assert.Throws<InvalidOperationException>(() => EmailServer.Resolve(false, environmentHost, false));
}

public class EmailServerTests : IClassFixture<EmailServerApp>, IAsyncLifetime
{
    private const string Url = "/api/settings/email-server";
    private const string Ann = "emailserver-ann@example.com";
    private readonly FakeEmailSender _mail;
    private readonly WebApplicationFactory<Program> _factory;

    public EmailServerTests(EmailServerApp app)
    {
        _factory = app.Factory;
        _mail = app.Mail;
        _connectionString = app.ConnectionString;
    }

    private readonly string _connectionString;

    private static readonly EmailServerRequest Valid = new("smtp.example.com", 587, "user", "secret-pw", "headroom@example.com");

    // The tests share one settings row, so each test starts with nothing stored.
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
            db.People.Add(new Person { Name = "Flo", Email = "emailserver-flo@example.com", LastChangedBy = ann.Id });
            await db.SaveChangesAsync();
        }
        await db.InstanceSettings.ExecuteUpdateAsync(s => s
            .SetProperty(i => i.SmtpHost, (string?)null).SetProperty(i => i.SmtpPort, (int?)null)
            .SetProperty(i => i.SmtpUsername, (string?)null).SetProperty(i => i.SmtpPassword, (string?)null)
            .SetProperty(i => i.SmtpFromAddress, (string?)null));
        _mail.TestSent.Clear();
        _mail.TestFails = false;
        _mail.TestHangs = false;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task DevelopmentWithoutAnyServerLogsTheCodeButNotTheAddress()
    {
        using var scope = _factory.Services.CreateScope();
        var log = new CapturingLogger();
        var sender = new EmailSender(
            scope.ServiceProvider.GetRequiredService<HeadroomDbContext>(), Microsoft.Extensions.Options.Options.Create(new SmtpOptions()),
            new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = "Development" },
            scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>(), log);

        await sender.SendLoginCodeAsync("someone@example.com", "Acme", "123456", TestContext.Current.CancellationToken);

        var line = Assert.Single(log.Lines);
        Assert.Contains("123456", line);
        Assert.DoesNotContain("someone@example.com", line);
    }

    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger<EmailSender>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) => Lines.Add(formatter(state, exception));
    }

    [Fact]
    public async Task NonPlannerGets403OnGetAndPut()
    {
        var client = await TestLogin.SignInAsync(_factory, _mail, "emailserver-flo@example.com");
        var token = await TestLogin.ApiTokenAsync(client);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Url, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Put(client, token, Valid)).StatusCode);
    }

    [Fact]
    public async Task AnAccessTokenOfAPlannerNeverReachesTheEmailServer()
    {
        var (client, token) = await PlannerAsync();
        var create = new HttpRequestMessage(HttpMethod.Post, "/api/me/access-tokens") { Content = JsonContent.Create(new AccessTokenRequest("mail", CanChange: true)) };
        create.Headers.Add(Headroom.Client.Api.AntiforgeryToken.HeaderName, token);
        var created = await client.SendAsync(create, TestContext.Current.CancellationToken);
        var secret = (await created.Content.ReadFromJsonAsync<NewAccessTokenResponse>(TestContext.Current.CancellationToken))!.Secret;
        var bearer = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        bearer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);

        Assert.Equal(HttpStatusCode.Forbidden, (await bearer.GetAsync(Url, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bearer.PutAsJsonAsync(Url, Valid, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Empty(_mail.TestSent);
    }

    [Fact]
    public async Task PlannerSavesAfterATestEmailToThemselves()
    {
        var (client, token) = await PlannerAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await Put(client, token, Valid)).StatusCode);

        var sent = Assert.Single(_mail.TestSent);
        Assert.Equal(Ann, sent.To);
        Assert.Equal("secret-pw", sent.Server.Password);
        var stored = await StoredAsync();
        Assert.Equal("smtp.example.com", stored.SmtpHost);
        Assert.Equal(587, stored.SmtpPort);
        Assert.Equal("user", stored.SmtpUsername);
        Assert.Equal("headroom@example.com", stored.SmtpFromAddress);
        Assert.NotNull(stored.SmtpPassword);
        Assert.NotEqual("secret-pw", stored.SmtpPassword);
        Assert.DoesNotContain("secret-pw", stored.SmtpPassword);
        // The raw column, read without the model, holds ciphertext.
        await using (var connection = new Npgsql.NpgsqlConnection(_connectionString))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = new Npgsql.NpgsqlCommand("""SELECT "SmtpPassword" FROM "InstanceSettings" """, connection);
            var raw = (string)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
            Assert.NotEqual("secret-pw", raw);
            Assert.DoesNotContain("secret-pw", raw);
        }
        Assert.NotNull(stored.LastChangedBy);

        var response = await client.GetAsync(Url, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("secret-pw", body);
        Assert.DoesNotContain(stored.SmtpPassword, body);
        var get = System.Text.Json.JsonSerializer.Deserialize<EmailServerResponse>(body, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        Assert.Equal(new EmailServerResponse("smtp.example.com", 587, "user", "headroom@example.com", true, false), get);
    }

    [Fact]
    public async Task StoredSettingsServeTheStoredServerAndItsPassword()
    {
        var (client, token) = await PlannerAsync();
        await Put(client, token, Valid);

        using var scope = _factory.Services.CreateScope();
        var server = await EmailServer.ReadStoredAsync(
            scope.ServiceProvider.GetRequiredService<HeadroomDbContext>(),
            scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>(), TestContext.Current.CancellationToken);

        Assert.Equal("smtp.example.com", server!.Host);
        Assert.Equal("secret-pw", server.Password);
    }

    [Fact]
    public async Task AFailedTestEmailSavesNothingAndHidesTheReason()
    {
        var (client, token) = await PlannerAsync();
        await Put(client, token, Valid);
        var before = await StoredAsync();
        _mail.TestFails = true;

        var response = await Put(client, token, Valid with { Host = "other.example.com", Password = "new" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("Could not connect to the email server.", text);
        Assert.DoesNotContain("secret@example.com", text);
        var after = await StoredAsync();
        Assert.Equal(before.SmtpHost, after.SmtpHost);
        Assert.Equal(before.SmtpPassword, after.SmtpPassword);
    }

    [Fact]
    public async Task AFailedFirstSaveStoresNothing()
    {
        var (client, token) = await PlannerAsync();
        _mail.TestFails = true;

        Assert.Equal(HttpStatusCode.BadRequest, (await Put(client, token, Valid)).StatusCode);

        Assert.Null((await StoredAsync()).SmtpHost);
    }

    [Fact]
    public async Task ATestEmailThatHangsGivesTheSameError()
    {
        var (client, token) = await PlannerAsync();
        _mail.TestHangs = true;

        var response = await Put(client, token, Valid);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Could not connect to the email server.", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Null((await StoredAsync()).SmtpHost);
    }

    [Fact]
    public async Task AnEmptyPasswordKeepsTheStoredOneForTheSameServer()
    {
        var (client, token) = await PlannerAsync();
        await Put(client, token, Valid with { Password = "a" });
        _mail.TestSent.Clear();

        var response = await Put(client, token, Valid with { Password = "", FromAddress = "other@example.com" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("a", Assert.Single(_mail.TestSent).Server.Password);
        var stored = await StoredAsync();
        Assert.NotNull(stored.SmtpPassword);
        using var scope = _factory.Services.CreateScope();
        var kept = await EmailServer.ReadStoredAsync(
            scope.ServiceProvider.GetRequiredService<HeadroomDbContext>(),
            scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>(), TestContext.Current.CancellationToken);
        Assert.Equal("a", kept!.Password);
        Assert.Equal("other@example.com", stored.SmtpFromAddress);
    }

    [Theory]
    [InlineData("other.example.com", 587, "user")]
    [InlineData("smtp.example.com", 465, "user")]
    [InlineData("smtp.example.com", 587, "other")]
    public async Task AKeptPasswordNeverGoesToAnotherServer(string host, int port, string username)
    {
        var (client, token) = await PlannerAsync();
        await Put(client, token, Valid);
        _mail.TestSent.Clear();

        var response = await Put(client, token, new EmailServerRequest(host, port, username, null, "headroom@example.com"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Enter the password again for the new server.", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Empty(_mail.TestSent);
        Assert.Equal("smtp.example.com", (await StoredAsync()).SmtpHost);
    }

    [Fact]
    public async Task GetWithNothingStoredShowsDefaults()
    {
        var (client, _) = await PlannerAsync();
        var empty = await client.GetFromJsonAsync<EmailServerResponse>(Url, TestContext.Current.CancellationToken);
        Assert.Equal(new EmailServerResponse(null, 587, null, null, false, false), empty);
    }

    [Theory]
    [InlineData("", 587, "a@example.com")]
    [InlineData("h", 0, "a@example.com")]
    [InlineData("h", 65536, "a@example.com")]
    [InlineData("h", 587, "not-an-email")]
    [InlineData("h", 587, "")]
    public async Task InvalidInputIsRefused(string host, int port, string from)
    {
        var (client, token) = await PlannerAsync();

        var response = await Put(client, token, new EmailServerRequest(host, port, null, null, from));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_mail.TestSent);
    }

    [Fact]
    public async Task TooLongValuesAreRefused()
    {
        var (client, token) = await PlannerAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await Put(client, token, Valid with { Host = new string('h', 254) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Put(client, token, Valid with { Username = new string('u', 255) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Put(client, token, Valid with { Password = new string('p', 1001) })).StatusCode);
        Assert.Empty(_mail.TestSent);
    }

    private async Task<(HttpClient Client, string Token)> PlannerAsync()
    {
        var client = await TestLogin.SignInAsync(_factory, _mail, Ann);
        return (client, await TestLogin.ApiTokenAsync(client));
    }

    private async Task<InstanceSettings> StoredAsync()
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<HeadroomDbContext>().InstanceSettings.AsNoTracking().FirstAsync();
    }

    private static Task<HttpResponseMessage> Put(HttpClient client, string token, EmailServerRequest body)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, Url) { Content = JsonContent.Create(body) };
        request.Headers.Add(Headroom.Client.Api.AntiforgeryToken.HeaderName, token);
        return client.SendAsync(request);
    }
}
