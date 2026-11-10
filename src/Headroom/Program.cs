using Headroom.Auth;
using Headroom.Components;
using Headroom.Data;
using Headroom.Email;
using Headroom.Endpoints;
using Headroom.Mcp;
using Headroom.People;
using Headroom.Telemetry;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();
builder.Services.AddDbContext<HeadroomDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Headroom")));
if (builder.Environment.IsDevelopment())
    builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddCheck<Headroom.Data.DatabaseHealthCheck>("database");
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<Headroom.Capacity.IPublicHolidays, Headroom.Capacity.BelgianPublicHolidays>();
builder.Services.AddHeadroomTelemetry(builder.Configuration);
builder.Services.AddHeadroomAuth();
builder.Services.AddHeadroomOAuth(builder.Configuration);
PasskeyCeremony.AddPasskeys(builder.Services);
builder.Services.AddApiAntiforgery();
builder.Services.AddHeadroomMcp();
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection("Smtp"));
builder.Services.AddSingleton<ErasureCheck>();
builder.Services.AddEmailSender(builder.Environment, builder.Configuration["Smtp:Host"]);
builder.Services.AddScoped<Headroom.Dev.DevSeed>();
builder.Services.AddHttpClient(Headroom.Events.EventSender.ClientName, c => c.Timeout = TimeSpan.FromSeconds(30))
    .ConfigurePrimaryHttpMessageHandler(() => Headroom.Events.WebhookGuard.CreateHandler(allowInternal: builder.Environment.IsDevelopment()));
builder.Services.AddScoped<Headroom.Events.EventSender>();
builder.Services.AddHostedService<Headroom.Events.EventSenderService>();
builder.Services.AddHostedService<EventBacklogGauge>();

var app = builder.Build();

// "dotnet ef" builds the host at design time without a database. Skip the migration then.
if (!EF.IsDesignTime)
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<HeadroomDbContext>().Database.Migrate();
    await app.Services.GetRequiredService<ErasureCheck>().RunIfDueAsync();
    var seedPlannerEmail = app.Configuration["Seed:PlannerEmail"];
    if (args.Contains("--seed") || !string.IsNullOrEmpty(seedPlannerEmail))
        await scope.ServiceProvider.GetRequiredService<Headroom.Dev.DevSeed>().RunAsync(app.Environment.EnvironmentName, seedPlannerEmail);
}

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
    app.MapOpenApi();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
// The API and MCP keep their bare status codes: a re-executed POST would turn a 403 into a 400.
app.Use((http, next) => { HeadroomMetrics.Request(http.Request.Path); return next(http); });
app.UseWhen(http => !http.Request.Path.StartsWithSegments("/api") && !http.Request.Path.StartsWithSegments(McpSetup.Path),
    pages => pages.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));
app.UseHttpsRedirection();
app.Use(async (_, next) =>
{
    await app.Services.GetRequiredService<ErasureCheck>().RunIfDueAsync();
    await next();
});

app.UseMcpOriginCheck();
app.UseApiResourceMetadata();
app.UseApiChallenge();
app.UseAuthentication();
app.UseAuthorization();
app.UseClientTags(app.Configuration);
app.UseApiAntiforgery();
app.UseAntiforgery();

// The assets are public. Skipping auth keeps a page load from opening one database connection per file.
app.MapStaticAssets().ShortCircuit();
app.MapHealthChecks("/health");
app.MapApi();
app.MapHeadroomMcp();
app.MapFirstRunEndpoints();
app.MapLoginEndpoints();
app.MapOAuthEndpoints();
// The server redirects a logged-out visitor to /login before it sends the app shell.
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(Headroom.Client._Imports).Assembly)
    .RequireAuthorization();

app.Run();

// Lets the integration tests start the app.
public partial class Program;
