using System.Net;
using Headroom.Events;

namespace Headroom.Tests.Api;

public class WebhookGuardTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("::ffff:10.0.0.1")]
    public void InternalAddressesAreBlocked(string ip) => Assert.True(WebhookGuard.IsBlocked(IPAddress.Parse(ip)));

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("172.15.0.1")]
    [InlineData("172.32.0.1")]
    [InlineData("2606:4700::1111")]
    public void PublicAddressesAreAllowed(string ip) => Assert.False(WebhookGuard.IsBlocked(IPAddress.Parse(ip)));

    [Theory]
    [InlineData("https://hooks.example.com/x", false, true)]
    [InlineData("http://hooks.example.com/x", false, false)]
    [InlineData("http://localhost:5000/x", true, true)]
    [InlineData("ftp://hooks.example.com/x", true, false)]
    [InlineData("not a url", true, false)]
    public void HttpIsAllowedOnlyInDevelopment(string url, bool development, bool valid) =>
        Assert.Equal(valid, WebhookGuard.UrlProblem(url, development, out _) is null);

    [Fact]
    public async Task TheConnectionToALoopbackAddressIsRefusedAtConnectTime()
    {
        using var client = new HttpClient(WebhookGuard.CreateHandler(allowInternal: false));

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("https://127.0.0.1:1/", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("not allowed", error.Message);
    }

    [Fact]
    public void RedirectsAreNotFollowed()
    {
        using var handler = WebhookGuard.CreateHandler(allowInternal: false);

        Assert.False(handler.AllowAutoRedirect);
    }
}
