using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Headroom.Tests.Api;

// Checks the Content-Security-Policy of a server-rendered response: no framing, and every inline style allowed by its hash.
// A new inline style or style attribute fails here, before it fails silently in the browser.
public static class PagePolicy
{
    public static async Task AssertAsync(HttpResponseMessage response)
    {
        var policy = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("frame-ancestors 'none'", policy);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(" style=", html);
        foreach (Match style in Regex.Matches(html, "<style>(.*?)</style>", RegexOptions.Singleline))
            Assert.Contains($"'sha256-{Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(style.Groups[1].Value)))}'", policy);
    }
}
