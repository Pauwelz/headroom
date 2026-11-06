using System.Net;
using Headroom.Client.Api;
using Headroom.Client.People;

namespace Headroom.Client.Help;

// Typed client for the help. The server renders the user guide to HTML.
public class HelpApi(HttpClient http)
{
    /// <summary>The topic of the contents page.</summary>
    public const string Contents = "README";

    /// <summary>The HTML of a topic, or null when the topic does not exist. Throws <see cref="ApiException"/> on any other error.</summary>
    public async Task<string?> TopicAsync(string topic)
    {
        using var response = await http.GetAsync($"api/help/{Uri.EscapeDataString(topic)}");
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode) throw new ApiException(await ApiCall.ErrorTextAsync(response));
        return await response.Content.ReadAsStringAsync();
    }
}
