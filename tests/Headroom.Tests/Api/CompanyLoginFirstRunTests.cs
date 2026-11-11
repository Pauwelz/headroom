using System.Net;
using System.Net.Http.Json;
using Headroom.Client.Api;
using Headroom.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Headroom.Tests.Api;

// The first-run screen with Company login. It needs an empty database, so the class has its own app.
public class CompanyLoginFirstRunTests(CompanyLoginApp app) : IClassFixture<CompanyLoginApp>
{
    [Fact]
    public async Task TheTestLoginCreatesTheFirstPlannerWithoutAnEmailServer()
    {
        var client = NewClient(app);
        var form = await client.GetStringAsync("/first-run", TestContext.Current.CancellationToken);
        Assert.Contains("Set up Company login instead", form);

        var step = await TestLogin.PostFrom(client, "/first-run", "/first-run/company-login", ("company", "Acme"));
        var page = await step.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("https://localhost/signin-oidc", page);

        await StartAsync(client, "Other", "https://idp.example.com");

        var callback = await app.Provider.LoginAsync(client, "/login/company/test",
            ("sub", "first"), ("email", "first@company.be"), ("email_verified", "true"), ("name", "First Planner"));

        Assert.Equal("/", callback.Headers.Location!.OriginalString);
        var me = await client.GetFromJsonAsync<MeResponse>("/api/me", TestContext.Current.CancellationToken);
        Assert.True(me!.IsPlanner);
        using var scope = app.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
        var person = await db.People.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(("First Planner", "first@company.be", "first"), (person.Name, person.Email, person.CompanyLoginSubject));
        var settings = await db.InstanceSettings.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(("Acme", CompanyLoginKind.Other, null), (settings.CompanyName, settings.CompanyLoginKind, settings.SmtpHost));
        Assert.Empty(app.Mail.Sent);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/first-run", TestContext.Current.CancellationToken)).StatusCode);
    }

    internal static HttpClient NewClient(CompanyLoginApp app) =>
        app.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

    internal static async Task StartAsync(HttpClient client, string kind, string issuer)
    {
        var start = await TestLogin.PostFrom(client, "/first-run", "/first-run/company-login/test",
            ("company", "Acme"), ("kind", kind), ("issuer", issuer), ("clientId", "headroom"), ("clientSecret", "s3cret"));
        Assert.Equal("/login/company/test", start.Headers.Location!.OriginalString);
    }
}

public class CompanyLoginFirstRunEntraTests(CompanyLoginApp app) : IClassFixture<CompanyLoginApp>
{
    private const string Tenant = "11111111-1111-1111-1111-111111111111";

    [Fact]
    public async Task TheEntraTestLoginCreatesTheFirstPlannerFromTheTenantAndObjectId()
    {
        var client = CompanyLoginFirstRunTests.NewClient(app);
        await CompanyLoginFirstRunTests.StartAsync(client, "Entra", $"https://login.microsoftonline.com/{Tenant}/v2.0");

        var callback = await app.Provider.LoginAsync(client, "/login/company/test",
            ("sub", "entra-first"), ("tid", Tenant), ("oid", "first"), ("preferred_username", "first@company.be"), ("name", "First Planner"));

        Assert.Equal("/", callback.Headers.Location!.OriginalString);
        using var scope = app.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
        var person = await db.People.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(("first@company.be", Tenant, "first", true), (person.Email, person.CompanyLoginIssuer, person.CompanyLoginSubject, person.IsPlanner));
        Assert.Equal(CompanyLoginKind.Entra, (await db.InstanceSettings.SingleAsync(TestContext.Current.CancellationToken)).CompanyLoginKind);
    }
}

public class CompanyLoginFirstRunNoEmailTests(CompanyLoginApp app) : IClassFixture<CompanyLoginApp>
{
    [Fact]
    public async Task AProviderWithoutAUsableEmailIsRefusedAndNothingIsCreated()
    {
        var client = CompanyLoginFirstRunTests.NewClient(app);
        await CompanyLoginFirstRunTests.StartAsync(client, "Other", "https://idp.example.com");

        // Kind Other trusts the email only when email_verified is true.
        var callback = await app.Provider.LoginAsync(client, "/login/company/test", ("sub", "first"), ("email", "first@company.be"), ("name", "First Planner"));

        Assert.Equal("/first-run?companyLogin=no-email", callback.Headers.Location!.OriginalString);
        using var scope = app.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
        Assert.False(await db.People.AnyAsync(TestContext.Current.CancellationToken));
        Assert.False(await db.InstanceSettings.AnyAsync(TestContext.Current.CancellationToken));
        Assert.False(await db.Contracts.AnyAsync(TestContext.Current.CancellationToken));
    }
}
