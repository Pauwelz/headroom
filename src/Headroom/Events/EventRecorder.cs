using System.Text.Json;
using System.Text.Json.Nodes;
using Headroom.Capacity;
using Headroom.Client.Api;
using Headroom.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Headroom.Events;

// Turns record changes into stored Events. HeadroomDbContext calls it from its save path, so one place covers every endpoint.
// To add a source (3 steps):
// 1. Add a case in Classify.
// 2. Preload what its data record needs in BuildAsync (People, Projects, Clients, stats), one query per kind.
// 3. Add a case in DataOf and a data record.
public static class EventRecorder
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>A change to record: the CloudEvents type and the record.</summary>
    public record Change(string Type, object Record);

    public static List<Change> Collect(IEnumerable<EntityEntry> entries) =>
        entries.Select(Classify).OfType<Change>().ToList();

    private static Change? Classify(EntityEntry e) => e.Entity switch
    {
        Allocation => ByState("Allocation", e),
        TimeOff => ByState("TimeOff", e),
        // Only a status change is an Event. Other Project edits are not.
        Project when e.State == EntityState.Modified && e.Property(nameof(Project.Status)).IsModified
            && !Equals(e.Property(nameof(Project.Status)).OriginalValue, e.Property(nameof(Project.Status)).CurrentValue) =>
            new("headroom.Project.changed", e.Entity),
        _ => null,
    };

    private static Change? ByState(string name, EntityEntry e) => e.State switch
    {
        EntityState.Added => new($"headroom.{name}.created", e.Entity),
        EntityState.Modified => new($"headroom.{name}.changed", e.Entity),
        EntityState.Deleted => new($"headroom.{name}.deleted", e.Entity),
        _ => null,
    };

    public static async Task<List<Event>> BuildAsync(
        HeadroomDbContext db, IReadOnlyList<Change> changes, IReadOnlyList<Guid> webhookIds, DateTimeOffset now, CancellationToken ct)
    {
        var allocations = changes.Select(c => c.Record).OfType<Allocation>().ToList();
        var timeOff = changes.Select(c => c.Record).OfType<TimeOff>().ToList();
        var changedProjects = changes.Select(c => c.Record).OfType<Project>().ToList();
        var personIds = allocations.Select(a => a.PersonId).Concat(timeOff.Select(t => t.PersonId)).Distinct().ToList();
        var projectIds = allocations.Select(a => a.ProjectId).Distinct().ToList();
        var people = await db.People.AsNoTracking().Where(p => personIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var projects = await db.Projects.AsNoTracking().Where(p => projectIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var clientIds = projects.Values.Select(p => p.ClientId).Concat(changedProjects.Select(p => p.ClientId)).Distinct().ToList();
        var clients = await db.Clients.AsNoTracking().Where(c => clientIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var changedIds = changedProjects.Select(p => p.Id).ToList();
        var stats = await db.Allocations.Where(a => changedIds.Contains(a.ProjectId)).GroupBy(a => a.ProjectId)
            .Select(g => new { g.Key, Start = g.Min(a => a.StartDate), End = g.Max(a => a.EndDate), Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, ct);

        object DataOf(object record)
        {
            switch (record)
            {
                case Allocation a:
                    var person = people[a.PersonId];
                    var project = projects[a.ProjectId];
                    return new AllocationData(
                        a.Id, a.PersonId, person.DisplayName, person.Email, a.ProjectId, project.Name, project.ClientId,
                        clients[project.ClientId].Name, a.StartDate, a.EndDate, a.Percentage, a.DateModified, a.LastChangedBy);
                case TimeOff t:
                    var owner = people[t.PersonId];
                    return new TimeOffData(
                        t.Id, t.PersonId, owner.DisplayName, owner.Email, t.StartDate, DayPartNames.ToApi(t.StartPart),
                        t.EndDate, DayPartNames.ToApi(t.EndPart), t.DateModified, t.LastChangedBy);
                default:
                    // The same shape as ProjectResponse, plus the Client name.
                    var p = (Project)record;
                    stats.TryGetValue(p.Id, out var s);
                    return new ProjectData(new ProjectResponse("Project", p.Id, p.ClientId, p.Name, p.StatusName,
                        s?.Start, s?.End, s?.Count ?? 0, p.DateModified, p.LastChangedBy), clients[p.ClientId].Name);
            }
        }

        var events = new List<Event>();
        foreach (var change in changes)
        {
            var data = DataOf(change.Record);
            foreach (var webhookId in webhookIds)
            {
                var id = Guid.CreateVersion7();
                var body = new CloudEvent("1.0", id.ToString(), "headroom", change.Type, now, "application/json", data);
                events.Add(new Event
                {
                    Id = id, WebhookId = webhookId, Type = change.Type, DateCreated = now,
                    Payload = JsonSerializer.Serialize(body, Json),
                });
            }
        }
        return events;
    }

    // Replaces the name and email of a Person in their stored Events. Allocation and TimeOff data share these three fields.
    // shortcut: a scan of the Event payloads, because erase and Delete are rare. Add an index if the Event table grows large.
    public static async Task ScrubPersonAsync(HeadroomDbContext db, Guid personId, string name)
    {
        string Key(string property) => Json.PropertyNamingPolicy!.ConvertName(property);
        var (idKey, nameKey, emailKey) = (Key(nameof(AllocationData.PersonId)), Key(nameof(AllocationData.PersonName)), Key(nameof(AllocationData.PersonEmail)));
        var events = await db.Events.Where(e => e.Payload.Contains($"\"{idKey}\":\"{personId}\"")).ToListAsync();
        foreach (var e in events)
        {
            var node = JsonNode.Parse(e.Payload);
            if (node?[Key(nameof(CloudEvent.Data))] is not JsonObject data || data[idKey]?.ToString() != personId.ToString()) continue;
            data[nameKey] = name;
            data[emailKey] = null;
            e.Payload = node.ToJsonString();
        }
    }

    private record CloudEvent(string Specversion, string Id, string Source, string Type, DateTimeOffset Time, string Datacontenttype, object Data);

    // The Allocation in the ADR 0002 vocabulary, with the names a receiver needs.
    private record AllocationData(
        Guid Id, Guid PersonId, string PersonName, string? PersonEmail, Guid ProjectId, string ProjectName, Guid ClientId,
        string ClientName, DateOnly StartDate, DateOnly EndDate, int Percentage, DateTimeOffset DateModified, Guid? LastChangedBy)
    {
        public string Type => "Allocation";
    }

    private record TimeOffData(
        Guid Id, Guid PersonId, string PersonName, string? PersonEmail, DateOnly StartDate, string StartPart,
        DateOnly EndDate, string EndPart, DateTimeOffset DateModified, Guid? LastChangedBy)
    {
        public string Type => "TimeOff";
    }

    // Serialises as the flat ProjectResponse fields plus clientName.
    private class ProjectData(ProjectResponse project, string clientName)
    {
        public string Type => project.Type;
        public Guid Id => project.Id;
        public Guid ClientId => project.ClientId;
        public string ClientName => clientName;
        public string Name => project.Name;
        public string Status => project.Status;
        public DateOnly? StartDate => project.StartDate;
        public DateOnly? EndDate => project.EndDate;
        public int AllocationCount => project.AllocationCount;
        public DateTimeOffset DateModified => project.DateModified;
        public Guid? LastChangedBy => project.LastChangedBy;
    }
}
