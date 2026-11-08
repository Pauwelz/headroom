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
        var name = request.Name?.Trim() ?? "";
        var email = request.Email?.Trim() ?? "";
        if (name.Length is 0 or > 200) return Problem(400, "Enter a name of 1 to 200 characters.");
        if (!FirstRunEndpoints.IsEmail(email)) return Problem(400, "Enter a valid email address.");
        if (request.Hours is not { IsValid: true }) return Problem(400, HoursMessage);
        if (await EmailTaken(db, email, null)) return Problem(409, EmailTakenMessage);

        var by = http.User.PersonId();
        var person = new Person { Name = name, Email = email, IsPlanner = request.IsPlanner, LastChangedBy = by };
        var contract = NewContract(person.Id, request.StartDate, request.Hours, by);
        db.People.Add(person);
        db.Contracts.Add(contract);
        await db.SaveChangesAsync();
        return Results.Created($"/api/people/{person.Id}", await ToResponseAsync(db, person, [contract], BrusselsTime.Today(time)));
    }

    private static async Task<IResult> Edit(Guid id, EditPersonRequest request, HttpContext http, HeadroomDbContext db, TimeProvider time)
    {
        var person = await db.People.FindAsync(id);
        if (person is null) return Results.NotFound();
        if (person.ErasedNumber is not null) return Problem(409, PersonLockRules.ErasedDetails);
        var name = request.Name?.Trim() ?? "";
        var email = request.Email?.Trim() ?? "";
        if (name.Length is 0 or > 200) return Problem(400, "Enter a name of 1 to 200 characters.");
        if (!FirstRunEndpoints.IsEmail(email)) return Problem(400, "Enter a valid email address.");
        if (await EmailTaken(db, email, id)) return Problem(409, EmailTakenMessage);

        var today = BrusselsTime.Today(time);
        if (!request.IsPlanner && await LastPlanner.IsAsync(db, person, today)) return Problem(409, LastPlanner.Message);

        person.Name = name;
        person.Email = email;
        person.IsPlanner = request.IsPlanner;
        person.LastChangedBy = http.User.PersonId();
        await db.SaveChangesAsync();
        return Results.Ok(await ToResponseAsync(db, person, today));
    }

    private static async Task<IResult> AddContract(Guid id, ContractRequest request, HttpContext http, HeadroomDbContext db)
    {
        var person = await db.People.FindAsync(id);
        if (person is null) return Results.NotFound();
        if (await ContractProblem(db, person, null, request) is { } problem) return problem;

        var contract = NewContract(id, request.StartDate, request.Hours, http.User.PersonId());
        db.Contracts.Add(contract);
        await db.SaveChangesAsync();
        return Results.Created($"/api/people/{id}", await ToContractResponse(db, contract));
    }

    private static async Task<IResult> EditContract(
        Guid id, Guid contractId, ContractRequest request, HttpContext http, HeadroomDbContext db)
    {
        var person = await db.People.FindAsync(id);
        var contract = await db.Contracts.FirstOrDefaultAsync(c => c.Id == contractId && c.PersonId == id);
        if (person is null || contract is null) return Results.NotFound();
        if (await ContractProblem(db, person, contractId, request) is { } problem) return problem;

        contract.StartDate = request.StartDate;
        SetHours(contract, request.Hours);
        contract.LastChangedBy = http.User.PersonId();
        await db.SaveChangesAsync();
        return Results.Ok(await ToContractResponse(db, contract));
    }

    private static async Task<IResult> DeleteContract(Guid id, Guid contractId, HeadroomDbContext db)
    {
        var contract = await db.Contracts.FirstOrDefaultAsync(c => c.Id == contractId && c.PersonId == id);
        if (contract is null) return Results.NotFound();
        if (await db.Contracts.CountAsync(c => c.PersonId == id) == 1)
            return Problem(409, "A Person keeps at least one Contract. Add another Contract first.");

        db.Contracts.Remove(contract);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

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

    private const string HoursMessage = "Hours are 0 to 24 per day, in steps of 0.25 hour.";
    private const string EmailTakenMessage = "Another Person already has this email.";

    // The database collation ignores case, so this compare ignores case too.
    private static Task<bool> EmailTaken(HeadroomDbContext db, string email, Guid? exceptId) =>
        db.People.AnyAsync(p => p.Email == email && p.Id != exceptId);

    // Null means the request is fine.
    private static async Task<IResult?> ContractProblem(HeadroomDbContext db, Person person, Guid? contractId, ContractRequest request)
    {
        if (request.Hours is not { IsValid: true }) return Problem(400, HoursMessage);
        if (person.EndDate is { } end && request.StartDate > end)
            return Problem(409, "A Contract cannot start after the end date. Use Rehire.");
        if (await db.Contracts.AnyAsync(c => c.PersonId == person.Id && c.Id != contractId && c.StartDate == request.StartDate))
            return Problem(409, "This Person already has a Contract that starts on this date.");
        return null;
    }

    internal static Contract NewContract(Guid personId, DateOnly start, WeekHours hours, Guid? by)
    {
        var contract = new Contract { PersonId = personId, StartDate = start, LastChangedBy = by };
        SetHours(contract, hours);
        return contract;
    }

    private static void SetHours(Contract c, WeekHours h)
    {
        c.HoursMonday = h.Monday;
        c.HoursTuesday = h.Tuesday;
        c.HoursWednesday = h.Wednesday;
        c.HoursThursday = h.Thursday;
        c.HoursFriday = h.Friday;
        c.HoursSaturday = h.Saturday;
        c.HoursSunday = h.Sunday;
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
