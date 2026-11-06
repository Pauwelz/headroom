using System.Net.Http.Json;
using Headroom.Client.Api;

namespace Headroom.Client.Utilisation;

// Typed client of /api/utilisation. Period is "week" or "month".
public class UtilisationApi(HttpClient http)
{
    public async Task<UtilisationResponse> GetAsync(UtilisationRange range, string period) =>
        (await http.GetFromJsonAsync<UtilisationResponse>(
            $"api/utilisation?from={range.From:yyyy-MM-dd}&to={range.To:yyyy-MM-dd}&period={period}"))!;
}
