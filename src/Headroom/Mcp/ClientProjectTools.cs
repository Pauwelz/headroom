using System.ComponentModel;
using Headroom.Allocations;
using Headroom.Auth;
using Headroom.Capacity;
using Headroom.Client.Api;
using Headroom.Data;
using Headroom.Endpoints;
using Headroom.People;
using Headroom.Projects;
using Headroom.Utilisation;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using static Headroom.Mcp.ToolSupport;

namespace Headroom.Mcp;

// MCP tools for Clients and Projects. Each one calls the code of the matching endpoint, in process.
[McpServerToolType]
public sealed class ClientProjectTools
{
    private const string ClientName = "Name of the Client, 1 to 200 characters";

    [McpServerTool(Name = "list_projects", ReadOnly = true), Description("The Projects of all Clients, with their status and dates.")]
    public static async Task<List<ProjectResponse>> ListProjects(
        HeadroomDbContext db, [Description("tentative, confirmed or archived")] string? status = null) =>
        (await ProjectEndpoints.ListClientsAsync(db)).SelectMany(c => c.Projects)
            .Where(p => status is null || p.Status.Equals(status, StringComparison.OrdinalIgnoreCase)).ToList();

    [McpServerTool(Name = "list_clients", ReadOnly = true), Description("The Clients with their Projects. Gives the Client ids and Project ids that the Client and Project tools need.")]
    public static Task<List<ClientResponse>> ListClients(HeadroomDbContext db) => ProjectEndpoints.ListClientsAsync(db);

    [McpServerTool(Name = "create_client", Destructive = false), Description("Create a Client." + PlannerRights)]
    public static async Task<string> CreateClient([Description(ClientName)] string name, HeadroomDbContext db, IHttpContextAccessor http) =>
        Done((await ClientWrites.CreateAsync(db, McpWriteGuard.PlannerId(http), new(name))).Refusal, "Client created.");

    [McpServerTool(Name = "rename_client", Destructive = true), Description("Rename a Client. The Client id is in list_clients." + PlannerRights)]
    public static async Task<string> RenameClient([Description("Id of the Client")] Guid id, [Description(ClientName)] string name, HeadroomDbContext db, IHttpContextAccessor http) =>
        Done((await ClientWrites.RenameAsync(db, McpWriteGuard.PlannerId(http), id, new(name))).Refusal, "Client renamed.");

    [McpServerTool(Name = "create_project", Destructive = false), Description("Create a Project for a Client. A new Project is Tentative or Confirmed. The Client id is in list_clients." + PlannerRights)]
    public static async Task<string> CreateProject(
        [Description("Id of the Client")] Guid clientId, [Description("Name of the Project, unique for the Client")] string name,
        [Description("\"tentative\" or \"confirmed\"")] string status, HeadroomDbContext db, IHttpContextAccessor http) =>
        Done((await ProjectWrites.CreateAsync(db, McpWriteGuard.PlannerId(http), clientId, new(name, status))).Refusal, "Project created.");

    [McpServerTool(Name = "change_project", Destructive = true), Description("Change the name, Client and status of a Project. Give all three. The Project id is in list_projects. An Archived Project cannot become Tentative." + PlannerRights)]
    public static async Task<string> ChangeProject(
        [Description("Id of the Project")] Guid id, [Description("Name of the Project, unique for the Client")] string name,
        [Description("Id of the Client. Give the current Client to keep it")] Guid clientId,
        [Description("\"tentative\", \"confirmed\" or \"archived\"")] string status, HeadroomDbContext db, IHttpContextAccessor http) =>
        Done((await ProjectWrites.UpdateAsync(db, McpWriteGuard.PlannerId(http), id, new(name, clientId, status))).Refusal, "Project changed.");
}
