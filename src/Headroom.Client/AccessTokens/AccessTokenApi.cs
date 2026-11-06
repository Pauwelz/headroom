using System.Net.Http.Json;
using Headroom.Client.Api;

namespace Headroom.Client.AccessTokens;

// Typed client for the Access tokens of the signed-in Person.
public class AccessTokenApi(HttpClient http)
{
    private const string Path = "api/me/access-tokens";

    public async Task<List<AccessTokenResponse>> GetAsync() =>
        await http.GetFromJsonAsync<List<AccessTokenResponse>>(Path) ?? [];

    // Returns the new token with its secret, or the server's error text.
    public async Task<(NewAccessTokenResponse? Created, string? Error)> AddAsync(string name, string expiry, bool canChange)
    {
        var response = await http.PostAsJsonAsync(Path, new AccessTokenRequest(name, expiry, CanChange: canChange));
        return response.IsSuccessStatusCode
            ? (await response.Content.ReadFromJsonAsync<NewAccessTokenResponse>(), null)
            : (null, (await response.Content.ReadAsStringAsync()).Trim('"'));
    }

    public async Task<List<ConnectionResponse>> GetConnectionsAsync() =>
        await http.GetFromJsonAsync<List<ConnectionResponse>>("api/me/connections") ?? [];

    public async Task<bool> RevokeConnectionAsync(string id) => (await http.DeleteAsync($"api/me/connections/{id}")).IsSuccessStatusCode;

    public async Task<bool> RevokeAsync(Guid id) => (await http.DeleteAsync($"{Path}/{id}")).IsSuccessStatusCode;
}
