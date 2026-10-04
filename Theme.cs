using System.Drawing.Drawing2D;
namespace ATSRoadTripConverter;
internal sealed record ThemePalette(
    string Name,
    Color Background,
    Color Surface,
    Color Field,
    Color Border,
    Color Text,
    Color Muted,
    Color Accent);

internal static class Theme
{
    public static IReadOnlyList<ThemePalette> Palettes { get; } = Array.AsReadOnly(new[]
    {
        new ThemePalette("Roadtrip", Color.FromArgb(13, 16, 21), Color.FromArgb(20, 25, 34), Color.FromArgb(27, 33, 44), Color.FromArgb(38, 46, 60), Color.FromArgb(233, 237, 244), Color.FromArgb(139, 149, 167), Color.FromArgb(255, 138, 61)),
        new ThemePalette("Daylight", Color.FromArgb(245, 247, 250), Color.FromArgb(255, 255, 255), Color.FromArgb(238, 241, 246), Color.FromArgb(222, 228, 237), Color.FromArgb(16, 21, 28), Color.FromArgb(92, 103, 120), Color.FromArgb(37, 99, 235)),
        new ThemePalette("Steel", Color.FromArgb(17, 19, 23), Color.FromArgb(24, 27, 32), Color.FromArgb(32, 36, 43), Color.FromArgb(44, 49, 58), Color.FromArgb(232, 236, 240), Color.FromArgb(146, 155, 167), Color.FromArgb(158, 190, 219)),
        new ThemePalette("Ember", Color.FromArgb(23, 15, 16), Color.FromArgb(31, 21, 23), Color.FromArgb(42, 28, 30), Color.FromArgb(57, 38, 41), Color.FromArgb(244, 233, 231), Color.FromArgb(166, 144, 142), Color.FromArgb(255, 107, 87)),
        new ThemePalette("Obsidian", Color.FromArgb(10, 10, 12), Color.FromArgb(19, 19, 23), Color.FromArgb(28, 28, 34), Color.FromArgb(42, 42, 50), Color.FromArgb(242, 240, 234), Color.FromArgb(154, 151, 142), Color.FromArgb(227, 179, 65)),
        new ThemePalette("Crimson", Color.FromArgb(21, 12, 14), Color.FromArgb(31, 18, 21), Color.FromArgb(43, 25, 29), Color.FromArgb(58, 34, 40), Color.FromArgb(248, 234, 236), Color.FromArgb(190, 150, 157), Color.FromArgb(255, 77, 109)),
        new ThemePalette("Midnight", Color.FromArgb(10, 16, 26), Color.FromArgb(16, 24, 37), Color.FromArgb(23, 33, 50), Color.FromArgb(33, 46, 69), Color.FromArgb(230, 238, 247), Color.FromArgb(134, 151, 174), Color.FromArgb(76, 194, 255)),
        new ThemePalette("Evergreen", Color.FromArgb(10, 18, 15), Color.FromArgb(16, 26, 22), Color.FromArgb(23, 36, 30), Color.FromArgb(33, 50, 42), Color.FromArgb(228, 240, 233), Color.FromArgb(134, 160, 150), Color.FromArgb(63, 217, 140)),
        new ThemePalette("Lagoon", Color.FromArgb(9, 19, 21), Color.FromArgb(14, 29, 33), Color.FromArgb(20, 41, 46), Color.FromArgb(28, 57, 64), Color.FromArgb(224, 243, 243), Color.FromArgb(126, 165, 168), Color.FromArgb(45, 212, 191)),
        new ThemePalette("Sandstone", Color.FromArgb(250, 246, 240), Color.FromArgb(255, 255, 255), Color.FromArgb(243, 236, 227), Color.FromArgb(226, 216, 203), Color.FromArgb(38, 31, 24), Color.FromArgb(126, 112, 95), Color.FromArgb(194, 112, 58)),
        new ThemePalette("Aurora", Color.FromArgb(14, 12, 26), Color.FromArgb(23, 20, 40), Color.FromArgb(32, 28, 55), Color.FromArgb(45, 39, 74), Color.FromArgb(237, 234, 255), Color.FromArgb(154, 145, 190), Color.FromArgb(167, 139, 250)),
        new ThemePalette("Vapor", Color.FromArgb(20, 11, 36), Color.FromArgb(29, 16, 51), Color.FromArgb(40, 26, 71), Color.FromArgb(59, 39, 102), Color.FromArgb(242, 234, 255), Color.FromArgb(168, 150, 201), Color.FromArgb(255, 79, 216))
    });

    private static ThemePalette CurrentPalette =>
        Palettes.FirstOrDefault(palette => palette.Name.Equals(SettingsManager.Current.ThemeName, StringComparison.OrdinalIgnoreCase))
            ?? Palettes[0];

    public static Color Background => CurrentPalette.Background;
    public static Color Surface => CurrentPalette.Surface;
    public static Color Field => CurrentPalette.Field;
    public static Color Border => CurrentPalette.Border;
    public static Color Text => CurrentPalette.Text;
    public static Color Muted => CurrentPalette.Muted;

    /// <summary>Lists palette names for prompts, e.g. "Crimson, Obsidian and Ember".</summary>
    public static string ThemeNamesFor(IEnumerable<string> names)
    {
        var list = names.ToArray();
        return list.Length switch
        {
            0 => "the themes",
            1 => list[0],
            _ => string.Join(", ", list.Take(list.Length - 1)) + " and " + list[^1]
        };
    }

    public static Color PresetAccent(string name) =>
        Palettes.FirstOrDefault(palette => palette.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Accent ?? Palettes[0].Accent;
    public static Color Accent
    {
        get
        {
            try
            {
                return ColorTranslator.FromHtml(SettingsManager.Current.AccentColor);
            }
            catch
            {
                return Color.FromArgb(255, 138, 61);
            }
        }
    }
    public static Color AccentHover => ControlPaint.Light(Accent, 0.2f);
    public static Color AccentPressed => ControlPaint.Dark(Accent, 0.14f);

    public static Color FieldHover =>
        IsLightBackground ? ControlPaint.Dark(Field, 0.06f) : ControlPaint.Light(Field, 0.14f);

    public static Color FieldPressed =>
        IsLightBackground ? ControlPaint.Dark(Field, 0.13f) : ControlPaint.Dark(Field, 0.06f);

    /// <summary>Readable foreground for text drawn on top of <paramref name="accent"/>.</summary>
    public static Color OnAccent(Color accent)
    {
        var luminance = 0.299 * accent.R + 0.587 * accent.G + 0.114 * accent.B;
        return luminance > 155 ? Color.FromArgb(14, 17, 22) : Color.White;
    }

    /// <summary>
    /// Colour of the surface sitting directly behind <paramref name="control"/>.
    /// Used so rounded corners erase cleanly instead of leaving stale pixels behind.
    /// </summary>
    public static Color HostColor(Control control)
    {
        for (var parent = control.Parent; parent != null; parent = parent.Parent)
        {
            if (parent is Card)
                return Surface;

            // Transparent layout panels paint nothing, so keep walking until we reach a
            // surface that really is drawn. Clearing to Color.Transparent renders as
            // black behind a non-layered window, which shows up in the rounded corners.
            if (parent.BackColor.A == 255)
                return parent.BackColor;
        }

        return Background;
    }

    public static LinearGradientBrush AccentGradient(Rectangle rect) =>
        new(rect, ControlPaint.Light(Accent, 0.16f), ControlPaint.Dark(Accent, 0.12f), LinearGradientMode.Vertical);

    public static readonly Color Success = Color.FromArgb(76, 201, 128);
    public static readonly Color Warning = Color.FromArgb(240, 196, 70);
    public static readonly Color Error = Color.FromArgb(240, 92, 92);
    public static readonly Color Info = Color.FromArgb(110, 168, 254);

    public static bool IsLightBackground
    {
        get
        {
            var background = Background;
            return (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) > 140;
        }
    }

    public static Font UiFont(float size, FontStyle style = FontStyle.Regular) =>
        new(SettingsManager.Current.FontFamily, size * Math.Clamp(SettingsManager.Current.FontSize, 8f, 16f) / 9.5f, style);
    public static Font MonoFont(float size) => new(SettingsManager.Current.FontFamily, size * Math.Clamp(SettingsManager.Current.FontSize, 8f, 16f) / 9.5f);

    public static Color RemapColor(Color color, Color previousAccent)
    {
        if (color == previousAccent)
            return Accent;
        foreach (var palette in Palettes)
        {
            if (color == palette.Background) return Background;
            if (color == palette.Surface) return Surface;
            if (color == palette.Field) return Field;
            if (color == palette.Border) return Border;
            if (color == palette.Text) return Text;
            if (color == palette.Muted) return Muted;
        }
        return color;
    }

    public static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        if (d <= 0)
        {
            path.AddRectangle(r);
            return path;
        }

        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
