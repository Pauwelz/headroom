namespace Headroom.Client.Shell;

public enum ThemeChoice { Auto, Light, Dark }

public static class ThemeChoices
{
    public static bool IsDark(this ThemeChoice choice, bool systemIsDark) => choice switch
    {
        ThemeChoice.Light => false,
        ThemeChoice.Dark => true,
        _ => systemIsDark,
    };

    // Auto -> Light -> Dark -> Auto.
    public static ThemeChoice Next(this ThemeChoice choice) => (ThemeChoice)(((int)choice + 1) % 3);

    // A missing or unknown saved value means Auto.
    public static ThemeChoice Parse(string? saved) =>
        Enum.TryParse<ThemeChoice>(saved, out var choice) && Enum.IsDefined(choice) ? choice : ThemeChoice.Auto;
}
