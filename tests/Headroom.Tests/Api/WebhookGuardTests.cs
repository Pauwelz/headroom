using System.Net;
using Headroom.Events;
using Microsoft.Security.AntiSSRF;

namespace Headroom.Tests.Api;

public class WebhookGuardTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("[::1]")]
    [InlineData("10.0.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("168.63.129.16")]
    [InlineData("[::ffff:10.0.0.1]")]
    [InlineData("[fd12:3456::1]")]
    public async Task AConnectionToANonPublicAddressIsRefused(string host)
    {
        using var client = new HttpClient(WebhookGuard.CreateHandler(allowInternal: false));

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync($"https://{host}:1/", TestContext.Current.CancellationToken));

        Assert.Contains("not allowed", error.Message);
    }

    [Theory]
    [InlineData("https://hooks.example.com/x", false, true)]
    [InlineData("http://hooks.example.com/x", false, false)]
    [InlineData("http://localhost:5000/x", true, true)]
    [InlineData("ftp://hooks.example.com/x", true, false)]
    [InlineData("not a url", true, false)]
    public void HttpIsAllowedOnlyInDevelopment(string url, bool development, bool valid) =>
        Assert.Equal(valid, WebhookGuard.UrlProblem(url, development, out _) is null);

    [Fact]
    public void RedirectsAreNotFollowed()
    {
        using var handler = (DelegatingHandler)WebhookGuard.CreateHandler(allowInternal: false);

        Assert.False(((AntiSSRFHandler)handler.InnerHandler!).AllowAutoRedirect);
    }

    // A proxy would make AntiSSRF check the proxy address instead of the Webhook address.
    [Fact]
    public async Task ASystemProxyDoesNotBypassTheGuard()
    {
        var saved = HttpClient.DefaultProxy;
        HttpClient.DefaultProxy = new WebProxy("http://1.1.1.1:1");
        try
        {
            using var client = new HttpClient(WebhookGuard.CreateHandler(allowInternal: false)) { Timeout = TimeSpan.FromSeconds(5) };

            var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("https://10.0.0.1:1/", TestContext.Current.CancellationToken));

            Assert.Contains("not allowed", error.Message);
        }
        finally { HttpClient.DefaultProxy = saved; }
    }

    [Fact]
    public void TheDevelopmentHandlerUsesNoProxy()
    {
        using var handler = (SocketsHttpHandler)WebhookGuard.CreateHandler(allowInternal: true);

        Assert.False(handler.UseProxy);
        Assert.False(handler.AllowAutoRedirect);
    }
}
