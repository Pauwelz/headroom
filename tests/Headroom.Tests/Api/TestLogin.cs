using System.Net;
using System.Security.Cryptography;
using System.Text;
using Headroom.Auth;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Headroom.Tests.Api;

// Logs a seeded Person in through the real Entra ID flow, with the FakeProvider of the HeadroomApp. Use it in API tests that need a session.
public static class TestLogin
{
    public static async Task<HttpClient> SignInAsync(WebApplicationFactory<Program> factory, string email)
    {
        var client = NewClient(factory);
        var done = await factory.Services.GetRequiredService<FakeProvider>().LoginAsync(client, CompanyLogin.StartPath, Claims(email));
        Assert.Equal(HttpStatusCode.Redirect, done.StatusCode);
        Assert.DoesNotContain("/login", done.Headers.Location!.OriginalString);
        return client;
    }

    public static HttpClient NewClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"), // the auth cookie is Secure
        });

    // The claims of Entra ID. The object id follows the email, so a Person keeps one identity.
    public static (string, string)[] Claims(string email, string? name = null)
    {
        var oid = new Guid(MD5.HashData(Encoding.UTF8.GetBytes(email.ToLowerInvariant()))).ToString();
        return [("sub", "entra-" + oid), ("tid", HeadroomApp.TenantId), ("oid", oid), ("preferred_username", email), ("name", name ?? "Test Person")];
    }

    // The antiforgery token for writes under /api. /api/me returns it in a response header.
    public static async Task<string> ApiTokenAsync(HttpClient client) =>
        (await client.GetAsync("/api/me")).Headers.GetValues(Headroom.Client.Api.AntiforgeryToken.HeaderName).Single();
}
