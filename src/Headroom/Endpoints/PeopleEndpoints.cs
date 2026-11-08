using Headroom.Auth;
using Headroom.Client.Api;
using Headroom.Data;
using Headroom.People;
using Microsoft.EntityFrameworkCore;
using static Headroom.Endpoints.ApiProblem;

namespace Headroom.Endpoints;

// People page. Planners only: the server enforces it.
public static class PeopleEndpoints
{
    public static void MapPeopleEndpoints(this RouteGroupBuilder api)
    {
        var people = api.MapGroup("/people").RequirePlanner();
        people.MapGet("", async (HeadroomDbContext db, TimeProvider time) => Results.Ok(await ListAsync(db, time)));
        people.MapPost("", Add);
        people.MapPut("/{id:guid}", Edit);
        people.MapPost("/{id:guid}/contracts", AddContract);
        people.MapPut("/{id:guid}/contracts/{contractId:guid}", EditContract);
        people.MapDelete("/{id:guid}/contracts/{contractId:guid}", DeleteContract);
        people.MapEndDateEndpoints();
        people.MapRehireAndDeleteEndpoints();

        people.MapPost("/{id:guid}/erase", Erase);
    }

    public static async Task<List<PersonResponse>> ListAsync(HeadroomDbContext db, TimeProvider time)
    {
        var contracts = (await db.Contracts.AsNoTracking().ToListAsync()).ToLookup(c => c.PersonId);
        var everyone = await db.People.AsNoTracking().ToListAsync();
        var today = BrusselsTime.Today(time);
        var rows = new List<PersonResponse>();
        foreach (var p in everyone) rows.Add(await ToResponseAsync(db, p, contracts[p.Id], today));
        return rows;
    }

    private static async Task<IResult> Add(AddPersonRequest request, HttpContext http, HeadroomDbContext db, TimeProvider time)
    {
        var (person, contract, refusal) = await PeopleWrites.AddPersonAsync(db, http.User.PersonId(), request);
        if (refusal is not null) return Refused(refusal);
        return Results.Created($"/api/people/{person!.Id}", await ToResponseAsync(db, person, [contract!], BrusselsTime.Today(time)));
    }

    private static async Task<IResult> Edit(Guid id, EditPersonRequest request, HttpContext http, HeadroomDbContext db, TimeProvider time)
    {
        var today = BrusselsTime.Today(time);
        var (person, refusal) = await PeopleWrites.EditPersonAsync(db, http.User.PersonId(), id, request, today);
        return refusal is not null ? Refused(refusal) : Results.Ok(await ToResponseAsync(db, person!, today));
    }

    private static async Task<IResult> AddContract(Guid id, ContractRequest request, HttpContext http, HeadroomDbContext db)
    {
        var (contract, refusal) = await PeopleWrites.AddContractAsync(db, http.User.PersonId(), id, request);
        return refusal is not null ? Refused(refusal) : Results.Created($"/api/people/{id}", await ToContractResponse(db, contract!));
    }

    private static async Task<IResult> EditContract(
        Guid id, Guid contractId, ContractRequest request, HttpContext http, HeadroomDbContext db)
    {
        var (contract, refusal) = await PeopleWrites.EditContractAsync(db, http.User.PersonId(), id, contractId, request);
        return refusal is not null ? Refused(refusal) : Results.Ok(await ToContractResponse(db, contract!));
    }

    private static async Task<IResult> DeleteContract(Guid id, Guid contractId, HeadroomDbContext db) =>
        await PeopleWrites.DeleteContractAsync(db, id, contractId) is { } refusal ? Refused(refusal) : Results.NoContent();

    // REST answers a missing Person or Contract with an empty 404.
    private static IResult Refused(Refusal refusal) => refusal.Status == 404 ? Results.NotFound() : Problem(refusal);

    // The dialog asks for the "I understand" tick. The result cannot be undone.
    private static async Task<IResult> Erase(Guid id, HttpContext http, HeadroomDbContext db, TimeProvider time)
    {
        var person = await db.People.FindAsync(id);
        if (person is null) return Results.NotFound();
        var today = BrusselsTime.Today(time);
        if (PersonLockRules.Erase(person, await LastPlanner.IsAsync(db, person, today), today) is { } reason) return Problem(409, reason);

        await PersonEraser.EraseAsync(db, person, http.User.PersonId());
        return Results.Ok(await ToResponseAsync(db, person, today));
    }

    private static WeekHours GetHours(Contract c) =>
        new(c.HoursMonday, c.HoursTuesday, c.HoursWednesday, c.HoursThursday, c.HoursFriday, c.HoursSaturday, c.HoursSunday);

    private static async Task<ContractResponse> ToContractResponse(HeadroomDbContext db, Contract contract)
    {
        var all = await db.Contracts.AsNoTracking().Where(c => c.PersonId == contract.PersonId).ToListAsync();
        var endDate = await db.People.Where(p => p.Id == contract.PersonId).Select(p => p.EndDate).FirstAsync();
        return ToResponses(all, endDate).First(c => c.Id == contract.Id);
    }

    // "To" is the day before the next Contract. The last Contract runs to the end date, or stays open.
    private static List<ContractResponse> ToResponses(IEnumerable<Contract> contracts, DateOnly? personEnd)
    {
        var sorted = contracts.OrderBy(c => c.StartDate).ToList();
        return sorted.Select((c, i) =>
        {
            var hours = GetHours(c);
            DateOnly? to = i + 1 < sorted.Count ? sorted[i + 1].StartDate.AddDays(-1) : personEnd;
            return new ContractResponse("Contract", c.Id, c.PersonId, c.StartDate, to, hours, hours.Total, c.LastChangedBy, c.DateModified);
        }).ToList();
    }

    internal static async Task<PersonResponse> ToResponseAsync(HeadroomDbContext db, Person p, DateOnly today) =>
        await ToResponseAsync(db, p, await db.Contracts.AsNoTracking().Where(c => c.PersonId == p.Id).ToListAsync(), today);

    internal static async Task<PersonResponse> ToResponseAsync(HeadroomDbContext db, Person p, IEnumerable<Contract> contracts, DateOnly today)
    {
        var rows = ToResponses(contracts, p.EndDate);
        var now = rows.LastOrDefault(c => c.StartDate <= today);
        var status = PersonStatus.Of(p.ErasedNumber, p.EndDate, rows.FirstOrDefault()?.StartDate, today);
        var locks = PersonLockRules.Of(p, await LastPlanner.IsAsync(db, p, today), today);
        return new PersonResponse("Person", p.Id, p.DisplayName, p.Email, p.IsPlanner, p.EndDate, p.ErasedNumber, status,
            p.CanLogIn(today), now?.HoursPerWeek ?? 0, locks, rows, p.LastChangedBy, p.DateModified);
    }
}
