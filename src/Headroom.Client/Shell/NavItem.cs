namespace Headroom.Client.Shell;

public record NavItem(string Label, string Href, string Icon, bool PlannerOnly = false)
{
    // The server also refuses the endpoints of the Planner-only screens.
    private static readonly NavItem[] All =
    [
        new("Schedule", "/schedule", MudBlazor.Icons.Material.Filled.CalendarMonth),
        new("Projects", "/projects", MudBlazor.Icons.Material.Filled.Folder),
        new("People", "/people", MudBlazor.Icons.Material.Filled.People, PlannerOnly: true),
        new("Utilisation", "/utilisation", MudBlazor.Icons.Material.Filled.BarChart),
        new("Access tokens", "/access-tokens", MudBlazor.Icons.Material.Filled.VpnKey),
        new("Settings", "/settings", MudBlazor.Icons.Material.Filled.Settings, PlannerOnly: true),
    ];

    /// <summary>The first route segment of each screen. It is also the name of its help topic.</summary>
    public static IEnumerable<string> Topics => All.Select(i => i.Href.TrimStart('/'));

    public static IEnumerable<NavItem> For(bool isPlanner) => All.Where(i => isPlanner || !i.PlannerOnly);
}
