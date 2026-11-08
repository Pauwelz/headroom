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

// MCP tools for Allocations. Each one calls the code of the matching endpoint, in process.
[McpServerToolType]
public sealed class AllocationTools
{
    private const string Percentage = "5 to 100, in steps of 5";

    [McpServerTool(Name = "create_allocation", Destructive = false), Description("Create an Allocation. Needs an Access token of a Planner with Can change. Allocations of the same Person and Project that overlap are merged: the new one wins on its days. The percentage is 5 to 100 in steps of 5.")]
    public static async Task<string> CreateAllocation(
        [Description(PersonIdText)] Guid personId, [Description("Id of the Project")] Guid projectId,
        [Description(FirstDay)] DateOnly startDate, [Description(LastDay)] DateOnly endDate,
        [Description(Percentage)] int percentage, HeadroomDbContext db, IHttpContextAccessor http) =>
        Done(await AllocationWrites.CreateAsync(db, McpWriteGuard.PlannerId(http), new(personId, projectId, startDate, endDate, percentage)), "Allocation created.");

    [McpServerTool(Name = "change_allocation", Destructive = true), Description("Change the dates and percentage of an Allocation, and optionally its Person or Project. The Allocation id is in get_schedule. Needs an Access token of a Planner with Can change.")]
    public static async Task<string> ChangeAllocation(
        [Description("Id of the Allocation")] Guid id,
        [Description(FirstDay)] DateOnly startDate, [Description(LastDay)] DateOnly endDate,
        [Description(Percentage)] int percentage, HeadroomDbContext db, IHttpContextAccessor http,
        [Description("New Person id. Leave out to keep the Person")] Guid? personId = null,
        [Description("New Project id. Leave out to keep the Project")] Guid? projectId = null) =>
        Done(await AllocationWrites.UpdateAsync(db, McpWriteGuard.PlannerId(http), id, new(startDate, endDate, percentage, personId, projectId)), "Allocation changed.");

    [McpServerTool(Name = "split_allocation", Destructive = true), Description("Split an Allocation on a date. The first part ends the day before the date. The second part starts on the date. Both parts keep the Person, Project and percentage. The Allocation id is in get_schedule. Needs an Access token of a Planner with Can change.")]
    public static async Task<string> SplitAllocation(
        [Description("Id of the Allocation")] Guid id,
        [Description("First day of the second part. After the start date and not after the end date of the Allocation")] DateOnly date,
        HeadroomDbContext db, IHttpContextAccessor http) =>
        Done(await AllocationWrites.SplitAsync(db, McpWriteGuard.PlannerId(http), id, new(date)), "Allocation split.");

    [McpServerTool(Name = "delete_allocation", Destructive = true), Description("Delete an Allocation. The Allocation id is in get_schedule. Needs an Access token of a Planner with Can change.")]
    public static async Task<string> DeleteAllocation([Description("Id of the Allocation")] Guid id, HeadroomDbContext db, IHttpContextAccessor http) =>
        Done(await AllocationWrites.DeleteAsync(db, McpWriteGuard.AfterPlannerCheck(http, id)), "Allocation deleted.");
}
