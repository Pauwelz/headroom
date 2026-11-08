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

// MCP tools for Company days off. Each one calls the code of the matching endpoint, in process.
[McpServerToolType]
public sealed class CompanyDayOffTools
{
    private const string CompanyDayOffRights = " Needs an Access token or Connection of a Planner with Can change.";
    private const string CompanyDayOffDate = "The date, yyyy-MM-dd";
    private const string CompanyDayOffName = "The name, 1 to 200 characters";
    private const string CompanyDayOffId = "Id of the Company day off";

    [McpServerTool(Name = "list_company_days_off", ReadOnly = true), Description("The Company days off and public holidays of a year, with the Company day off ids. Needs an Access token of a Planner. Can change is not needed.")]
    public static async Task<List<DayOffResponse>> ListCompanyDaysOff([Description("The year, for example 2026")] int year, HeadroomDbContext db, IPublicHolidays holidays, IHttpContextAccessor http)
    {
        McpReadGuard.RequirePlanner(http);
        return Value(await SettingsEndpoints.ListYearAsync(year, db, holidays));
    }

    [McpServerTool(Name = "add_company_day_off", Destructive = false), Description("Add a Company day off. The Capacity of every Person is zero on that date. Only one Company day off per date." + CompanyDayOffRights)]
    public static async Task<string> AddCompanyDayOff([Description(CompanyDayOffDate)] DateOnly date, [Description(CompanyDayOffName)] string name, HeadroomDbContext db, IHttpContextAccessor http) =>
        Done((await CompanyDaysOffWrites.AddAsync(db, McpWriteGuard.PlannerId(http), new(date, name))).Refusal, "Company day off added.");

    [McpServerTool(Name = "change_company_day_off", Destructive = true), Description("Change the date and name of a Company day off. The id is in list_company_days_off." + CompanyDayOffRights)]
    public static async Task<string> ChangeCompanyDayOff([Description(CompanyDayOffId)] Guid id, [Description(CompanyDayOffDate)] DateOnly date, [Description(CompanyDayOffName)] string name, HeadroomDbContext db, IHttpContextAccessor http) =>
        Done((await CompanyDaysOffWrites.ChangeAsync(db, McpWriteGuard.PlannerId(http), id, new(date, name))).Refusal, "Company day off changed.");

    [McpServerTool(Name = "delete_company_day_off", Destructive = true), Description("Delete a Company day off. The id is in list_company_days_off." + CompanyDayOffRights)]
    public static async Task<string> DeleteCompanyDayOff([Description(CompanyDayOffId)] Guid id, HeadroomDbContext db, IHttpContextAccessor http) =>
        Done(await CompanyDaysOffWrites.DeleteAsync(db, McpWriteGuard.AfterPlannerCheck(http, id)), "Company day off deleted.");
}
