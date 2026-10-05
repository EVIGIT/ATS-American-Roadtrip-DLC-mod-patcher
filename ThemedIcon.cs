using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace TruckersToolKit;
internal static class ThemedIcon
{
    private static readonly Dictionary<string, Icon> IconCache = new();
    private static readonly Dictionary<string, Bitmap> LogoCache = new();

    public static Icon? WindowIcon() => BuildIcon(Theme.Accent);

    public static Bitmap? Logo() => BuildLogo(Theme.Accent);

    private static string Key(Color accent) => accent.ToArgb().ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static Icon? BuildIcon(Color accent)
    {
        var key = Key(accent);
        if (IconCache.TryGetValue(key, out var cached))
            return cached;

        try
        {
            using var stream = ConverterForm.LoadResource("app.ico");
            if (stream == null)
                return null;
            using var original = new Icon(stream);
            using var source = original.ToBitmap();
            var icon = Icon.FromHandle(Tint(source, accent).GetHicon());
            IconCache[key] = icon;
            return icon;
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap? BuildLogo(Color accent)
    {
        var key = Key(accent);
        if (LogoCache.TryGetValue(key, out var cached))
            return cached;

        try
        {
            using var stream = ConverterForm.LoadResource("logo.png");
            if (stream == null)
                return null;
            using var image = Image.FromStream(stream);
            var tinted = Tint(image, accent);
            LogoCache[key] = tinted;
            return tinted;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Recolours the artwork to the accent while keeping the original luminance, so the
    /// shape and shading of the logo survive the theme change.
    /// </summary>
    private static Bitmap Tint(Image source, Color accent)
    {
        var bitmap = new Bitmap(source.Width, source.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(source, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        }

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.A == 0)
                    continue;

                var luminance = (0.2126 * pixel.R + 0.7152 * pixel.G + 0.0722 * pixel.B) / 255.0;
                var shade = 0.35 + 0.85 * luminance;
                bitmap.SetPixel(x, y, Color.FromArgb(
                    pixel.A,
                    Clamp(accent.R * shade),
                    Clamp(accent.G * shade),
                    Clamp(accent.B * shade)));
            }
        }

        return bitmap;
    }

    private static int Clamp(double value) => (int)Math.Clamp(value, 0, 255);
}
