using Microsoft.AspNetCore.Antiforgery;

namespace Headroom.Auth;

// Every write to /api needs the antiforgery token in a header.
// /api/me hands the token to the client in a cookie and in the same-named response header.
// The WebAssembly client reads the header, so it needs no JavaScript to read the cookie.
public static class ApiAntiforgery
{
    public const string CookieName = "XSRF-TOKEN";
    public const string HeaderName = Headroom.Client.Api.AntiforgeryToken.HeaderName; // one name for client and server

    public static void AddApiAntiforgery(this IServiceCollection services) =>
        services.AddAntiforgery(o => o.HeaderName = HeaderName);

    public static void UseApiAntiforgery(this IApplicationBuilder app) =>
        app.Use(async (http, next) =>
        {
            var safe = HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method)
                || HttpMethods.IsOptions(http.Request.Method);
            if (!safe && http.Request.Path.StartsWithSegments("/api"))
            {
                // A bearer call has no cookie to forge, so it needs no antiforgery token. It needs "Can change".
                if (http.User.IsAccessToken())
                {
                    if (http.User.CanChange()) { await next(); return; }
                    http.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
                try { await http.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(http); }
                catch (AntiforgeryValidationException)
                {
                    http.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }
            }
            await next();
        });

    public static void IssueToken(HttpContext http)
    {
        var token = http.RequestServices.GetRequiredService<IAntiforgery>().GetAndStoreTokens(http).RequestToken!;
        http.Response.Cookies.Append(CookieName, token, new CookieOptions { Secure = true, SameSite = SameSiteMode.Strict });
        http.Response.Headers[HeaderName] = token;
    }
}
