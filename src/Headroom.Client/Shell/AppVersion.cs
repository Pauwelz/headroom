using System.Reflection;

namespace Headroom.Client.Shell;

// The running version for the Help page. The build sets it with -p:Version.
public static class AppVersion
{
    public static string Text { get; } = Format(
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    // "0.1.0+5f3c2a1abcdef" gives "Headroom 0.1.0 (5f3c2a1)". Without a commit, only the version shows.
    public static string Format(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion)) return "Headroom";
        var parts = informationalVersion.Split('+', 2);
        var commit = parts.Length == 2 && parts[1].Length > 0 ? $" ({parts[1][..Math.Min(7, parts[1].Length)]})" : "";
        return $"Headroom {parts[0]}{commit}";
    }
}
