using System.Net;
using System.Net.Http.Json;
using Headroom.Client.Api;

namespace Headroom.Client.Shell;

// Typed client for the logged-in Person and the Instance. Copy this shape for each screen's client.
public class SessionApi(HttpClient http)
{
    // Null means "not logged in" (401).
    public async Task<MeResponse?> GetMeAsync()
    {
        var response = await http.GetAsync("api/me");
        if (response.StatusCode == HttpStatusCode.Unauthorized) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<MeResponse>();
    }

    public async Task<string?> GetCompanyNameAsync() =>
        (await http.GetFromJsonAsync<InstanceResponse>("api/instance"))?.Name;

    public async Task SignOutAsync() => (await http.PostAsync("api/logout", null)).EnsureSuccessStatusCode();
}
