namespace Headroom.Auth;

// Names a Passkey from the User-Agent, for example "Firefox on Linux". shortcut: a fixed list of common browsers and systems.
public static class PasskeyName
{
    // The order matters: Edge and Opera also say Chrome, Chrome also says Safari, Android also says Linux.
    private static readonly (string Marker, string Name)[] Browsers =
        [("Edg/", "Edge"), ("OPR/", "Opera"), ("Firefox/", "Firefox"), ("Chrome/", "Chrome"), ("CriOS/", "Chrome"), ("Safari/", "Safari")];

    private static readonly (string Marker, string Name)[] Systems =
        [("Windows", "Windows"), ("Android", "Android"), ("iPhone", "iOS"), ("iPad", "iOS"), ("Mac OS X", "macOS"), ("CrOS", "ChromeOS"), ("Linux", "Linux")];

    public static string From(string? userAgent)
    {
        var browser = Browsers.FirstOrDefault(b => userAgent?.Contains(b.Marker) == true).Name;
        var system = Systems.FirstOrDefault(s => userAgent?.Contains(s.Marker) == true).Name;
        return (browser, system) switch
        {
            (not null, not null) => $"{browser} on {system}",
            (not null, null) => browser,
            (null, not null) => system,
            _ => "Passkey",
        };
    }
}
