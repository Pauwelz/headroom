using System.Net.Http.Json;
using Headroom.Client.Api;

namespace Headroom.Client.Projects;

// Typed client for Clients and Projects. A write returns null on success, or the text to show the user.
public class ProjectsApi(HttpClient http)
{
    public async Task<List<ClientResponse>> ListAsync() =>
        await http.GetFromJsonAsync<List<ClientResponse>>("api/clients") ?? [];

    public async Task<List<ProjectPersonResponse>> PeopleAsync(Guid projectId) =>
        await http.GetFromJsonAsync<List<ProjectPersonResponse>>($"api/projects/{projectId}/people") ?? [];

    public async Task<HashSet<Guid>> MineAsync() =>
        (await http.GetFromJsonAsync<List<Guid>>("api/projects/mine") ?? []).ToHashSet();

    public Task<string?> CreateClientAsync(string name) =>
        ApiCall.SendAsync(http.PostAsJsonAsync("api/clients", new ClientRequest(name)));

    public Task<string?> RenameClientAsync(Guid id, string name) =>
        ApiCall.SendAsync(http.PutAsJsonAsync($"api/clients/{id}", new ClientRequest(name)));

    public Task<string?> DeleteClientAsync(Guid id) => ApiCall.SendAsync(http.DeleteAsync($"api/clients/{id}"));

    public Task<string?> CreateProjectAsync(Guid clientId, string name, string status) =>
        ApiCall.SendAsync(http.PostAsJsonAsync($"api/clients/{clientId}/projects", new CreateProjectRequest(name, status)));

    public Task<string?> UpdateProjectAsync(Guid id, string name, Guid clientId, string status) =>
        ApiCall.SendAsync(http.PutAsJsonAsync($"api/projects/{id}", new UpdateProjectRequest(name, clientId, status)));

    public Task<string?> DeleteProjectAsync(Guid id) => ApiCall.SendAsync(http.DeleteAsync($"api/projects/{id}"));
}
