using System.Diagnostics;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Headroom.Auth;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Headroom.Telemetry;

public static class HeadroomTelemetry
{
    // The one ActivitySource and Meter name.
    public const string Name = "Headroom";

    public const string ConnectionStringKey = "APPLICATIONINSIGHTS_CONNECTION_STRING";
    // The MCP SDK's own source: one span per MCP call, with the tool name. Checked against ModelContextProtocol.Core 2.3.1.
    internal const string McpSourceName = "Experimental.ModelContextProtocol";

    public const string OtlpEndpointKey = "OTEL_EXPORTER_OTLP_ENDPOINT";

    // OTLP is on when the general endpoint or the endpoint of one signal is set.
    internal static readonly string[] OtlpEndpointKeys =
        [OtlpEndpointKey, "OTEL_EXPORTER_OTLP_TRACES_ENDPOINT", "OTEL_EXPORTER_OTLP_METRICS_ENDPOINT", "OTEL_EXPORTER_OTLP_LOGS_ENDPOINT"];

    // Span attributes that hold values: the path has the Person id, the query has the values, the SQL text has the data.
    // Upstream instrumentation adds some of them by default, so this list removes them whatever the version does.
    internal static readonly string[] ForbiddenTags =
        ["url.path", "url.query", "url.full", "http.url", "http.target", "db.statement", "db.query.text"];

    // Exports only when a connection string or an OTLP endpoint is set. Without either, the Azure exporter throws at startup.
    // Both may be on together. The personal-data filter runs before every exporter.
    public static IServiceCollection AddHeadroomTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        var azureMonitor = !string.IsNullOrEmpty(configuration[ConnectionStringKey]);
        var otlp = OtlpEndpointKeys.Any(key => !string.IsNullOrEmpty(configuration[key]));
        if (!azureMonitor && !otlp) return services;

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
    // Azure Monitor maps enduser.id to user_AuthenticatedId. The Person id is the only personal value in telemetry (ADR 0016).
    public static IApplicationBuilder UseEndUserId(this IApplicationBuilder app) =>
        app.Use((http, next) =>
        {
            if (http.User.PersonId() is { } id) Activity.Current?.SetTag("enduser.id", id.ToString());
            return next(http);
        });

    private sealed class StripPersonalData : BaseProcessor<Activity>
    {
        public override void OnEnd(Activity activity)
        {
            foreach (var tag in ForbiddenTags) activity.SetTag(tag, null);
        }
    }
}
