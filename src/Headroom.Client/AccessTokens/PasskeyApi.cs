using System.Net.Http.Json;
using System.Text;
using Headroom.Client.Api;

namespace Headroom.Client.AccessTokens;

// Typed client for the Passkeys of the signed-in Person. The WebAuthn JSON stays a raw string: the browser reads and writes it.
public class PasskeyApi(HttpClient http)
{
    private const string Path = "api/me/passkeys";

    public async Task<List<PasskeyResponse>> GetAsync() =>
        await http.GetFromJsonAsync<List<PasskeyResponse>>(Path) ?? [];

    public async Task<string> OptionsAsync() =>
        await (await http.PostAsync($"{Path}/options", null)).Content.ReadAsStringAsync();

    // Returns null on success, or the text to show.
    public Task<string?> SaveAsync(string credentialJson) =>
        ApiCall.SendAsync(http.PostAsync(Path, new StringContent(credentialJson, Encoding.UTF8, "application/json")));

    public async Task<bool> RemoveAsync(Guid id) => (await http.DeleteAsync($"{Path}/{id}")).IsSuccessStatusCode;
}
