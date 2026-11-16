using System.Security.Claims;
using Headroom.Data;
using Headroom.Endpoints;
using Headroom.People;
using Headroom.Telemetry;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Headroom.Auth;

// What happens when Entra ID sends the browser back. The handler has then checked the ID token.
public static class CompanyLoginCallback
{
    // Any number: one fixed key makes two first logins at once take turns.
    private const long FirstPlannerLock = 7_600_001;

    public static async Task TicketReceived(TicketReceivedContext context)
    {
        context.HandleResponse();
        var http = context.HttpContext;
        var db = http.RequestServices.GetRequiredService<HeadroomDbContext>();
        var time = http.RequestServices.GetRequiredService<TimeProvider>();
        var tenant = Guid.Parse(http.RequestServices.GetRequiredService<IOptions<EntraIdOptions>>().Value.TenantId);
        var identity = CompanyIdentity.FromClaims(claim => context.Principal?.FindFirstValue(claim));
        // Only the configured tenant may log in.
        if (identity is not null && !(Guid.TryParse(identity.Issuer, out var tid) && tid == tenant)) identity = null;

        var today = BrusselsTime.Today(time);
        var person = identity is null ? null
            : await db.InstanceSettings.AnyAsync() ? await identity.FindPersonAsync(db) : await AddFirstPlannerAsync(db, identity, today);
        // Every refusal gives one outcome: the answer never says whether an email is known.
        if (person is null || !person.CanLogIn(today))
        {
            HeadroomMetrics.Login(success: false);
            http.Response.Redirect($"/login?companyLogin={CompanyLoginOutcome.Refused}");
            return;
        }
        await db.SaveChangesAsync(); // keeps the identifier of a first login
        await LoginEndpoints.SignInAsync(http, person);
        http.Response.Redirect(LoginEndpoints.SafeReturnUrl(context.ReturnUri));
    }

    public static Task RemoteFailure(RemoteFailureContext context)
    {
        context.HandleResponse();
        context.Response.Redirect($"/login?companyLogin={CompanyLoginOutcome.Provider}");
        return Task.CompletedTask;
    }

    // The first person of the tenant who logs in on an empty Instance becomes the first Planner.
    // The lock and the second check make sure that only one login does it.
    private static async Task<Person?> AddFirstPlannerAsync(HeadroomDbContext db, CompanyIdentity identity, DateOnly today)
    {
        if (identity.Email is not { } email || !PeopleWrites.IsEmail(email)) return null;
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({FirstPlannerLock})");
        if (await db.InstanceSettings.AnyAsync()) return null;

        var name = identity.Name ?? email;
        var person = new Person
        {
            Name = name.Length > 200 ? name[..200] : name, Email = email, IsPlanner = true,
            CompanyLoginIssuer = identity.Issuer, CompanyLoginSubject = identity.Subject,
        };
        // The first Planner is the "last changed by" of every record of the first login.
        person.LastChangedBy = person.Id;
        db.InstanceSettings.Add(new InstanceSettings { LastChangedBy = person.Id });
        db.People.Add(person);
        db.Contracts.Add(new Contract
        {
            PersonId = person.Id, StartDate = today, LastChangedBy = person.Id,
            HoursMonday = 8, HoursTuesday = 8, HoursWednesday = 8, HoursThursday = 8, HoursFriday = 8,
        });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return person;
    }
}
