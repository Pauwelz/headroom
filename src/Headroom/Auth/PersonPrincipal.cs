using System.Security.Claims;
using Headroom.Data;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Headroom.Auth;

public static class PersonPrincipal
{
    public const string PlannerRole = "Planner";

    public static ClaimsPrincipal Create(Person person, string scheme = CookieAuthenticationDefaults.AuthenticationScheme,
        params Claim[] extra)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, person.Id.ToString()) };
        if (person.IsPlanner) claims.Add(new Claim(ClaimTypes.Role, PlannerRole));
        claims.AddRange(extra);
        return new ClaimsPrincipal(new ClaimsIdentity(claims, scheme));
    }

    public static Guid? PersonId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
