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
        if (response.StatusCode == HttpStatusCode.Forbidden) return "Only a Planner can do this.";
        string? detail = null;
        try { detail = (await response.Content.ReadFromJsonAsync<Problem>())?.Detail; }
        catch (Exception e) when (e is JsonException or NotSupportedException) { }
        return string.IsNullOrWhiteSpace(detail) ? $"The server refused the request ({(int)response.StatusCode})." : detail;
    }
}
