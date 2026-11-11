using Headroom.Auth;
using Headroom.Client.Api;
using Headroom.Data;
using Headroom.Projects;
using Microsoft.EntityFrameworkCore;
using ClientRecord = Headroom.Data.Client; // "Client" alone means the namespace Headroom.Client here.
using static Headroom.Endpoints.ApiProblem;

namespace Headroom.Endpoints;

// Clients and Projects. Anyone logged in reads. Only a Planner writes.
public static class ProjectEndpoints
{
    public static void MapProjectEndpoints(this RouteGroupBuilder api)
    {
        var clients = api.MapGroup("/clients").RequireAuthorization();
        clients.MapGet("", ListClients);
        clients.MapPost("", CreateClient).RequirePlanner();
        clients.MapPut("/{id:guid}", RenameClient).RequirePlanner();
        clients.MapDelete("/{id:guid}", DeleteClient).RequirePlanner();
        clients.MapPost("/{id:guid}/projects", CreateProject).RequirePlanner();

        var projects = api.MapGroup("/projects").RequireAuthorization();
        projects.MapGet("/mine", MyProjects);
        projects.MapGet("/{id:guid}/people", ProjectPeople);
        projects.MapPut("/{id:guid}", UpdateProject).RequirePlanner();
        projects.MapDelete("/{id:guid}", DeleteProject).RequirePlanner();
    }

    private static async Task<IResult> ListClients(HeadroomDbContext db) => Results.Ok(await ListClientsAsync(db));

    // The endpoint and the MCP tool share this query.
    public static async Task<List<ClientResponse>> ListClientsAsync(HeadroomDbContext db)
    {
        var clients = await db.Clients.AsNoTracking().ToListAsync();
        var projects = await db.Projects.AsNoTracking().ToListAsync();
        var stats = await StatsAsync(db);
        return clients
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(c => ToResponse(c, projects.Where(p => p.ClientId == c.Id)
                .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList(), stats))
            .ToList();
    }

    // The ids of the Projects where the current user has an Allocation ("Only my Projects").
    private static async Task<IResult> MyProjects(HeadroomDbContext db, HttpContext http)
    {
        var me = http.User.PersonId();
        return Results.Ok(await db.Allocations.Where(a => a.PersonId == me).Select(a => a.ProjectId).Distinct().ToListAsync());
    }

    private static async Task<IResult> ProjectPeople(Guid id, HeadroomDbContext db)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == id)) return Results.NotFound();
        var rows = await db.Allocations.AsNoTracking().Where(a => a.ProjectId == id)
            .Join(db.People, a => a.PersonId, p => p.Id, (a, p) => new { a, Person = p }).ToListAsync();
        return Results.Ok(rows.GroupBy(r => r.a.PersonId)
            .Select(g => new ProjectPersonResponse("ProjectPerson", g.Key, g.First().Person.Name,
                g.OrderBy(r => r.a.StartDate).Select(r => new ScheduleAllocation(r.a.Id, r.a.StartDate, r.a.EndDate, r.a.Percentage)).ToList()))
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList());
    }

    private static async Task<IResult> CreateClient(ClientRequest request, HeadroomDbContext db, HttpContext http)
    {
        var (client, refusal) = await ClientWrites.CreateAsync(db, http.User.PersonId(), request);
        return refusal is not null ? Refused(refusal) : Results.Created($"/api/clients/{client!.Id}", ToResponse(client, [], NoStats));
    }

    private static async Task<IResult> RenameClient(Guid id, ClientRequest request, HeadroomDbContext db, HttpContext http)
    {
        var (client, refusal) = await ClientWrites.RenameAsync(db, http.User.PersonId(), id, request);
        return refusal is not null ? Refused(refusal)
            : Results.Ok(ToResponse(client!, await db.Projects.AsNoTracking().Where(p => p.ClientId == id).ToListAsync(), await StatsAsync(db)));
    }

    private static async Task<IResult> DeleteClient(Guid id, HeadroomDbContext db)
    {
        if (await db.Clients.FindAsync(id) is not { } client) return Results.NotFound();
        if (await db.Projects.AnyAsync(p => p.ClientId == id)) return Problem(409, "Delete the Projects of this Client first.");
        db.Clients.Remove(client);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> CreateProject(Guid id, CreateProjectRequest request, HeadroomDbContext db, HttpContext http)
    {
        var (project, refusal) = await ProjectWrites.CreateAsync(db, http.User.PersonId(), id, request);
        return refusal is not null ? Refused(refusal) : Results.Created($"/api/projects/{project!.Id}", ToResponse(project, NoStats));
    }

    private static async Task<IResult> UpdateProject(Guid id, UpdateProjectRequest request, HeadroomDbContext db, HttpContext http)
    {
        var (project, refusal) = await ProjectWrites.UpdateAsync(db, http.User.PersonId(), id, request);
        return refusal is not null ? Refused(refusal) : Results.Ok(ToResponse(project!, await StatsAsync(db)));
    }

    // Allocations go with the Project.
    private static async Task<IResult> DeleteProject(Guid id, HeadroomDbContext db)
    {
        if (await db.Projects.FindAsync(id) is not { } project) return Results.NotFound();
        // Remove through the tracker, so the deletes also store Events.
        db.Allocations.RemoveRange(await db.Allocations.Where(a => a.ProjectId == id).ToListAsync());
        db.Projects.Remove(project);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private record ProjectStats(DateOnly Start, DateOnly End, int Count);

    private static readonly IReadOnlyDictionary<Guid, ProjectStats> NoStats = new Dictionary<Guid, ProjectStats>();

    // The first and last Allocation set the dates of a Project.
    private static async Task<IReadOnlyDictionary<Guid, ProjectStats>> StatsAsync(HeadroomDbContext db) =>
        await db.Allocations.GroupBy(a => a.ProjectId)
            .Select(g => new { g.Key, Stats = new ProjectStats(g.Min(a => a.StartDate), g.Max(a => a.EndDate), g.Count()) })
            .ToDictionaryAsync(g => g.Key, g => g.Stats);

    private static ProjectResponse ToResponse(Project p, IReadOnlyDictionary<Guid, ProjectStats> stats)
    {
        stats.TryGetValue(p.Id, out var s);
        return new("Project", p.Id, p.ClientId, p.Name, p.StatusName,
            s?.Start, s?.End, s?.Count ?? 0, p.DateModified, p.LastChangedBy);
    }

    private static ClientResponse ToResponse(ClientRecord c, IReadOnlyCollection<Project> projects, IReadOnlyDictionary<Guid, ProjectStats> stats) =>
        new("Client", c.Id, c.Name, projects.Count == 0 || projects.Any(p => p.Status != ProjectStatus.Archived),
            projects.Select(p => ToResponse(p, stats)).ToList(), c.DateModified, c.LastChangedBy);
}
