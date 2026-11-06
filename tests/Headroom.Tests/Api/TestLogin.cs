using System.Text.RegularExpressions;
using Headroom.Data;
using Headroom.Email;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Headroom.Tests.Api;

// Logs a seeded Person in through the real email-code flow. Use it in API tests that need a session.
// The factory must use the FakeEmailSender passed here (swap it in with WithWebHostBuilder).
public static class TestLogin
{
    public static async Task<HttpClient> SignInAsync(WebApplicationFactory<Program> factory, FakeEmailSender mail, string email)
    {
        // A used code still counts toward the limit of 3 per 15 minutes, so each sign-in starts clean.
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<HeadroomDbContext>().LoginCodes.Where(c => c.Email == email).ExecuteDeleteAsync();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"), // the auth cookie is Secure
        });
        await Post(client, "/login", ("email", email));
        var code = mail.Sent.Last(m => m.To == email).Code;
        var done = await Post(client, "/login/verify", ("email", email), ("code", code));
        Assert.Equal(System.Net.HttpStatusCode.Redirect, done.StatusCode);
        return client;
    }

    // The antiforgery token for writes under /api. /api/me returns it in a response header.
    public static async Task<string> ApiTokenAsync(HttpClient client) =>
        (await client.GetAsync("/api/me")).Headers.GetValues(Headroom.Client.Api.AntiforgeryToken.HeaderName).Single();

    public static async Task<HttpResponseMessage> Post(HttpClient client, string url, params (string, string)[] fields) =>
        await PostFrom(client, "/login", url, fields);

    // Takes the antiforgery token from the page at formUrl.
    public static async Task<HttpResponseMessage> PostFrom(HttpClient client, string formUrl, string url, params (string, string)[] fields)
    {
        var page = await client.GetStringAsync(formUrl);
        var token = Regex.Match(page, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"").Groups[1].Value;
        return await client.PostAsync(url, new FormUrlEncodedContent(
            fields.Select(f => KeyValuePair.Create(f.Item1, f.Item2)).Append(KeyValuePair.Create("__RequestVerificationToken", token))));
    }
}
