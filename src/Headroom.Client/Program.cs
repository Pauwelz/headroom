using Headroom.Client.Api;
using Headroom.Client.Projects;
using Headroom.Client.Shell;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddMudServices();
// One line per screen client. Add yours below this one.
builder.Services.AddApiClient<SessionApi>(builder.HostEnvironment.BaseAddress);
builder.Services.AddApiClient<Headroom.Client.AccessTokens.AccessTokenApi>(builder.HostEnvironment.BaseAddress);
builder.Services.AddApiClient<Headroom.Client.AccessTokens.PasskeyApi>(builder.HostEnvironment.BaseAddress);
builder.Services.AddApiClient<Headroom.Client.People.PeopleApi>(builder.HostEnvironment.BaseAddress);
builder.Services.AddApiClient<Headroom.Client.Settings.SettingsApi>(builder.HostEnvironment.BaseAddress);
builder.Services.AddApiClient<ProjectsApi>(builder.HostEnvironment.BaseAddress);
builder.Services.AddApiClient<Headroom.Client.Schedule.ScheduleApi>(builder.HostEnvironment.BaseAddress);
builder.Services.AddApiClient<Headroom.Client.Schedule.TimeOffApi>(builder.HostEnvironment.BaseAddress);
builder.Services.AddApiClient<Headroom.Client.Utilisation.UtilisationApi>(builder.HostEnvironment.BaseAddress);
builder.Services.AddApiClient<Headroom.Client.Help.HelpApi>(builder.HostEnvironment.BaseAddress);
builder.Services.AddSingleton(TimeProvider.System);

await builder.Build().RunAsync();
