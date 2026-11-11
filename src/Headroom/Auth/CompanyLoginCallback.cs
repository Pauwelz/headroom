using System.Security.Claims;
using Headroom.Client.Api;
using Headroom.Data;
using Headroom.Endpoints;
using Headroom.Telemetry;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Headroom.Auth;

// What happens when the provider sends the browser back. The handler has then checked the ID token.
// Three cases: a normal login, a test login of a Planner in Settings, a test login on the first-run screen.
public static class CompanyLoginCallback
{
    public static async Task TicketReceived(TicketReceivedContext context)
    {
        context.HandleResponse();
        var http = context.HttpContext;
        var pending = CompanyLogin.Pending.Read(http);
        CompanyLogin.Pending.Delete(http);
        var db = http.RequestServices.GetRequiredService<HeadroomDbContext>();
        var kind = pending?.Kind ?? await db.InstanceSettings.AsNoTracking().Select(s => s.CompanyLoginKind).FirstOrDefaultAsync();
        var identity = kind is { } k ? CompanyIdentity.FromClaims(k, claim => context.Principal?.FindFirstValue(claim)) : null;

        var time = http.RequestServices.GetRequiredService<TimeProvider>();
        var today = BrusselsTime.Today(time);
        var url = pending switch
        {
            { PersonId: { } planner } => await SaveSettingsAsync(http, db, pending, planner, identity, today),
            { CompanyName: { } company } => await FirstRunAsync(http, db, pending, company, identity, time),
            _ => await LoginAsync(http, db, LoginEndpoints.SafeReturnUrl(context.ReturnUri), identity, today, time),
        };
        http.Response.Redirect(url);
    }

    public static Task RemoteFailure(RemoteFailureContext context)
    {
        context.HandleResponse();
        var pending = CompanyLogin.Pending.Read(context.HttpContext);
        CompanyLogin.Pending.Delete(context.HttpContext);
        context.Response.Redirect(Back(pending, CompanyLoginOutcome.Provider));
        return Task.CompletedTask;
    }

    // Each case returns to the page it came from, with the outcome in the query.
    private static string Back(CompanyLogin.Pending? pending, string outcome) =>
        $"{(pending is null ? "/login" : pending.PersonId is not null ? "/settings" : "/first-run")}?companyLogin={outcome}";

    private static async Task<string> LoginAsync(HttpContext http, HeadroomDbContext db, string returnUrl, CompanyIdentity? identity, DateOnly today, TimeProvider time)
    {
        var person = identity is null ? null : await identity.FindPersonAsync(db);
        // Every refusal gives one outcome: the answer never says whether an email is known.
        if (person is null || person.ErasedNumber is not null || !person.CanLogIn(today))
        {
            HeadroomMetrics.Login(success: false);
            return Back(null, CompanyLoginOutcome.Refused);
        }
        await db.SaveChangesAsync(); // keeps the identifier of a first login
        await CompanyLogin.SignInAsync(http, person, time);
        return returnUrl;
    }

    private static async Task<string> SaveSettingsAsync(HttpContext http, HeadroomDbContext db, CompanyLogin.Pending pending, Guid plannerId, CompanyIdentity? identity, DateOnly today)
    {
        var planner = await db.People.FirstOrDefaultAsync(p => p.Id == plannerId);
        if (planner is not { IsPlanner: true } || !planner.CanLogIn(today)) return Back(pending, CompanyLoginOutcome.NotYou);
        if (identity is null) return Back(pending, CompanyLoginOutcome.NoEmail);
        // The test login must be the Planner who saves. Another Person, or nobody, keeps the old settings.
        if (await identity.FindPersonAsync(db) is not { } person || person.Id != planner.Id) return Back(pending, CompanyLoginOutcome.NotYou);

        var row = await db.InstanceSettings.FirstAsync();
        var protection = http.RequestServices.GetRequiredService<IDataProtectionProvider>();
        (row.CompanyLoginKind, row.CompanyLoginIssuer, row.CompanyLoginClientId) = (pending.Kind, pending.Issuer, pending.ClientId);
        row.CompanyLoginClientSecret = CompanyLogin.ProtectSecret(pending.ClientSecret, protection);
        row.LastChangedBy = plannerId;
        await db.SaveChangesAsync();
        return $"/settings?companyLogin={CompanyLoginOutcome.Saved}";
    }

    private static async Task<string> FirstRunAsync(HttpContext http, HeadroomDbContext db, CompanyLogin.Pending pending, string company, CompanyIdentity? identity, TimeProvider time)
    {
        if (await db.InstanceSettings.AnyAsync()) return "/login";
        if (identity?.Email is not { } email || !FirstRunEndpoints.IsEmail(email)) return Back(pending, CompanyLoginOutcome.NoEmail);

        var name = identity.Name ?? email;
        var person = new Person
        {
            Name = name.Length > 200 ? name[..200] : name, Email = email, IsPlanner = true,
            CompanyLoginIssuer = identity.Issuer, CompanyLoginSubject = identity.Subject,
        };
        person.LastChangedBy = person.Id;
        var settings = new InstanceSettings
        {
            CompanyName = company, LastChangedBy = person.Id,
            CompanyLoginKind = pending.Kind, CompanyLoginIssuer = pending.Issuer, CompanyLoginClientId = pending.ClientId,
            CompanyLoginClientSecret = CompanyLogin.ProtectSecret(pending.ClientSecret, http.RequestServices.GetRequiredService<IDataProtectionProvider>()),
        };
        db.InstanceSettings.Add(settings);
        db.People.Add(person);
        db.Contracts.Add(new Contract
        {
            PersonId = person.Id, StartDate = BrusselsTime.Today(time), LastChangedBy = person.Id,
            HoursMonday = 8, HoursTuesday = 8, HoursWednesday = 8, HoursThursday = 8, HoursFriday = 8,
        });
        await db.SaveChangesAsync();
        await CompanyLogin.SignInAsync(http, person, time);
        return "/";
    }
}
