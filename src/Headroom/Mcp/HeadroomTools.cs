using System.ComponentModel;
using Headroom.Allocations;
using Headroom.Capacity;
using Headroom.Client.Api;
using Headroom.Data;
using Headroom.Endpoints;
using Headroom.Projects;
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
    private const string PlannerRights = " Needs an Access token of a Planner with Can change.";
    private const string ClientName = "Name of the Client, 1 to 200 characters";
    private const string CompanyDayOffRights = " Needs an Access token or Connection of a Planner with Can change.";
    private const string CompanyDayOffDate = "The date, yyyy-MM-dd";
    private const string CompanyDayOffName = "The name, 1 to 200 characters";
    private const string CompanyDayOffId = "Id of the Company day off";
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

    [McpServerTool(Name = "list_people", ReadOnly = true), Description("Every Person with status, Contracts (with their ids), hours per week and end date. Gives the Person ids. Needs an Access token of a Planner. Can change is not needed.")]
    public static async Task<List<PersonResponse>> ListPeople(HeadroomDbContext db, TimeProvider time, IHttpContextAccessor http)
    {
        McpReadGuard.RequirePlanner(http);
        return await PeopleEndpoints.ListAsync(db, time);
    }

    [McpServerTool(Name = "list_company_days_off", ReadOnly = true), Description("The Company days off and public holidays of a year, with the Company day off ids. Needs an Access token of a Planner. Can change is not needed.")]
    public static async Task<List<DayOffResponse>> ListCompanyDaysOff([Description("The year, for example 2026")] int year, HeadroomDbContext db, IPublicHolidays holidays, IHttpContextAccessor http)
    {
        McpReadGuard.RequirePlanner(http);
        var (days, refusal) = await SettingsEndpoints.ListYearAsync(year, db, holidays);
        return days ?? throw new McpException(refusal!.Detail);
    }

    [McpServerTool(Name = "add_company_day_off", Destructive = false), Description("Add a Company day off. The Capacity of every Person is zero on that date. Only one Company day off per date." + CompanyDayOffRights)]
    public static async Task<string> AddCompanyDayOff([Description(CompanyDayOffDate)] DateOnly date, [Description(CompanyDayOffName)] string name, HeadroomDbContext db, IHttpContextAccessor http) =>
        Done(await CompanyDaysOffWrites.AddAsync(db, McpWriteGuard.PlannerId(http), new(date, name)), "Company day off added.");

    [McpServerTool(Name = "change_company_day_off", Destructive = true), Description("Change the date and name of a Company day off. The id is in list_company_days_off." + CompanyDayOffRights)]
    public static async Task<string> ChangeCompanyDayOff([Description(CompanyDayOffId)] Guid id, [Description(CompanyDayOffDate)] DateOnly date, [Description(CompanyDayOffName)] string name, HeadroomDbContext db, IHttpContextAccessor http) =>
        Done(await CompanyDaysOffWrites.ChangeAsync(db, McpWriteGuard.PlannerId(http), id, new(date, name)), "Company day off changed.");

    [McpServerTool(Name = "delete_company_day_off", Destructive = true), Description("Delete a Company day off. The id is in list_company_days_off." + CompanyDayOffRights)]
    public static async Task<string> DeleteCompanyDayOff([Description(CompanyDayOffId)] Guid id, HeadroomDbContext db, IHttpContextAccessor http)
    {
        McpWriteGuard.PlannerId(http);
        return Done(await CompanyDaysOffWrites.DeleteAsync(db, id), "Company day off deleted.");
    }

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

    [McpServerTool(Name = "split_allocation", Destructive = true), Description("Split an Allocation on a date. The first part ends the day before the date. The second part starts on the date. Both parts keep the Person, Project and percentage. The Allocation id is in get_schedule. Needs an Access token of a Planner with Can change.")]
    public static async Task<string> SplitAllocation(
        [Description("Id of the Allocation")] Guid id,
        [Description("First day of the second part. After the start date and not after the end date of the Allocation")] DateOnly date,
        HeadroomDbContext db, IHttpContextAccessor http) =>
        Done(await AllocationWrites.SplitAsync(db, McpWriteGuard.PlannerId(http), id, new(date)), "Allocation split.");

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

    private static string Done((DayOffResponse? Day, Refusal? Refusal) result, string message) => Done(result.Refusal, message);

    // A refusal becomes a tool error with the message of the API.
    private static string Done(Refusal? refusal, string message) => refusal is null ? message : throw new McpException(refusal.Detail);
}
