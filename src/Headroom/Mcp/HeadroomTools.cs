using System.ComponentModel;
using Headroom.Allocations;
using Headroom.Capacity;
using Headroom.Client.Api;
using Headroom.Data;
using Headroom.Endpoints;
using Headroom.Utilisation;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Headroom.Mcp;

// Tools. Each one calls the query code of the matching endpoint, in process.
[McpServerToolType]
public sealed class HeadroomTools
{
    private const int WeeklyUntilDays = 62;
    private const string FirstDay = "First day, yyyy-MM-dd";
    private const string LastDay = "Last day, inclusive, yyyy-MM-dd";
    private const string DayPart = "\"morning\" or \"afternoon\"";
    private const string PersonIdText = "Id of the Person";
    private const string TimeOffRights = " Needs an Access token or Connection with Can change. A Person can change their own Time off, a Planner the Time off of every Person.";
    private const string Percentage = "5 to 100, in steps of 5";

    [McpServerTool(Name = "get_schedule", ReadOnly = true), Description("The schedule: per Person, the capacity, hours, Allocations and Time off for each day. The range is at most 140 days.")]
    public static async Task<ScheduleResponse> GetSchedule(
        [Description(FirstDay)] DateOnly from, [Description(LastDay)] DateOnly to,
        HeadroomDbContext db, IPublicHolidays holidays, TimeProvider time,
        [Description("Part of a Person's name")] string? person = null)
    {
        var (schedule, error) = await ScheduleEndpoints.QueryAsync(from, to, db, holidays, time);
        if (schedule is null) throw new McpException(error!);
        return person is null ? schedule
            : schedule with { People = schedule.People.Where(p => p.Name.Contains(person, StringComparison.CurrentCultureIgnoreCase)).ToList() };
    }

    [McpServerTool(Name = "get_utilisation", ReadOnly = true), Description("Utilisation per Person and per Project. Weekly when the range is 62 days or fewer, else monthly. The range is at most 190 days.")]
    public static async Task<UtilisationResponse> GetUtilisation(
        [Description(FirstDay)] DateOnly from, [Description(LastDay)] DateOnly to,
        HeadroomDbContext db, IPublicHolidays holidays, TimeProvider time)
    {
        var period = to.DayNumber - from.DayNumber < WeeklyUntilDays ? UtilisationBuilder.Week : UtilisationBuilder.Month;
        var (report, error) = await UtilisationEndpoints.QueryAsync(from, to, period, db, holidays, time);
        return report ?? throw new McpException(error!);
    }

    [McpServerTool(Name = "find_headroom", ReadOnly = true), Description("The People with free hours in a period: Capacity minus allocated hours, more than 'hours' (default above zero). Tentative Allocations count as allocated. The range is at most 190 days. Erased People are left out.")]
    public static async Task<List<PersonHeadroom>> FindHeadroom(
        [Description(FirstDay)] DateOnly from, [Description(LastDay)] DateOnly to,
        HeadroomDbContext db, IPublicHolidays holidays, TimeProvider time,
        [Description("Minimum free hours, exclusive")] decimal hours = 0)
    {
        var (report, error) = await UtilisationEndpoints.QueryAsync(from, to, UtilisationBuilder.Month, db, holidays, time);
        var erased = (await db.People.AsNoTracking().Where(p => p.ErasedNumber != null).Select(p => p.Id).ToListAsync()).ToHashSet();
        return HeadroomFinder.Find(report ?? throw new McpException(error!), hours, erased);
    }

    [McpServerTool(Name = "list_projects", ReadOnly = true), Description("The Projects of all Clients, with their status and dates.")]
    public static async Task<List<ProjectResponse>> ListProjects(
        HeadroomDbContext db, [Description("tentative, confirmed or archived")] string? status = null) =>
        (await ProjectEndpoints.ListClientsAsync(db)).SelectMany(c => c.Projects)
            .Where(p => status is null || p.Status.Equals(status, StringComparison.OrdinalIgnoreCase)).ToList();

    [McpServerTool(Name = "get_me", ReadOnly = true), Description("The Person that this Access token acts for: id, name and whether they are a Planner.")]
    public static async Task<MeResponse> GetMe(IHttpContextAccessor http, HeadroomDbContext db) =>
        MeEndpoints.ToResponse(await MeEndpoints.OfCallerAsync(http.HttpContext!, db));

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

    [McpServerTool(Name = "delete_allocation", Destructive = true), Description("Delete an Allocation. The Allocation id is in get_schedule. Needs an Access token of a Planner with Can change.")]
    public static async Task<string> DeleteAllocation([Description("Id of the Allocation")] Guid id, HeadroomDbContext db, IHttpContextAccessor http)
    {
        McpWriteGuard.PlannerId(http);
        return Done(await AllocationWrites.DeleteAsync(db, id), "Allocation deleted.");
    }

    [McpServerTool(Name = "add_time_off", Destructive = false), Description("Add Time off for a Person. Time off of the Person that overlaps or touches it is merged in. Each part is \"morning\" or \"afternoon\"." + TimeOffRights)]
    public static async Task<string> AddTimeOff(
        [Description(PersonIdText)] Guid personId,
        [Description(FirstDay)] DateOnly startDate, [Description(DayPart)] string startPart,
        [Description(LastDay)] DateOnly endDate, [Description(DayPart)] string endPart,
        HeadroomDbContext db, IHttpContextAccessor http) =>
        Done(await TimeOffWrites.SaveAsync(db, McpWriteGuard.RequireCanChange(http), null, personId, new(startDate, startPart, endDate, endPart)), "Time off added.");

    [McpServerTool(Name = "change_time_off", Destructive = true), Description("Change the dates of Time off. The Time off id is in get_schedule." + TimeOffRights)]
    public static async Task<string> ChangeTimeOff(
        [Description("Id of the Time off")] Guid id,
        [Description(FirstDay)] DateOnly startDate, [Description(DayPart)] string startPart,
        [Description(LastDay)] DateOnly endDate, [Description(DayPart)] string endPart,
        HeadroomDbContext db, IHttpContextAccessor http) =>
        Done(await TimeOffWrites.ChangeAsync(db, McpWriteGuard.RequireCanChange(http), id, new(startDate, startPart, endDate, endPart)), "Time off changed.");

    [McpServerTool(Name = "delete_time_off", Destructive = true), Description("Delete Time off. The Time off id is in get_schedule." + TimeOffRights)]
    public static async Task<string> DeleteTimeOff([Description("Id of the Time off")] Guid id, HeadroomDbContext db, IHttpContextAccessor http) =>
        Done(await TimeOffWrites.DeleteAsync(db, McpWriteGuard.RequireCanChange(http), id), "Time off deleted.");

    private static string Done((ScheduleTimeOff? TimeOff, Refusal? Refusal) result, string message) => Done(result.Refusal, message);

    // A refusal becomes a tool error with the message of the API.
    private static string Done(Refusal? refusal, string message) => refusal is null ? message : throw new McpException(refusal.Detail);
}
