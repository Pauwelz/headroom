using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Headroom.Client.Api;
using Headroom.Data;
using Headroom.Endpoints;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Headroom.Tests.Api;

// The Email server step of the first-run screen. Each test needs an empty database, so each starts its own app.
public class FirstRunServerApp(string environment, string? smtpHost = null) : HeadroomApp
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        if (smtpHost is not null) builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Smtp:Host"] = smtpHost }));
        builder.ConfigureServices(s => s.AddSingleton(new EmailTestTimeout(TimeSpan.FromMilliseconds(300))));
    }
}

public class FirstRunServerTests
{
    private const string Email = "ann@example.com";

    [Fact]
    public async Task TheCodeGoesThroughTheEnteredServerAndSettingsShowItAfterSetup()
    {
        await using var app = await StartAsync(new("Production"));
        var client = Client(app);
        var html = await Html(await client.GetAsync("/first-run", TestContext.Current.CancellationToken));
        Assert.Contains("Email server", html);

        var step = await client.PostAsync("/first-run", Form(Token(html), ("company", "Acme"), ("name", "Ann"), ("email", Email)), TestContext.Current.CancellationToken);
        html = await Html(step);
        Assert.Contains("name=\"host\"", html);
        Assert.Empty(app.Mail.Sent); // no code before the server is known

        var code = await client.PostAsync("/first-run/server", Form(Token(html), ("company", "Acme"), ("name", "Ann"), ("email", Email),
            ("host", "smtp.example.com"), ("port", "465"), ("username", "user"), ("password", "plain-secret"), ("from", "headroom@example.com")), TestContext.Current.CancellationToken);
        html = await Html(code);
        Assert.Equal(HttpStatusCode.OK, code.StatusCode);
        Assert.DoesNotContain("plain-secret", html);
        var server = app.Mail.CodeServers[Email]!;
        Assert.Equal(("smtp.example.com", 465, "user", "plain-secret", "headroom@example.com"), (server.Host, server.Port, server.Username, server.Password, server.FromAddress));

        var done = await client.PostAsync("/first-run/verify", Form(Token(html), Hidden(html, "company", "name", "email", "server"),
            ("code", app.Mail.Sent.Single().Code)), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, done.StatusCode);

        var stored = await Stored(app);
        Assert.Equal("smtp.example.com", stored.SmtpHost);
        Assert.NotNull(stored.SmtpPassword);
        Assert.DoesNotContain("plain-secret", stored.SmtpPassword);
        Assert.NotEqual("plain-secret", stored.SmtpPassword);
        var get = await client.GetFromJsonAsync<EmailServerResponse>("/api/settings/email-server", TestContext.Current.CancellationToken);
        Assert.Equal(("smtp.example.com", 465, "user", true), (get!.Host, get.Port, get.Username, get.HasPassword));
    }

    [Fact]
    public async Task AFailedSendKeepsYouOnTheStepWithoutTheReplyOrThePassword()
    {
        await using var app = await StartAsync(new("Production"));
        app.Mail.CodeFails = true;
        var client = Client(app);
        var token = Token(await Html(await client.GetAsync("/first-run", TestContext.Current.CancellationToken)));

        var response = await client.PostAsync("/first-run/server", Form(token, ("company", "Acme"), ("name", "Ann"), ("email", Email),
            ("host", "smtp.example.com"), ("port", "587"), ("username", "user"), ("password", "plain-secret"), ("from", "headroom@example.com")), TestContext.Current.CancellationToken);

        var html = await Html(response);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Could not connect to the email server.", html);
        Assert.DoesNotContain("secret@example.com", html);
        Assert.DoesNotContain("plain-secret", html);
        Assert.Contains("value=\"smtp.example.com\"", html);
        Assert.Empty(await Settings(app));
    }

    [Fact]
    public async Task ATestEmailThatHangsGivesTheSameError()
    {
        await using var app = await StartAsync(new("Production"));
        app.Mail.CodeHangs = true;
        var client = Client(app);
        var token = Token(await Html(await client.GetAsync("/first-run", TestContext.Current.CancellationToken)));

        var response = await client.PostAsync("/first-run/server", Form(token, ("company", "Acme"), ("name", "Ann"), ("email", Email),
            ("host", "h"), ("port", "587"), ("from", "headroom@example.com")), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Could not connect to the email server.", await Html(response));
        Assert.Empty(await Settings(app));
    }

    [Fact]
    public async Task TheStepUsesTheRulesOfSettings()
    {
        await using var app = await StartAsync(new("Production"));
        var client = Client(app);
        var token = Token(await Html(await client.GetAsync("/first-run", TestContext.Current.CancellationToken)));

        var response = await client.PostAsync("/first-run/server", Form(token, ("company", "Acme"), ("name", "Ann"), ("email", Email),
            ("host", "h"), ("port", "70000"), ("from", "headroom@example.com")), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Enter a port from 1 to 65535.", await Html(response));
        Assert.Empty(app.Mail.Sent);
    }

    [Fact]
    public async Task AChangedOrExpiredPasswordFieldSendsYouBackWithoutUsingTheCode()
    {
        await using var app = await StartAsync(new("Production"));
        var client = Client(app);
        var token = Token(await Html(await client.GetAsync("/first-run", TestContext.Current.CancellationToken)));

        var response = await client.PostAsync("/first-run/verify", Form(token, ("company", "Acme"), ("name", "Ann"), ("email", Email),
            ("server", "not-protected"), ("code", "123456")), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("expired", await Html(response));
        Assert.Empty(await Settings(app));
    }

    [Fact]
    public async Task ChangedHiddenFieldsCannotChangeTheStoredServer()
    {
        await using var app = await StartAsync(new("Production"));
        var client = Client(app);
        var token = Token(await Html(await client.GetAsync("/first-run", TestContext.Current.CancellationToken)));
        var code = await client.PostAsync("/first-run/server", Form(token, ("company", "Acme"), ("name", "Ann"), ("email", Email),
            ("host", "smtp.example.com"), ("port", "587"), ("username", "user"), ("password", "plain-secret"), ("from", "headroom@example.com")), TestContext.Current.CancellationToken);
        var html = await Html(code);
        Assert.DoesNotContain("name=\"host\"", html);

        var done = await client.PostAsync("/first-run/verify", Form(Token(html), Hidden(html, "company", "name", "email", "server"),
            ("host", "evil.example.com"), ("port", "25"), ("username", "evil"), ("password", "evil"), ("from", "evil@example.com"),
            ("code", app.Mail.Sent.Single().Code)), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, done.StatusCode);
        var stored = await Stored(app);
        Assert.Equal(("smtp.example.com", 587, "user", "headroom@example.com"), (stored.SmtpHost, stored.SmtpPort, stored.SmtpUsername, stored.SmtpFromAddress));
    }

    [Fact]
    public async Task TheStepIsSkippedWhenSmtpHostIsSetAndNothingIsStored()
    {
        await using var app = await StartAsync(new("Production", "smtp.env"));
        var client = Client(app);
        var html = await Html(await client.GetAsync("/first-run", TestContext.Current.CancellationToken));
        Assert.DoesNotContain("Email server", html);

        var sent = await client.PostAsync("/first-run", Form(Token(html), ("company", "Acme"), ("name", "Ann"), ("email", Email)), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        Assert.Null(app.Mail.CodeServers[Email]);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/first-run/server", Form(Token(html), ("email", Email)), TestContext.Current.CancellationToken)).StatusCode);

        // Posted server fields are ignored: the Operator's settings apply.
        var done = await client.PostAsync("/first-run/verify", Form(Token(await Html(sent)), ("company", "Acme"), ("name", "Ann"), ("email", Email),
            ("host", "evil.example.com"), ("port", "25"), ("from", "x@example.com"), ("code", app.Mail.Sent.Single().Code)), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, done.StatusCode);
        Assert.Null((await Stored(app)).SmtpHost);
    }

    [Fact]
    public async Task TheStepIsSkippedInDevelopment()
    {
        await using var app = await StartAsync(new("Development"));
        var client = Client(app);
        var html = await Html(await client.GetAsync("/first-run", TestContext.Current.CancellationToken));
        Assert.DoesNotContain("Email server", html);

        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/first-run/server", Form(Token(html), ("email", Email)), TestContext.Current.CancellationToken)).StatusCode);
    }

    private static async Task<FirstRunServerApp> StartAsync(FirstRunServerApp app)
    {
        await app.InitializeAsync();
        return app;
    }

    private static HttpClient Client(HeadroomApp app) =>
        app.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

    private static async Task<InstanceSettings> Stored(HeadroomApp app)
    {
        using var scope = app.Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<HeadroomDbContext>().InstanceSettings.AsNoTracking().SingleAsync();
    }

    private static async Task<List<InstanceSettings>> Settings(HeadroomApp app)
    {
        using var scope = app.Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<HeadroomDbContext>().InstanceSettings.AsNoTracking().ToListAsync();
    }

    private static async Task<string> Html(HttpResponseMessage response) => await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

    private static string Token(string html) => Regex.Match(html, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"").Groups[1].Value;

    private static FormUrlEncodedContent Form(string token, params (string, string)[] fields) =>
        new(new[] { ("__RequestVerificationToken", token) }.Concat(fields).Select(f => KeyValuePair.Create(f.Item1, f.Item2)));

    private static FormUrlEncodedContent Form(string token, (string, string)[] hidden, params (string, string)[] fields) => Form(token, hidden.Concat(fields).ToArray());

    // The hidden fields of the Code page, as the browser would post them.
    private static (string, string)[] Hidden(string html, params string[] names) =>
        names.Select(n => (n, System.Net.WebUtility.HtmlDecode(Regex.Match(html, $"name=\"{n}\" value=\"([^\"]*)\"").Groups[1].Value))).ToArray();
}
