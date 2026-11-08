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

// MCP tools for Time off. Each one calls the code of the matching endpoint, in process.
[McpServerToolType]
public sealed class TimeOffTools
{
    private const string DayPart = "\"morning\" or \"afternoon\"";
    private const string TimeOffRights = " Needs an Access token or Connection with Can change. A Person can change their own Time off, a Planner the Time off of every Person.";

    [McpServerTool(Name = "add_time_off", Destructive = false), Description("Add Time off for a Person. Time off of the Person that overlaps or touches it is merged in. Each part is \"morning\" or \"afternoon\"." + TimeOffRights)]
    public static async Task<string> AddTimeOff(
        [Description(PersonIdText)] Guid personId,
        [Description(FirstDay)] DateOnly startDate, [Description(DayPart)] string startPart,
        [Description(LastDay)] DateOnly endDate, [Description(DayPart)] string endPart,
        HeadroomDbContext db, IHttpContextAccessor http) =>
        Done((await TimeOffWrites.SaveAsync(db, McpWriteGuard.RequireCanChange(http), null, personId, new(startDate, startPart, endDate, endPart))).Refusal, "Time off added.");

    [McpServerTool(Name = "change_time_off", Destructive = true), Description("Change the dates of Time off. The Time off id is in get_schedule." + TimeOffRights)]
    public static async Task<string> ChangeTimeOff(
        [Description("Id of the Time off")] Guid id,
        [Description(FirstDay)] DateOnly startDate, [Description(DayPart)] string startPart,
        [Description(LastDay)] DateOnly endDate, [Description(DayPart)] string endPart,
        HeadroomDbContext db, IHttpContextAccessor http) =>
        Done((await TimeOffWrites.ChangeAsync(db, McpWriteGuard.RequireCanChange(http), id, new(startDate, startPart, endDate, endPart))).Refusal, "Time off changed.");

    [McpServerTool(Name = "delete_time_off", Destructive = true), Description("Delete Time off. The Time off id is in get_schedule." + TimeOffRights)]
    public static async Task<string> DeleteTimeOff([Description("Id of the Time off")] Guid id, HeadroomDbContext db, IHttpContextAccessor http) =>
        Done(await TimeOffWrites.DeleteAsync(db, McpWriteGuard.RequireCanChange(http), id), "Time off deleted.");
}
