using Headroom.Auth;
using Headroom.Client.Api;
using Headroom.Data;
using Headroom.Endpoints;
using Microsoft.EntityFrameworkCore;

namespace Headroom.People;

/// <summary>Person and Contract writes with their rules. REST and MCP both call this. A null refusal means the change is done.</summary>
public static class PeopleWrites
{
    private const string HoursMessage = "Hours are 0 to 24 per day, in steps of 0.25 hour.";
    private const string EmailMessage = "Enter a valid email address.";
    private const string EmailTakenMessage = "Another Person already has this email.";

    // REST sends an empty 404 body. MCP sends this text.
    private static readonly Refusal PersonNotFound = new(404, "The Person does not exist.");
    private static readonly Refusal ContractNotFound = new(404, "The Contract does not exist.");

    public static async Task<(Person? Person, Contract? Contract, Refusal? Refusal)> AddPersonAsync(HeadroomDbContext db, Guid? by, AddPersonRequest request)
    {
        var name = request.Name?.Trim() ?? "";
        var email = request.Email?.Trim() ?? "";
        if (name.Length is 0 or > 200) return (null, null, new(400, ApiProblem.NameDetail));
        if (!FirstRunEndpoints.IsEmail(email)) return (null, null, new(400, EmailMessage));
        if (request.Hours is not { IsValid: true }) return (null, null, new(400, HoursMessage));
        if (await EmailTaken(db, email, null)) return (null, null, new(409, EmailTakenMessage));

        var person = new Person { Name = name, Email = email, IsPlanner = request.IsPlanner, LastChangedBy = by };
        var contract = NewContract(person.Id, request.StartDate, request.Hours, by);
        db.People.Add(person);
        db.Contracts.Add(contract);
        await db.SaveChangesAsync();
        return (person, contract, null);
    }

    public static async Task<(Person? Person, Refusal? Refusal)> EditPersonAsync(HeadroomDbContext db, Guid? by, Guid id, EditPersonRequest request, DateOnly today)
    {
        if (await db.People.FindAsync(id) is not { } person) return (null, PersonNotFound);
        if (person.ErasedNumber is not null) return (null, new(409, PersonLockRules.ErasedDetails));
        var name = request.Name?.Trim() ?? "";
        var email = request.Email?.Trim() ?? "";
        if (name.Length is 0 or > 200) return (null, new(400, ApiProblem.NameDetail));
        if (!FirstRunEndpoints.IsEmail(email)) return (null, new(400, EmailMessage));
        if (await EmailTaken(db, email, id)) return (null, new(409, EmailTakenMessage));
        if (!request.IsPlanner && await LastPlanner.IsAsync(db, person, today)) return (null, new(409, LastPlanner.Message));

        person.Name = name;
        person.Email = email;
        person.IsPlanner = request.IsPlanner;
        person.LastChangedBy = by;
        await db.SaveChangesAsync();
        await CompanyLoginOnly.DeletePasskeysAsync(db, person.Id); // the new email may be in a company domain
        return (person, null);
    }

    public static async Task<(Contract? Contract, Refusal? Refusal)> AddContractAsync(HeadroomDbContext db, Guid? by, Guid personId, ContractRequest request)
    {
        if (await db.People.FindAsync(personId) is not { } person) return (null, PersonNotFound);
        if (await ContractRefusal(db, person, null, request) is { } refusal) return (null, refusal);

        var contract = NewContract(personId, request.StartDate, request.Hours, by);
        db.Contracts.Add(contract);
        await db.SaveChangesAsync();
        return (contract, null);
    }

    public static async Task<(Contract? Contract, Refusal? Refusal)> EditContractAsync(
        HeadroomDbContext db, Guid? by, Guid personId, Guid contractId, ContractRequest request)
    {
        var person = await db.People.FindAsync(personId);
        var contract = await db.Contracts.FirstOrDefaultAsync(c => c.Id == contractId && c.PersonId == personId);
        if (person is null) return (null, PersonNotFound);
        if (contract is null) return (null, ContractNotFound);
        if (await ContractRefusal(db, person, contractId, request) is { } refusal) return (null, refusal);

        contract.StartDate = request.StartDate;
        SetHours(contract, request.Hours);
        contract.LastChangedBy = by;
        await db.SaveChangesAsync();
        return (contract, null);
    }

    public static async Task<Refusal?> DeleteContractAsync(HeadroomDbContext db, Guid personId, Guid contractId)
    {
        if (await db.Contracts.FirstOrDefaultAsync(c => c.Id == contractId && c.PersonId == personId) is not { } contract) return ContractNotFound;
        if (await db.Contracts.CountAsync(c => c.PersonId == personId) == 1)
            return new(409, "A Person keeps at least one Contract. Add another Contract first.");

        db.Contracts.Remove(contract);
        await db.SaveChangesAsync();
        return null;
    }

    public static Contract NewContract(Guid personId, DateOnly start, WeekHours hours, Guid? by)
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

    // The database collation ignores case, so this compare ignores case too.
    private static Task<bool> EmailTaken(HeadroomDbContext db, string email, Guid? exceptId) =>
        db.People.AnyAsync(p => p.Email == email && p.Id != exceptId);

    private static async Task<Refusal?> ContractRefusal(HeadroomDbContext db, Person person, Guid? contractId, ContractRequest request)
    {
        if (request.Hours is not { IsValid: true }) return new(400, HoursMessage);
        if (person.EndDate is { } end && request.StartDate > end)
            return new(409, "A Contract cannot start after the end date. Use Rehire.");
        if (await db.Contracts.AnyAsync(c => c.PersonId == person.Id && c.Id != contractId && c.StartDate == request.StartDate))
            return new(409, "This Person already has a Contract that starts on this date.");
        return null;
    }
}
