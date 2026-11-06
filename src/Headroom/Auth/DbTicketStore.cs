using Headroom.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace Headroom.Auth;

// Keeps the auth ticket in the database. The cookie holds only the session id.
public class DbTicketStore(IServiceScopeFactory scopes, TimeProvider time) : ITicketStore
{
    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
        var now = time.GetUtcNow();
        await db.Sessions.Where(s => s.ExpiresAt < now).ExecuteDeleteAsync();
        var session = new Session();
        Fill(session, ticket);
        db.Sessions.Add(session);
        await db.SaveChangesAsync();
        return session.Id.ToString();
    }

    public async Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
        if (!Guid.TryParse(key, out var id) || await db.Sessions.FindAsync(id) is not { } session) return;
        Fill(session, ticket);
        await db.SaveChangesAsync();
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
        if (!Guid.TryParse(key, out var id) || await db.Sessions.FindAsync(id) is not { } session) return null;
        if (session.ExpiresAt < time.GetUtcNow())
        {
            await RemoveAsync(key);
            return null;
        }
        return TicketSerializer.Default.Deserialize(session.Ticket);
    }

    public async Task RemoveAsync(string key)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
        if (Guid.TryParse(key, out var id)) await db.Sessions.Where(s => s.Id == id).ExecuteDeleteAsync();
    }

    private void Fill(Session session, AuthenticationTicket ticket)
    {
        session.Ticket = TicketSerializer.Default.Serialize(ticket);
        session.ExpiresAt = ticket.Properties.ExpiresUtc ?? time.GetUtcNow().AddDays(30);
    }
}
