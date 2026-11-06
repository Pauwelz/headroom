using System.Diagnostics.Metrics;
using Headroom.Data;
using Headroom.Events;

namespace Headroom.Telemetry;

// The Events that still wait for a send: not delivered and not given up (EventSender.Pending).
// shortcut: the callback queries the database on the exporter thread, once per export interval. Cache the count if that costs too much.
public sealed class EventBacklogGauge(IServiceScopeFactory scopes, TimeProvider time) : IHostedService
{
    private Meter? _meter;

    public Task StartAsync(CancellationToken ct)
    {
        _meter = new Meter(HeadroomTelemetry.Name); // one per app, so a second app in a test has its own gauge
        _meter.CreateObservableGauge("headroom.events.backlog", Count, "{event}");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _meter?.Dispose();
        return Task.CompletedTask;
    }

    public long Count()
    {
        using var scope = scopes.CreateScope();
        return EventSender.Pending(scope.ServiceProvider.GetRequiredService<HeadroomDbContext>().Events, time.GetUtcNow()).Count();
    }
}
