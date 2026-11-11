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

// MCP tools for schedule and utilisation reads. Each one calls the code of the matching endpoint, in process.
[McpServerToolType]
public sealed class ScheduleTools
{
    private const int WeeklyUntilDays = 62;

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

    [McpServerTool(Name = "find_headroom", ReadOnly = true), Description("The People with free hours in a period: Capacity minus allocated hours, more than 'hours' (default above zero). Tentative Allocations count as allocated. The range is at most 190 days.")]
    public static async Task<List<PersonHeadroom>> FindHeadroom(
        [Description(FirstDay)] DateOnly from, [Description(LastDay)] DateOnly to,
        HeadroomDbContext db, IPublicHolidays holidays, TimeProvider time,
        [Description("Minimum free hours, exclusive")] decimal hours = 0)
    {
        var (report, error) = await UtilisationEndpoints.QueryAsync(from, to, UtilisationBuilder.Month, db, holidays, time);
        return HeadroomFinder.Find(report ?? throw new McpException(error!), hours);
    }

    [McpServerTool(Name = "get_me", ReadOnly = true), Description("The Person that this Access token acts for: id, name and whether they are a Planner.")]
    public static async Task<MeResponse> GetMe(IHttpContextAccessor http, HeadroomDbContext db) =>
        MeEndpoints.ToResponse(await MeEndpoints.OfCallerAsync(http.HttpContext!, db));
}
