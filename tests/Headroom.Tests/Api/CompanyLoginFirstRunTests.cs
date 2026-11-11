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
        var client = app.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        var form = await client.GetStringAsync("/first-run", TestContext.Current.CancellationToken);
        Assert.Contains("Set up Company login instead", form);

        var step = await TestLogin.PostFrom(client, "/first-run", "/first-run/company-login", ("company", "Acme"));
        var page = await step.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("https://localhost/signin-oidc", page);

        var start = await TestLogin.PostFrom(client, "/first-run", "/first-run/company-login/test",
            ("company", "Acme"), ("kind", "Other"), ("issuer", "https://idp.example.com"), ("clientId", "headroom"), ("clientSecret", "s3cret"));
        Assert.Equal("/login/company/test", start.Headers.Location!.OriginalString);

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
}
