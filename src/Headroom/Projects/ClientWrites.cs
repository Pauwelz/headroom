using Headroom.Client.Api;
using Headroom.Data;
using Microsoft.EntityFrameworkCore;
using ClientRecord = Headroom.Data.Client; // "Client" alone means the namespace Headroom.Client here.

namespace Headroom.Projects;

/// <summary>Client writes with their rules. REST and MCP both call this. A null refusal means the change is done.</summary>
public static class ClientWrites
{
    private static readonly Refusal NameRequired = new(400, "The name is required.");
    private static readonly Refusal Duplicate = new(409, "A Client with this name exists.");

    public static async Task<(ClientRecord? Client, Refusal? Refusal)> CreateAsync(HeadroomDbContext db, Guid? by, ClientRequest request)
    {
        if (ProjectWrites.CleanName(request.Name) is not { } name) return (null, NameRequired);
        if (await db.Clients.AnyAsync(c => c.Name == name)) return (null, Duplicate);
        var client = new ClientRecord { Name = name, LastChangedBy = by };
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        return (client, null);
    }

    public static async Task<(ClientRecord? Client, Refusal? Refusal)> RenameAsync(HeadroomDbContext db, Guid? by, Guid id, ClientRequest request)
    {
        if (ProjectWrites.CleanName(request.Name) is not { } name) return (null, NameRequired);
        if (await db.Clients.FindAsync(id) is not { } client) return (null, new Refusal(404, "The Client does not exist."));
        if (await db.Clients.AnyAsync(c => c.Id != id && c.Name == name)) return (null, Duplicate);
        client.Name = name;
        client.LastChangedBy = by;
        await db.SaveChangesAsync();
        return (client, null);
    }
}
