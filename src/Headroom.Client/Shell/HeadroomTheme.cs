using MudBlazor;

namespace Headroom.Client.Shell;

/// <summary>The Headroom look: petrol for work, amber for the ceiling, red only for Over-allocation.</summary>
public static class HeadroomTheme
{
    private static readonly string[] Body = ["Barlow", "system-ui", "sans-serif"];
    private static readonly string[] Display = ["Barlow Semi Condensed", "Barlow", "system-ui", "sans-serif"];

    public static readonly MudTheme Theme = new()
    {
        // The grid and heatmap read these slots: Secondary = Time off, Tertiary = Company day off,
        // Warning = public holiday and the "high" band, Info/Success = "low"/"ideal" bands.
        PaletteLight = new PaletteLight
        {
            Primary = "#1D5C6E",
            Secondary = "#7B4F7A",
            Tertiary = "#3E7CB1",
            Info = "#2F6FA8",
            Success = "#2E7D4F",
            Warning = "#B57409",
            Error = "#C2362B",
            AppbarBackground = "#23282E",
            AppbarText = "#F3F5F6",
            Background = "#F3F5F6",
            BackgroundGray = "#E8ECEE",
            Surface = "#FFFFFF",
            DrawerBackground = "#FFFFFF",
            DrawerText = "#23282E",
            DrawerIcon = "#5A636D",
            TextPrimary = "#23282E",
            TextSecondary = "#5A636D",
            LinesDefault = "#DDE2E6",
            LinesInputs = "#8A939C",
            TableLines = "#DDE2E6",
            Divider = "#DDE2E6",
            ActionDisabledBackground = "#E6EAED",
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#5FB0C4",
            PrimaryContrastText = "#0E1A1E",
            Secondary = "#B38DB2",
            SecondaryContrastText = "#1E1420",
            Tertiary = "#6FA3D4",
            Info = "#6FA3D4",
            Success = "#5DB884",
            Warning = "#E9A21C",
            WarningContrastText = "#1F1600",
            Error = "#EF6B5E",
            AppbarBackground = "#262D34",
            AppbarText = "#E6EAED",
            Background = "#171B20",
            BackgroundGray = "#20262C",
            Surface = "#1F252B",
            DrawerBackground = "#1F252B",
            DrawerText = "#E6EAED",
            DrawerIcon = "#A3ADB7",
            TextPrimary = "#E6EAED",
            TextSecondary = "#A3ADB7",
            LinesDefault = "#313940",
            LinesInputs = "#6B757F",
            TableLines = "#313940",
            Divider = "#313940",
            ActionDisabledBackground = "#262D34",
        },
        Typography = new Typography
        {
            Default = new DefaultTypography { FontFamily = Body, FontSize = ".875rem", LineHeight = "1.45" },
            Body1 = new Body1Typography { FontFamily = Body, FontSize = ".9375rem", LineHeight = "1.5" },
            Body2 = new Body2Typography { FontFamily = Body, FontSize = ".875rem", LineHeight = "1.45" },
            H4 = new H4Typography { FontFamily = Display, FontSize = "2rem", FontWeight = "600", LineHeight = "1.15", LetterSpacing = "-.01em" },
            H5 = new H5Typography { FontFamily = Display, FontSize = "1.5rem", FontWeight = "600", LineHeight = "1.2", LetterSpacing = "-.005em" },
            H6 = new H6Typography { FontFamily = Display, FontSize = "1.25rem", FontWeight = "600", LineHeight = "1.25", LetterSpacing = "0" },
            Subtitle1 = new Subtitle1Typography { FontFamily = Display, FontSize = "1rem", FontWeight = "500", LineHeight = "1.35" },
            Subtitle2 = new Subtitle2Typography { FontFamily = Display, FontSize = ".875rem", FontWeight = "600", LineHeight = "1.35" },
            Button = new ButtonTypography { FontFamily = Body, FontSize = ".875rem", FontWeight = "500", TextTransform = "none", LetterSpacing = ".01em" },
            Caption = new CaptionTypography { FontFamily = Body, FontSize = ".75rem", LineHeight = "1.4" },
        },
        LayoutProperties = new LayoutProperties { DefaultBorderRadius = "4px", AppbarHeight = "56px" },
    };
}
