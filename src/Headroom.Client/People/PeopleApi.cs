using System.Net;
using System.Net.Http.Json;
using Headroom.Client.Api;

namespace Headroom.Client.People;

/// <summary>The server refused a request. Message is text for the Planner.</summary>
public class ApiException(string message) : Exception(message);

// Typed client of /api/people. Every call throws ApiException when the server says no.
public class PeopleApi(HttpClient http)
{
    public async Task<IReadOnlyList<PersonResponse>> ListAsync() =>
        await Read<List<PersonResponse>>(await http.GetAsync("api/people")) ?? [];

    public async Task AddAsync(AddPersonRequest request) =>
        await Read<PersonResponse>(await http.PostAsJsonAsync("api/people", request));

    public async Task EditAsync(Guid id, EditPersonRequest request) =>
        await Read<PersonResponse>(await http.PutAsJsonAsync($"api/people/{id}", request));

    public async Task AddContractAsync(Guid personId, ContractRequest request) =>
        await Read<ContractResponse>(await http.PostAsJsonAsync($"api/people/{personId}/contracts", request));

    public async Task EditContractAsync(Guid personId, Guid contractId, ContractRequest request) =>
        await Read<ContractResponse>(await http.PutAsJsonAsync($"api/people/{personId}/contracts/{contractId}", request));

    public async Task DeleteContractAsync(Guid personId, Guid contractId) =>
        await Read<object>(await http.DeleteAsync($"api/people/{personId}/contracts/{contractId}"));

    public async Task<EndDateImpact> EndDateImpactAsync(Guid personId, DateOnly endDate) =>
        (await Read<EndDateImpact>(await http.GetAsync($"api/people/{personId}/end-date-impact?endDate={endDate:yyyy-MM-dd}")))!;

    public async Task SetEndDateAsync(Guid personId, DateOnly endDate) =>
        await Read<PersonResponse>(await http.PutAsJsonAsync($"api/people/{personId}/end-date", new SetEndDateRequest(endDate)));

    public async Task RemoveEndDateAsync(Guid personId) =>
        await Read<PersonResponse>(await http.DeleteAsync($"api/people/{personId}/end-date"));

    public async Task RehireAsync(Guid personId, RehireRequest request) =>
        await Read<PersonResponse>(await http.PostAsJsonAsync($"api/people/{personId}/rehire", request));

    public async Task<DeleteCheck> DeleteCheckAsync(Guid personId) =>
        (await Read<DeleteCheck>(await http.GetAsync($"api/people/{personId}/delete-check")))!;

    public async Task DeleteAsync(Guid personId) =>
        await Read<object>(await http.DeleteAsync($"api/people/{personId}"));

    private static async Task<T?> Read<T>(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return response.StatusCode == HttpStatusCode.NoContent ? default : await response.Content.ReadFromJsonAsync<T>();
        throw new ApiException(await ApiCall.ErrorTextAsync(response));
    }
}
