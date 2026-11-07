using System.Net;
using System.Net.Http.Json;
using Headroom.Auth;
using Headroom.Data;
using Headroom.Email;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headroom.Tests.Api;

public class AppShellTests : IClassFixture<HeadroomApp>, IAsyncLifetime
{
    private readonly FakeEmailSender _mail;
    private readonly WebApplicationFactory<Program> _factory;

    public AppShellTests(HeadroomApp app)
    {
        _factory = app.Factory;
        _mail = app.Mail;
    }

    public async ValueTask InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
        if (db.People.Any()) return;
        var ann = new Person { Name = "Ann Planner", Email = "ann@example.com", IsPlanner = true };
        ann.LastChangedBy = ann.Id;
        db.InstanceSettings.Add(new InstanceSettings { CompanyName = "Acme", LastChangedBy = ann.Id });
        db.People.Add(ann);
        db.People.Add(new Person { Name = "Flo Good", Email = "flo@example.com", LastChangedBy = ann.Id });
        await db.SaveChangesAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task SignOutEndsTheSessionOnTheServer()
    {
        var client = await TestLogin.SignInAsync(_factory, _mail, "flo@example.com");
        var token = await TestLogin.ApiTokenAsync(client);
        var sessionsBefore = await CountSessions();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/logout", null, TestContext.Current.CancellationToken)).StatusCode); // no antiforgery header
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/logout");
        request.Headers.Add(Headroom.Client.Api.AntiforgeryToken.HeaderName, token);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request, TestContext.Current.CancellationToken)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(sessionsBefore - 1, await CountSessions());
    }

    [Fact]
    public async Task ALoggedInPersonReadsTheInstance()
    {
        var client = await TestLogin.SignInAsync(_factory, _mail, "flo@example.com");

        var instance = await client.GetFromJsonAsync<Headroom.Client.Api.InstanceResponse>("/api/instance", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(("Instance", "Acme"), (instance?.Type, instance?.Name));
    }

    [Fact]
    public async Task OnlyAPlannerPassesThePlannerPolicy()
    {
        var auth = _factory.Services.GetRequiredService<IAuthorizationService>();
        var planner = PersonPrincipal.Create(new Person { Name = "P", IsPlanner = true });
        var other = PersonPrincipal.Create(new Person { Name = "O" });

        Assert.True((await auth.AuthorizeAsync(planner, PlannerPolicy.Name)).Succeeded);
        Assert.False((await auth.AuthorizeAsync(other, PlannerPolicy.Name)).Succeeded);
    }

    [Theory]
    [InlineData("/api/people", StatusCodes.Status403Forbidden)]
    [InlineData("/people", StatusCodes.Status302Found)]
    public async Task AccessDeniedGivesApiA403AndPagesARedirect(string path, int expected)
    {
        var options = _factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var http = new DefaultHttpContext();
        http.Request.Path = path;
        var context = new RedirectContext<CookieAuthenticationOptions>(
            http, new AuthenticationScheme(CookieAuthenticationDefaults.AuthenticationScheme, null, typeof(CookieAuthenticationHandler)),
            options, new AuthenticationProperties(), "/denied");

        await options.Events.OnRedirectToAccessDenied(context);

        Assert.Equal(expected, http.Response.StatusCode);
    }

    private async Task<int> CountSessions()
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<HeadroomDbContext>().Sessions.CountAsync();
    }
}
