using System.Diagnostics;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Headroom.Auth;
using Headroom.Data;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Headroom.Telemetry;

public static class HeadroomTelemetry
{
    // The one ActivitySource and Meter name.
    public const string Name = "Headroom";

    public const string ConnectionStringKey = "APPLICATIONINSIGHTS_CONNECTION_STRING";
    public const string OtlpEndpointKey = "OTEL_EXPORTER_OTLP_ENDPOINT";
    public const string EndUserIdKey = "Telemetry:EndUserId";
    public const string ClientAddressKey = "Telemetry:ClientAddress";

    // The MCP SDK's own source: one span per MCP call, with the tool name. TelemetryTests fails if the SDK renames it.
    internal const string McpSourceName = "Experimental.ModelContextProtocol";

    // OTLP is on when the general endpoint or the endpoint of one signal is set.
    internal static readonly string[] OtlpEndpointKeys =
        [OtlpEndpointKey, "OTEL_EXPORTER_OTLP_TRACES_ENDPOINT", "OTEL_EXPORTER_OTLP_METRICS_ENDPOINT", "OTEL_EXPORTER_OTLP_LOGS_ENDPOINT"];

    // Span attributes that hold values: the path has the Person id, the query has the values, the SQL text has the data.
    // Upstream instrumentation adds some of them by default, so this list removes them whatever the version does.
    internal static readonly string[] ForbiddenTags =
        ["url.path", "url.query", "url.full", "http.url", "http.target", "db.statement", "db.query.text"];

    // True when an exporter is set. The settings page and the registration use this one check.
    public static bool ExporterSet(IConfiguration configuration) =>
        !string.IsNullOrEmpty(configuration[ConnectionStringKey]) || OtlpEndpointKeys.Any(key => !string.IsNullOrEmpty(configuration[key]));

    // Exports only when a connection string or an OTLP endpoint is set. Without either, the Azure exporter throws at startup.
    // Both may be on together. The personal-data filter runs before every exporter.
    public static IServiceCollection AddHeadroomTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        if (!ExporterSet(configuration)) return services;
        var azureMonitor = !string.IsNullOrEmpty(configuration[ConnectionStringKey]);
        var otlp = OtlpEndpointKeys.Any(key => !string.IsNullOrEmpty(configuration[key]));

        // Registered before the exporters, so the processor runs before they see the span.
        services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddSource(Name).AddSource("Npgsql").AddSource(McpSourceName).AddProcessor(new StripPersonalData()));
        services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddMeter(Name));
        var builder = services.AddOpenTelemetry();
        if (azureMonitor)
            builder.UseAzureMonitor(options =>
            {
                // shortcut: no sampling, add tail-based sampling in an OpenTelemetry Collector if volume grows
                options.SamplingRatio = 1;
                options.TracesPerSecond = null; // the default of 5 traces per second would drop spans
            });
        else
            // Azure Monitor adds the request and outgoing HTTP instrumentation itself. OTLP alone needs it.
            builder.WithTracing(t => t.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation())
                .WithMetrics(m => m.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation());
        if (otlp) builder.UseOtlpExporter(); // reads the standard OTEL_EXPORTER_OTLP_* variables
        return services;
    }

    // Runs after authorization, which sets the user of the access token and MCP schemes too.
    // Azure Monitor maps enduser.id to user_AuthenticatedId and client.address to client_IP.
    // The Person id and the client IP address are the only personal values in telemetry (ADR 0016).
    // Each tag follows TelemetryPrivacy: the Planner's setting, then Telemetry__*, then on.
    public static IApplicationBuilder UseClientTags(this IApplicationBuilder app)
    {
        return app.Use(async (http, next) =>
        {
            // No Activity means no exporter and no span, so there is nothing to tag and no need to read.
            if (Activity.Current is { } activity)
            {
                var (endUserId, clientAddress) = await http.RequestServices.GetRequiredService<TelemetryPrivacy>().GetAsync(http.RequestServices.GetRequiredService<HeadroomDbContext>(), http.RequestAborted);
                if (endUserId && http.User.PersonId() is { } id) activity.SetTag("enduser.id", id.ToString());
                // The forwarded headers middleware sets RemoteIpAddress to the real client behind the proxy.
                if (clientAddress) activity.SetTag("client.address", http.Connection.RemoteIpAddress?.ToString());
            }
            await next(http);
        });
    }

    private sealed class StripPersonalData : BaseProcessor<Activity>
    {
        public override void OnEnd(Activity activity)
        {
            foreach (var tag in ForbiddenTags) activity.SetTag(tag, null);
        }
    }
}
