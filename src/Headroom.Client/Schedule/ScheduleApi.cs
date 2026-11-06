using System.Net.Http.Json;
using Headroom.Client.Api;

namespace Headroom.Client.Schedule;

// Typed client of /api/schedule. Allocation and Time off writes go here too.
// A write returns null on success, or the text to show the user.
public class ScheduleApi(HttpClient http)
{
    public async Task<ScheduleResponse> GetAsync(DateOnly from, DateOnly to) =>
        (await http.GetFromJsonAsync<ScheduleResponse>($"api/schedule?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}"))!;

    public Task<string?> CreateAllocationAsync(Guid personId, Guid projectId, DateOnly start, DateOnly end, int percentage) =>
        ApiCall.SendAsync(http.PostAsJsonAsync("api/allocations", new CreateAllocationRequest(personId, projectId, start, end, percentage)));

    // Person and Project are optional: when null, the Allocation keeps them.
    public Task<string?> UpdateAllocationAsync(Guid id, DateOnly start, DateOnly end, int percentage, Guid? personId = null, Guid? projectId = null) =>
        ApiCall.SendAsync(http.PutAsJsonAsync($"api/allocations/{id}", new UpdateAllocationRequest(start, end, percentage, personId, projectId)));

    public Task<string?> SplitAllocationAsync(Guid id, DateOnly date) =>
        ApiCall.SendAsync(http.PostAsJsonAsync($"api/allocations/{id}/split", new SplitAllocationRequest(date)));

    public Task<string?> DeleteAllocationAsync(Guid id) => ApiCall.SendAsync(http.DeleteAsync($"api/allocations/{id}"));
}
