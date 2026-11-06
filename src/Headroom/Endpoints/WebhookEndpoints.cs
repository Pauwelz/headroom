using Headroom.Auth;
using Headroom.Client.Api;
using Headroom.Data;
using Headroom.Events;
using Microsoft.EntityFrameworkCore;
using static Headroom.Endpoints.ApiProblem;

namespace Headroom.Endpoints;

// Webhooks in Settings. Planners only.
public static class WebhookEndpoints
{
    public static void MapWebhookEndpoints(this RouteGroupBuilder api)
    {
        var hooks = api.MapGroup("/settings/webhooks").RequirePlanner();
        hooks.MapGet("/", List);
        hooks.MapPost("/", Add);
        hooks.MapPut("/{id:guid}", Rename);
        hooks.MapDelete("/{id:guid}", Delete);
        hooks.MapPost("/{id:guid}/secret", RotateSecret);
        hooks.MapGet("/failed-events", FailedEvents);
    }

    private static async Task<IResult> List(HeadroomDbContext db) =>
        Results.Ok(await db.Webhooks.AsNoTracking().OrderBy(w => w.Name)
            .Select(w => new WebhookResponse(w.Id, w.Name, w.Url)).ToListAsync());

    private static async Task<IResult> Add(WebhookRequest request, HttpContext http, HeadroomDbContext db, IWebHostEnvironment env)
    {
        if (CleanName(request.Name) is not { } name) return Problem(400, NameDetail);
        if (WebhookGuard.UrlProblem(request.Url, env.IsDevelopment(), out var url) is { } urlProblem) return Problem(400, urlProblem);
        var hook = new Webhook { Name = name, Url = url.ToString(), Secret = EventSender.NewSecret(), LastChangedBy = http.User.PersonId() };
        db.Webhooks.Add(hook);
        await db.SaveChangesAsync();
        return Results.Ok(new CreatedWebhookResponse(hook.Id, hook.Name, hook.Url, hook.Secret));
    }

    private static async Task<IResult> Rename(Guid id, RenameWebhookRequest request, HttpContext http, HeadroomDbContext db)
    {
        if (CleanName(request.Name) is not { } name) return Problem(400, NameDetail);
        if (await db.Webhooks.FindAsync(id) is not { } hook) return Problem(404, NotFoundDetail);
        (hook.Name, hook.LastChangedBy) = (name, http.User.PersonId());
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> RotateSecret(Guid id, HttpContext http, HeadroomDbContext db)
    {
        if (await db.Webhooks.FindAsync(id) is not { } hook) return Problem(404, NotFoundDetail);
        (hook.Secret, hook.LastChangedBy) = (EventSender.NewSecret(), http.User.PersonId());
        await db.SaveChangesAsync();
        return Results.Ok(new CreatedWebhookResponse(hook.Id, hook.Name, hook.Url, hook.Secret));
    }

    // The newest 50 Events per Webhook that were not delivered, with the last error.
    private static async Task<IResult> FailedEvents(HeadroomDbContext db, TimeProvider time)
    {
        var cutoff = time.GetUtcNow() - EventSender.GiveUpAfter;
        var failed = new List<FailedEventResponse>();
        foreach (var webhookId in await db.Webhooks.Select(w => w.Id).ToListAsync())
            failed.AddRange(await db.Events.AsNoTracking().Where(e => e.WebhookId == webhookId && e.DeliveredAt == null && e.LastError != null)
                .OrderByDescending(e => e.DateCreated).Take(FailedEventsPerWebhook)
                .Select(e => new FailedEventResponse(e.Id, e.WebhookId, e.Type, e.DateCreated, e.Attempts, e.LastError!, e.DateCreated <= cutoff))
                .ToListAsync());
        return Results.Ok(failed);
    }

    private const int FailedEventsPerWebhook = 50;

    private static async Task<IResult> Delete(Guid id, HeadroomDbContext db)
    {
        if (await db.Webhooks.FindAsync(id) is not { } hook) return Problem(404, NotFoundDetail);
        db.Webhooks.Remove(hook);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private const string NotFoundDetail = "The Webhook does not exist.";
}
