using Headroom.Client.Api;
using Headroom.Data;
using Headroom.Endpoints;
using Microsoft.EntityFrameworkCore;

namespace Headroom.Projects;

/// <summary>Project writes with their rules. REST and MCP both call this. A null refusal means the change is done.</summary>
public static class ProjectWrites
{
    private static readonly Refusal NameRequired = new(400, "The name is required.");
    private static readonly Refusal Duplicate = new(409, "This Client has a Project with this name.");

    public static async Task<(Project? Project, Refusal? Refusal)> CreateAsync(HeadroomDbContext db, Guid? by, Guid clientId, CreateProjectRequest request)
    {
        if (ApiProblem.CleanName(request.Name) is not { } name) return (null, NameRequired);
        if (!TryStatus(request.Status, out var status) || status == ProjectStatus.Archived)
            return (null, new Refusal(400, "A new Project is Tentative or Confirmed."));
        if (!await db.Clients.AnyAsync(c => c.Id == clientId)) return (null, new Refusal(404, "The Client does not exist."));
        if (await db.Projects.AnyAsync(p => p.ClientId == clientId && p.Name == name)) return (null, Duplicate);
        var project = new Project { ClientId = clientId, Name = name, Status = status, LastChangedBy = by };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return (project, null);
    }

    public static async Task<(Project? Project, Refusal? Refusal)> UpdateAsync(HeadroomDbContext db, Guid? by, Guid id, UpdateProjectRequest request)
    {
        if (ApiProblem.CleanName(request.Name) is not { } name) return (null, NameRequired);
        if (!TryStatus(request.Status, out var status)) return (null, new Refusal(400, "The status is not valid."));
        if (await db.Projects.FindAsync(id) is not { } project) return (null, new Refusal(404, "The Project does not exist."));
        if (project.Status == ProjectStatus.Archived && status == ProjectStatus.Tentative)
            return (null, new Refusal(400, "An Archived Project cannot become Tentative. Unarchive it to Confirmed."));
        if (!await db.Clients.AnyAsync(c => c.Id == request.ClientId)) return (null, new Refusal(400, "The Client does not exist."));
        if (await db.Projects.AnyAsync(p => p.Id != id && p.ClientId == request.ClientId && p.Name == name)) return (null, Duplicate);
        project.Name = name;
        project.ClientId = request.ClientId;
        project.Status = status;
        project.LastChangedBy = by;
        await db.SaveChangesAsync();
        return (project, null);
    }

    private static bool TryStatus(string? text, out ProjectStatus status) =>
        Enum.TryParse(text, ignoreCase: true, out status) && Enum.IsDefined(status);
}
