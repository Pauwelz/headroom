using Microsoft.AspNetCore.DataProtection;
using Headroom.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace Headroom.Auth;

public static class AuthSetup
{
    private const string DefaultScheme = "Headroom";

    public static void AddHeadroomAuth(this IServiceCollection services)
    {
        services.AddDataProtection().PersistKeysToDbContext<HeadroomDbContext>();
        services.AddSingleton<DbTicketStore>();
        services.AddScoped<LoginCodeService>();
        // "Headroom" picks the bearer selector when the request has a bearer header, else the cookie.
        services.AddAuthentication(o =>
        {
            o.DefaultScheme = DefaultScheme;
            o.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        })
        .AddPolicyScheme(DefaultScheme, null, o => o.ForwardDefaultSelector = ctx =>
            AccessTokenAuth.HasBearerHeader(ctx.Request) ? OAuthAuth.BearerScheme : CookieAuthenticationDefaults.AuthenticationScheme)
        .AddPolicyScheme(OAuthAuth.BearerScheme, null, o => o.ForwardDefaultSelector = ctx => OAuthAuth.SelectBearerScheme(ctx.Request))
        .AddScheme<AuthenticationSchemeOptions, OAuthAuth.Handler>(OAuthAuth.Scheme, null)
        .AddScheme<AuthenticationSchemeOptions, AccessTokenAuth.Handler>(AccessTokenAuth.Scheme, null)
        .AddCookie(o =>
        {
            o.Cookie.Name = "headroom.auth";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Lax;
            o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            o.ExpireTimeSpan = TimeSpan.FromDays(30);
            o.SlidingExpiration = true;
            o.LoginPath = "/login";
            o.Events.OnValidatePrincipal = ValidatePerson;
            // The client calls /api and needs a 401 or 403, not a redirect to a page.
            o.Events.OnRedirectToLogin = ctx => ApiStatusOrRedirect(ctx, StatusCodes.Status401Unauthorized);
            o.Events.OnRedirectToAccessDenied = ctx => ApiStatusOrRedirect(ctx, StatusCodes.Status403Forbidden);
        });
        services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
            .Configure<DbTicketStore>((o, store) => o.SessionStore = store);
        services.AddAuthorization(o =>
        {
            o.AddPlannerPolicy();
            o.AddPolicy(AccessTokenAuth.NoTokenPolicy, p => p.RequireAuthenticatedUser().RequireAssertion(c => !c.User.IsAccessToken()));
        });
    }

    private static Task ApiStatusOrRedirect(RedirectContext<CookieAuthenticationOptions> ctx, int status)
    {
        if (ctx.Request.Path.StartsWithSegments("/api") || ctx.Request.Path.StartsWithSegments(Mcp.McpSetup.Path)) ctx.Response.StatusCode = status;
        else ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    }

    // Runs on every request: the Person must still be active, and the Planner flag is read again.
    private static async Task ValidatePerson(CookieValidatePrincipalContext context)
    {
        var services = context.HttpContext.RequestServices;
        var db = services.GetRequiredService<HeadroomDbContext>();
        var id = context.Principal?.PersonId();
        var person = id is null ? null : await db.People.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (person is null || !person.CanLogIn(BrusselsTime.Today(services.GetRequiredService<TimeProvider>())))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync();
            return;
        }
        context.ReplacePrincipal(PersonPrincipal.Create(person));
    }
}
