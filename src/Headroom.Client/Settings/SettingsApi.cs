using System.Net.Http.Json;
using Headroom.Client.Api;

namespace Headroom.Client.Settings;

// Typed client for the Settings endpoints (Planners only).
public class SettingsApi(HttpClient http)
{
    private const string Path = "api/settings/company-days-off";

    public async Task<List<DayOffResponse>> GetDaysOffAsync(int year) =>
        await http.GetFromJsonAsync<List<DayOffResponse>>($"{Path}?year={year}") ?? [];

    // Each write returns null on success, or the server's error text.
    public async Task<string?> AddAsync(DateOnly date, string name) =>
        await ErrorOf(await http.PostAsJsonAsync(Path, new DayOffRequest(date, name)));

    public async Task<string?> EditAsync(Guid id, DateOnly date, string name) =>
        await ErrorOf(await http.PutAsJsonAsync($"{Path}/{id}", new DayOffRequest(date, name)));

    public async Task<string?> DeleteAsync(Guid id) => await ErrorOf(await http.DeleteAsync($"{Path}/{id}"));

    public Task<string?> ChangeCompanyNameAsync(string name) =>
        ApiCall.SendAsync(http.PutAsJsonAsync("api/settings/company-name", new CompanyNameRequest(name)));

    private const string TokenPath = "api/settings/access-tokens";

    public async Task<List<AccessTokenResponse>> GetTokensAsync() =>
        await http.GetFromJsonAsync<List<AccessTokenResponse>>(TokenPath) ?? [];

    public async Task<string?> RevokeTokenAsync(Guid id) => await ErrorOf(await http.DeleteAsync($"{TokenPath}/{id}"));

    public async Task<List<ConnectionResponse>> GetConnectionsAsync() =>
        await http.GetFromJsonAsync<List<ConnectionResponse>>("api/settings/connections") ?? [];

    public async Task<string?> RevokeConnectionAsync(string id) => await ErrorOf(await http.DeleteAsync($"api/settings/connections/{id}"));

    private const string WebhookPath = "api/settings/webhooks";

    public async Task<List<WebhookResponse>> GetWebhooksAsync() =>
        await http.GetFromJsonAsync<List<WebhookResponse>>(WebhookPath) ?? [];

    // Returns the new Webhook with its secret, or the server's error text.
    public async Task<(CreatedWebhookResponse? Webhook, string? Error)> AddWebhookAsync(string name, string url)
    {
        var response = await http.PostAsJsonAsync(WebhookPath, new WebhookRequest(name, url));
        return response.IsSuccessStatusCode
            ? (await response.Content.ReadFromJsonAsync<CreatedWebhookResponse>(), null)
            : (null, await ErrorOf(response));
    }

    public async Task<string?> RenameWebhookAsync(Guid id, string name) =>
        await ErrorOf(await http.PutAsJsonAsync($"{WebhookPath}/{id}", new RenameWebhookRequest(name)));

    public async Task<List<FailedEventResponse>> GetFailedEventsAsync() =>
        await http.GetFromJsonAsync<List<FailedEventResponse>>($"{WebhookPath}/failed-events") ?? [];

    public async Task<(CreatedWebhookResponse? Webhook, string? Error)> RotateWebhookSecretAsync(Guid id)
    {
        var response = await http.PostAsync($"{WebhookPath}/{id}/secret", null);
        return response.IsSuccessStatusCode
            ? (await response.Content.ReadFromJsonAsync<CreatedWebhookResponse>(), null)
            : (null, await ErrorOf(response));
    }

    public async Task<string?> DeleteWebhookAsync(Guid id) => await ErrorOf(await http.DeleteAsync($"{WebhookPath}/{id}"));

    private const string ConnectedAppPath = "api/settings/connected-apps";

    public async Task<List<ConnectedAppResponse>> GetConnectedAppsAsync() =>
        await http.GetFromJsonAsync<List<ConnectedAppResponse>>(ConnectedAppPath) ?? [];

    // Returns the new Connected app with its client secret, or the server's error text.
    public async Task<(CreatedConnectedAppResponse? App, string? Error)> AddConnectedAppAsync(string name, List<string> redirectUrls) =>
        await ReadCreatedAsync(await http.PostAsJsonAsync(ConnectedAppPath, new ConnectedAppRequest(name, redirectUrls)));

    public async Task<string?> ChangeConnectedAppAsync(string id, string name, List<string> redirectUrls) =>
        await ErrorOf(await http.PutAsJsonAsync($"{ConnectedAppPath}/{id}", new ConnectedAppRequest(name, redirectUrls)));

    public async Task<(CreatedConnectedAppResponse? App, string? Error)> NewConnectedAppSecretAsync(string id) =>
        await ReadCreatedAsync(await http.PostAsync($"{ConnectedAppPath}/{id}/secret", null));

    public async Task<string?> DeleteConnectedAppAsync(string id) => await ErrorOf(await http.DeleteAsync($"{ConnectedAppPath}/{id}"));

    private static async Task<(CreatedConnectedAppResponse?, string?)> ReadCreatedAsync(HttpResponseMessage response) =>
        response.IsSuccessStatusCode
            ? (await response.Content.ReadFromJsonAsync<CreatedConnectedAppResponse>(), null)
            : (null, await ErrorOf(response));

    private static async Task<string?> ErrorOf(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return null;
        var text = (await response.Content.ReadAsStringAsync()).Trim('"');
        return string.IsNullOrWhiteSpace(text) ? $"The server refused the change ({(int)response.StatusCode})." : text;
    }
}
