using Headroom.Client.Api;
using Headroom.Client.Help;
using Headroom.Client.Shell;

namespace Headroom.Tests.Shell;

public class ShellLogicTests
{
    [Fact]
    public void PlannerSeesAllEntriesInOrder() =>
        Assert.Equal(["Schedule", "Projects", "People", "Utilisation", "Access tokens", "Settings"],
            NavItem.For(isPlanner: true).Select(i => i.Label));

    [Fact]
    public void NonPlannerDoesNotSeePeopleOrSettings() =>
        Assert.Equal(["Schedule", "Projects", "Utilisation", "Access tokens"], NavItem.For(isPlanner: false).Select(i => i.Label));

    [Theory]
    [InlineData("/projects", "/help/projects")]
    [InlineData("/projects/42", "/help/projects")]
    [InlineData("/schedule?week=3", "/help/schedule")]
    [InlineData("projects", "/help/projects")]
    [InlineData("/", "/help")]
    [InlineData("/help", "/help")]
    [InlineData("/help/people", "/help")]
    [InlineData("/unknown", "/help")]
    public void HelpButtonOpensTheTopicOfTheScreenOrTheContents(string path, string href) =>
        Assert.Equal(href, HelpTopics.For(path));

    [Theory]
    [InlineData(ThemeChoice.Auto, true, true)]
    [InlineData(ThemeChoice.Auto, false, false)]
    [InlineData(ThemeChoice.Light, true, false)]
    [InlineData(ThemeChoice.Dark, false, true)]
    public void AutoFollowsTheSystemAndTheOthersIgnoreIt(ThemeChoice choice, bool system, bool expected) =>
        Assert.Equal(expected, choice.IsDark(system));

    [Fact]
    public void ThemeCyclesThroughAllThree() =>
        Assert.Equal([ThemeChoice.Light, ThemeChoice.Dark, ThemeChoice.Auto],
            new[] { ThemeChoice.Auto, ThemeChoice.Light, ThemeChoice.Dark }.Select(c => c.Next()));

    [Theory]
    [InlineData(null, ThemeChoice.Auto)]
    [InlineData("nonsense", ThemeChoice.Auto)]
    [InlineData("7", ThemeChoice.Auto)]
    [InlineData("Dark", ThemeChoice.Dark)]
    public void SavedThemeFallsBackToAuto(string? saved, ThemeChoice expected) =>
        Assert.Equal(expected, ThemeChoices.Parse(saved));

    [Fact]
    public async Task HandlerAddsTheTokenToWritesOnlyAndLearnsItFromResponses()
    {
        var token = new AntiforgeryToken();
        var inner = new StubHandler();
        var client = new HttpClient(new AntiforgeryHandler(token) { InnerHandler = inner }) { BaseAddress = new Uri("https://localhost") };

        await client.PostAsync("/api/x", null, TestContext.Current.CancellationToken); // no token known yet
        Assert.False(inner.Last!.Headers.Contains(AntiforgeryToken.HeaderName));

        inner.ResponseToken = "abc";
        await client.GetAsync("/api/me", TestContext.Current.CancellationToken);
        Assert.Equal("abc", token.Value);
        Assert.False(inner.Last!.Headers.Contains(AntiforgeryToken.HeaderName)); // not on a GET

        await client.PostAsync("/api/x", null, TestContext.Current.CancellationToken);
        Assert.Equal("abc", inner.Last!.Headers.GetValues(AntiforgeryToken.HeaderName).Single());
    }

    private class StubHandler : HttpMessageHandler
    {
        public string? ResponseToken { get; set; }
        public HttpRequestMessage? Last { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Last = request;
            var response = new HttpResponseMessage();
            if (ResponseToken is not null) response.Headers.Add(AntiforgeryToken.HeaderName, ResponseToken);
            return Task.FromResult(response);
        }
    }
}
