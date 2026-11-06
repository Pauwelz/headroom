using System.Net.Http.Json;
using Headroom.Client.Api;
using Headroom.Client.People;

namespace Headroom.Client.Schedule;

// Typed client of the Time off writes. Every call throws ApiException when the server says no.
public class TimeOffApi(HttpClient http)
{
    public Task AddAsync(Guid personId, TimeOffRequest request) => Send(http.PostAsJsonAsync($"api/people/{personId}/time-off", request));

    public Task EditAsync(Guid id, TimeOffRequest request) => Send(http.PutAsJsonAsync($"api/time-off/{id}", request));

    public Task DeleteAsync(Guid id) => Send(http.DeleteAsync($"api/time-off/{id}"));

    private static async Task Send(Task<HttpResponseMessage> call)
    {
        var response = await call;
        if (response.IsSuccessStatusCode) return;
        throw new ApiException(await ApiCall.ErrorTextAsync(response));
    }
}
