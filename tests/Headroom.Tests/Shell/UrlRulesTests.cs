using Headroom.Client.Shell;

namespace Headroom.Tests.Shell;

public class UrlRulesTests
{
    [Theory]
    [InlineData("https://example.com/hook", false)]
    [InlineData("  https://example.com  ", false)]
    [InlineData("http://localhost:5000/hook", true)]
    [InlineData("https://example.com/hook", true)]
    public void AWebhookUrlIsValid(string value, bool development) => Assert.Null(UrlRules.WebhookUrl(value, development));

    [Theory]
    [InlineData(null, false)]
    [InlineData("", true)]
    [InlineData("example.com/hook", true)]
    [InlineData("/hook", false)]
    [InlineData("ftp://example.com/hook", true)]
    [InlineData("http://example.com/hook", false)]
    public void AWebhookUrlIsInvalid(string? value, bool development) => Assert.NotNull(UrlRules.WebhookUrl(value, development));

    [Fact]
    public void AWebhookUrlOfMoreThan2000CharactersIsInvalid() =>
        Assert.NotNull(UrlRules.WebhookUrl("https://example.com/" + new string('a', 2000), false));

    [Fact]
    public void TheMessageNamesHttpOnlyInDevelopment()
    {
        Assert.Equal("The URL must be an absolute https URL.", UrlRules.WebhookUrl("http://x.example", false));
        Assert.Equal("The URL must be an absolute http or https URL.", UrlRules.WebhookUrl("ftp://x.example", true));
    }

    [Theory]
    [InlineData("https://claude.ai/callback")]
    [InlineData("https://a.example/cb\nhttps://b.example/cb")]
    [InlineData("https://a.example/cb\r\n\r\nhttps://b.example/cb\r\n")]
    public void RedirectUrlsAreValid(string value) => Assert.Null(UrlRules.RedirectUrls(value));

    [Theory]
    [InlineData("http://example.com/cb")]
    [InlineData("https://example.com/cb#x")]
    [InlineData("/cb")]
    [InlineData("https://a.example/cb\nnot a url")]
    public void RedirectUrlsAreInvalid(string value) => Assert.NotNull(UrlRules.RedirectUrls(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \n  ")]
    public void NoRedirectUrlGivesTheNoUrlMessage(string? value) => Assert.Equal(UrlRules.NoRedirectUrl, UrlRules.RedirectUrls(value));

    [Fact]
    public void OneRedirectUrlHasNoHttpAllowance() => Assert.NotNull(UrlRules.RedirectUrl("http://localhost/cb"));
}
