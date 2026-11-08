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

// MCP tools for People, their Contracts and their end date. Each one calls the code of the matching endpoint, in process.
[McpServerToolType]
public sealed class PeopleTools
{
    private const string LastWorkingDay = "Last working day, yyyy-MM-dd";
    private const string HoursText = "Hours per weekday, Monday to Sunday: each 0 to 24, in steps of 0.25";
    private const string IdsInListPeople = " The Person and Contract ids are in list_people.";

    [McpServerTool(Name = "list_people", ReadOnly = true), Description("Every Person with status, Contracts (with their ids), hours per week and end date. Gives the Person ids. Needs an Access token of a Planner. Can change is not needed.")]
    public static async Task<List<PersonResponse>> ListPeople(HeadroomDbContext db, TimeProvider time, IHttpContextAccessor http)
    {
        McpReadGuard.RequirePlanner(http);
        return await PeopleEndpoints.ListAsync(db, time);
    }

    [McpServerTool(Name = "add_person", Destructive = false), Description("Add a Person with their first Contract." + PlannerRights)]
    public static async Task<string> AddPerson(
        [Description("Name, 1 to 200 characters")] string name, [Description("Email address")] string email,
        [Description("True when the Person is a Planner")] bool isPlanner,
        [Description("Start date of the first Contract, yyyy-MM-dd")] DateOnly startDate,
        [Description(HoursText)] WeekHours hours, HeadroomDbContext db, IHttpContextAccessor http) =>
        Done((await PeopleWrites.AddPersonAsync(db, McpWriteGuard.PlannerId(http), new(name, email, isPlanner, startDate, hours))).Refusal, "Person added.");

    [McpServerTool(Name = "change_person", Destructive = true), Description("Change the name, email and Planner role of a Person." + IdsInListPeople + PlannerRights)]
    public static async Task<string> ChangePerson(
        [Description(PersonIdText)] Guid id, [Description("Name, 1 to 200 characters")] string name, [Description("Email address")] string email,
        [Description("True when the Person is a Planner")] bool isPlanner, HeadroomDbContext db, TimeProvider time, IHttpContextAccessor http) =>
        Done((await PeopleWrites.EditPersonAsync(db, McpWriteGuard.PlannerId(http), id, new(name, email, isPlanner), BrusselsTime.Today(time))).Refusal, "Person changed.");

    [McpServerTool(Name = "add_contract", Destructive = false), Description("Add a Contract to a Person from a start date. The hours count from that date." + IdsInListPeople + PlannerRights)]
    public static async Task<string> AddContract(
        [Description(PersonIdText)] Guid personId, [Description("Start date, yyyy-MM-dd")] DateOnly startDate,
        [Description(HoursText)] WeekHours hours, HeadroomDbContext db, IHttpContextAccessor http) =>
        Done((await PeopleWrites.AddContractAsync(db, McpWriteGuard.PlannerId(http), personId, new(startDate, hours))).Refusal, "Contract added.");

    [McpServerTool(Name = "change_contract", Destructive = true), Description("Change the start date and hours of a Contract." + IdsInListPeople + PlannerRights)]
    public static async Task<string> ChangeContract(
        [Description(PersonIdText)] Guid personId, [Description("Id of the Contract")] Guid contractId,
        [Description("Start date, yyyy-MM-dd")] DateOnly startDate, [Description(HoursText)] WeekHours hours,
        HeadroomDbContext db, IHttpContextAccessor http) =>
        Done((await PeopleWrites.EditContractAsync(db, McpWriteGuard.PlannerId(http), personId, contractId, new(startDate, hours))).Refusal, "Contract changed.");

    [McpServerTool(Name = "delete_contract", Destructive = true), Description("Delete a Contract. A Person keeps at least one Contract." + IdsInListPeople + PlannerRights)]
    public static async Task<string> DeleteContract(
        [Description(PersonIdText)] Guid personId, [Description("Id of the Contract")] Guid contractId, HeadroomDbContext db, IHttpContextAccessor http) =>
        Done(await PeopleWrites.DeleteContractAsync(db, McpWriteGuard.AfterPlannerCheck(http, personId), contractId), "Contract deleted.");

    [McpServerTool(Name = "get_end_date_impact", ReadOnly = true), Description("What an end date cuts for a Person: the Allocations, Time off and Contracts after that date, and whether each one is deleted or shortened. The Person id is in list_people. Needs an Access token of a Planner. Can change is not needed.")]
    public static async Task<EndDateImpact> GetEndDateImpact(
        [Description(PersonIdText)] Guid personId, [Description(LastWorkingDay)] DateOnly endDate,
        HeadroomDbContext db, TimeProvider time, IHttpContextAccessor http)
    {
        McpReadGuard.RequirePlanner(http);
        return Value(await EndDateWrites.ImpactAsync(db, personId, endDate, BrusselsTime.Today(time)));
    }

    [McpServerTool(Name = "set_end_date", Destructive = true), Description("Set the end date of a Person. The Person is Ended after that date. Allocations and Time off after it are deleted, those that run past it end on it, and later Contracts are deleted. Call get_end_date_impact first and show the result to the user. Set the end date only after the user confirms. The Person id is in list_people. Needs an Access token of a Planner with Can change.")]
    public static async Task<string> SetEndDate(
        [Description(PersonIdText)] Guid personId, [Description(LastWorkingDay)] DateOnly endDate,
        HeadroomDbContext db, TimeProvider time, IHttpContextAccessor http) =>
        Done((await EndDateWrites.SetAsync(db, McpWriteGuard.PlannerId(http), personId, endDate, BrusselsTime.Today(time))).Refusal, "End date set.");

    [McpServerTool(Name = "remove_end_date", Destructive = true), Description("Remove the end date of a Person. Records that the end date cut are not restored. The Person id is in list_people. Needs an Access token of a Planner with Can change.")]
    public static async Task<string> RemoveEndDate(
        [Description(PersonIdText)] Guid personId, HeadroomDbContext db, TimeProvider time, IHttpContextAccessor http) =>
        Done((await EndDateWrites.RemoveAsync(db, McpWriteGuard.PlannerId(http), personId, BrusselsTime.Today(time))).Refusal, "End date removed.");
}
