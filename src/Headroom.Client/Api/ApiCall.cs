using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Headroom.Client.Api;

public static class ApiCall
{
    private record Problem(string? Detail);

    /// <summary>Awaits a write. Returns null on success, or the text to show the user.</summary>
    public static async Task<string?> SendAsync(Task<HttpResponseMessage> call)
    {
        using var response = await call;
        return response.IsSuccessStatusCode ? null : await ErrorTextAsync(response);
    }

    /// <summary>The text to show for a refused request. An empty or non-JSON body gives a generic text.</summary>
    public static async Task<string> ErrorTextAsync(HttpResponseMessage response)
    {
        string? detail = null;
        try { detail = (await response.Content.ReadFromJsonAsync<Problem>())?.Detail; }
        catch (Exception e) when (e is JsonException or NotSupportedException) { }
        if (!string.IsNullOrWhiteSpace(detail)) return detail;
        return response.StatusCode == HttpStatusCode.Forbidden ? "Only a Planner can do this." : $"The server refused the request ({(int)response.StatusCode}).";
    }
}
