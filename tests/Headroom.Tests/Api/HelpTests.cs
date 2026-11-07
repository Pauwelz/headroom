using System.Net;
using Headroom.Data;
using Headroom.Email;
using Headroom.Endpoints;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Headroom.Tests.Api;

public class HelpTests : IClassFixture<HeadroomApp>, IAsyncLifetime
{
    private readonly FakeEmailSender _mail;
    private readonly WebApplicationFactory<Program> _factory;

    public HelpTests(HeadroomApp app)
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

    [Theory]
    [InlineData("README")]
    [InlineData("getting-started")]
    [InlineData("schedule")]
    [InlineData("projects")]
    [InlineData("utilisation")]
    [InlineData("people")]
    [InlineData("settings")]
    [InlineData("privacy")]
    public async Task APersonWhoIsNotAPlannerReadsEveryTopic(string topic)
    {
        var client = await TestLogin.SignInAsync(_factory, _mail, "flo@example.com");
        var response = await client.GetAsync($"/api/help/{topic}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("<h1", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        await PagePolicy.AssertAsync(response);
    }

    [Theory]
    [InlineData("/api/help/schedule")]
    [InlineData("/api/help/images/settings.png")]
    public async Task ARequestWithoutLoginIsRefused(string url) =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync(url, TestContext.Current.CancellationToken)).StatusCode);

    [Theory]
    [InlineData("/api/help/nope")]
    [InlineData("/api/help/..%2fREADME")]
    [InlineData("/api/help/%2e%2e%2f%2e%2e%2fREADME")]
    [InlineData("/api/help/images/nope.png")]
    [InlineData("/api/help/images/..%2fREADME.md")]
    public async Task AnUnknownNameIsNotFound(string url)
    {
        var client = await TestLogin.SignInAsync(_factory, _mail, "flo@example.com");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(url, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task TheContentsLinkToTopicsInsideTheApp()
    {
        var client = await TestLogin.SignInAsync(_factory, _mail, "flo@example.com");
        var contents = await client.GetStringAsync("/api/help/README", TestContext.Current.CancellationToken);
        Assert.Contains("href=\"/help/getting-started\"", contents);
    }

    [Fact]
    public void RawHtmlInTheGuideIsNotPassedThrough() =>
        Assert.DoesNotContain("<script", HelpEndpoints.Render("<script>x</script>"));

    [Fact]
    public void ALinkToATopicKeepsItsAnchor() =>
        Assert.Contains("href=\"/help/people#end-date\"", HelpEndpoints.Render("See [People](people.md#end-date)."));

    [Fact]
    public async Task ImagesComeBackFromTheApi()
    {
        var client = await TestLogin.SignInAsync(_factory, _mail, "flo@example.com");
        Assert.Contains("src=\"/api/help/images/login-email.png\"", await client.GetStringAsync("/api/help/getting-started", TestContext.Current.CancellationToken));
        var response = await client.GetAsync("/api/help/images/login-email.png", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
    }
}
