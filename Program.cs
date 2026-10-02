using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ATSRoadTripConverter;

// Settings persistence classes
public sealed class VehicleTypeCustom
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int SpeedLimit { get; set; } = 163;
    public string Tag { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsBuiltIn { get; set; } = false;
}

public sealed class DealerPreset
{
    public string Name { get; set; } = "";
    public string DealerId { get; set; } = "";
    public string VehicleType { get; set; } = "pickup";
    public bool MoveVehicleAssets { get; set; } = true;
    public bool TranslateDealer { get; set; } = true;
    public bool PatchOnly { get; set; } = false;
    public string ReferenceFile { get; set; } = "";
}

public sealed class ConversionProfile
{
    public string Name { get; set; } = "";
    public string InputFile { get; set; } = "";
    public string OutputFolder { get; set; } = "";
    public string DealerId { get; set; } = "";
    public string VehicleType { get; set; } = "pickup";
    public bool MoveVehicleAssets { get; set; } = true;
    public bool TranslateDealer { get; set; } = true;
    public bool PatchOnly { get; set; } = false;
    public string ReferenceFile { get; set; } = "";
}

public sealed class AppSettings
{
    public string DefaultDealerId { get; set; } = "volvo";
    public string DefaultVehicleType { get; set; } = "pickup";
    public string DefaultOutputFolder { get; set; } = "";
    public string AccentColor { get; set; } = "#FF9128";
    public float FontSize { get; set; } = 9.5f;
    public string FontFamily { get; set; } = "Segoe UI";
    public bool AutoSaveSettings { get; set; } = true;
    public bool VerboseLogging { get; set; } = false;
    public bool BackupOriginal { get; set; } = false;

    // Theme settings
    public string ThemeMode { get; set; } = "Dark"; // Legacy preference migrated to ThemeName.
    public string ThemeName { get; set; } = "";

    public List<VehicleTypeCustom> CustomVehicleTypes { get; set; } = new();
    public List<DealerPreset> DealerPresets { get; set; } = new();
    public List<ConversionProfile> ConversionProfiles { get; set; } = new();
}

public static class SettingsManager
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ATSRoadTripConverter",
        "settings.json");

    public static AppSettings Current { get; private set; } = new();

    public static void Load()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath)!;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null)
                    Current = settings;
            }
        }
        catch
        {
            Current = new AppSettings();
        }

        if (string.IsNullOrWhiteSpace(Current.ThemeName))
            Current.ThemeName = string.Equals(Current.ThemeMode, "Light", StringComparison.OrdinalIgnoreCase) ? "Daylight" : "Roadtrip";
        if (!Theme.Palettes.Any(palette => palette.Name.Equals(Current.ThemeName, StringComparison.OrdinalIgnoreCase)))
            Current.ThemeName = "Roadtrip";

        // Ensure built-in vehicle types exist
        EnsureBuiltInVehicleTypes();
    }

    public static void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath)!;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            var json = JsonSerializer.Serialize(Current, options);
            File.WriteAllText(SettingsPath, json);
        }
        catch (Exception ex)
        {
            // Silently fail - settings are not critical
            System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
        }
    }

    private static void EnsureBuiltInVehicleTypes()
    {
        var builtInTypes = new[]
        {
            new VehicleTypeCustom { Name = "sedan", DisplayName = "Sedan", SpeedLimit = 190, Tag = "sedan", Description = "Standard sedan", IsBuiltIn = true },
            new VehicleTypeCustom { Name = "hatchback", DisplayName = "Hatchback", SpeedLimit = 180, Tag = "hatchback", Description = "Compact hatchback", IsBuiltIn = true },
            new VehicleTypeCustom { Name = "pickup", DisplayName = "Pickup", SpeedLimit = 163, Tag = "pickup", Description = "Standard pickup truck", IsBuiltIn = true },
            new VehicleTypeCustom { Name = "van", DisplayName = "Van", SpeedLimit = 140, Tag = "van", Description = "Van or delivery vehicle", IsBuiltIn = true }
        };

        foreach (var builtIn in builtInTypes)
        {
            if (!Current.CustomVehicleTypes.Any(v => v.Name.Equals(builtIn.Name, StringComparison.OrdinalIgnoreCase)))
            {
                Current.CustomVehicleTypes.Add(builtIn);
            }
        }
    }

    public static IEnumerable<VehicleTypeCustom> GetAllVehicleTypes()
    {
        return Current.CustomVehicleTypes.OrderBy(v => v.IsBuiltIn ? 0 : 1).ThenBy(v => v.Name);
    }

    public static VehicleTypeCustom? GetVehicleType(string name)
    {
        return Current.CustomVehicleTypes.FirstOrDefault(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public static void AddOrUpdateVehicleType(VehicleTypeCustom vt)
    {
        var existing = Current.CustomVehicleTypes.FirstOrDefault(v => v.Name.Equals(vt.Name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.DisplayName = vt.DisplayName;
            existing.SpeedLimit = vt.SpeedLimit;
            existing.Tag = vt.Tag;
            existing.Description = vt.Description;
        }
        else
        {
            Current.CustomVehicleTypes.Add(vt);
        }
        Save();
    }

    public static bool DeleteVehicleType(string name)
    {
        var vt = Current.CustomVehicleTypes.FirstOrDefault(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (vt != null && !vt.IsBuiltIn)
        {
            Current.CustomVehicleTypes.Remove(vt);
            Save();
            return true;
        }
        return false;
    }
}

internal static class Program
{
    public const string AppName = "ATS American Roadtrip Car Patcher";
    public const string AppVersion = "v1.3";

    [STAThread]
    static void Main()
    {
        SettingsManager.Load();
        ApplicationConfiguration.Initialize();
        Application.Run(new ConverterForm());
    }
}

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
        new ThemePalette("Roadtrip", Color.FromArgb(17, 19, 24), Color.FromArgb(26, 29, 36), Color.FromArgb(35, 39, 48), Color.FromArgb(48, 53, 64), Color.FromArgb(232, 234, 240), Color.FromArgb(140, 147, 163), Color.FromArgb(255, 145, 40)),
        new ThemePalette("Midnight", Color.FromArgb(16, 25, 34), Color.FromArgb(23, 36, 47), Color.FromArgb(32, 49, 63), Color.FromArgb(48, 69, 84), Color.FromArgb(231, 240, 245), Color.FromArgb(145, 167, 180), Color.FromArgb(71, 190, 219)),
        new ThemePalette("Evergreen", Color.FromArgb(19, 29, 25), Color.FromArgb(27, 41, 35), Color.FromArgb(37, 56, 47), Color.FromArgb(53, 75, 64), Color.FromArgb(231, 240, 233), Color.FromArgb(148, 169, 154), Color.FromArgb(93, 190, 119)),
        new ThemePalette("Ember", Color.FromArgb(32, 24, 25), Color.FromArgb(45, 32, 33), Color.FromArgb(61, 43, 43), Color.FromArgb(80, 56, 55), Color.FromArgb(244, 233, 230), Color.FromArgb(179, 151, 147), Color.FromArgb(231, 116, 91)),
        new ThemePalette("Daylight", Color.FromArgb(244, 246, 249), Color.FromArgb(255, 255, 255), Color.FromArgb(235, 238, 243), Color.FromArgb(205, 211, 220), Color.FromArgb(32, 37, 45), Color.FromArgb(99, 108, 121), Color.FromArgb(35, 117, 180))
    });

    private static ThemePalette CurrentPalette =>
        Palettes.FirstOrDefault(palette => palette.Name.Equals(SettingsManager.Current.ThemeName, StringComparison.OrdinalIgnoreCase)) ?? Palettes[0];

    public static Color Background => CurrentPalette.Background;
    public static Color Surface => CurrentPalette.Surface;
    public static Color Field => CurrentPalette.Field;
    public static Color Border => CurrentPalette.Border;
    public static Color Text => CurrentPalette.Text;
    public static Color Muted => CurrentPalette.Muted;
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
                return Color.FromArgb(255, 145, 40);
            }
        }
    }
    public static Color AccentHover => ControlPaint.Light(Accent, 0.2f);
    public static readonly Color Success = Color.FromArgb(76, 201, 128);
    public static readonly Color Warning = Color.FromArgb(240, 196, 70);
    public static readonly Color Error = Color.FromArgb(240, 92, 92);
    public static readonly Color Info = Color.FromArgb(110, 168, 254);

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

internal sealed class ThemeSwatch : Control
{
    private bool _selected;

    public ThemePalette Palette { get; }
    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value)
                return;
            _selected = value;
            Invalidate();
        }
    }

    public ThemeSwatch(ThemePalette palette)
    {
        Palette = palette;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Size = new Size(96, 52);
        Margin = new Padding(3);
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleRole = AccessibleRole.RadioButton;
        AccessibleName = $"{palette.Name} color theme";
        Font = Theme.UiFont(8.5f, FontStyle.Bold);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            OnClick(EventArgs.Empty);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Background);
        using var surface = new SolidBrush(Palette.Surface);
        using var background = new SolidBrush(Palette.Background);
        using var swatchSurface = new SolidBrush(Palette.Surface);
        using var accent = new SolidBrush(Palette.Accent);
        g.FillRectangle(surface, ClientRectangle);

        var swatch = new Rectangle(6, 6, Width - 12, 12);
        var segmentWidth = swatch.Width / 3;
        g.FillRectangle(background, swatch.X, swatch.Y, segmentWidth, swatch.Height);
        g.FillRectangle(swatchSurface, swatch.X + segmentWidth, swatch.Y, segmentWidth, swatch.Height);
        g.FillRectangle(accent, swatch.X + segmentWidth * 2, swatch.Y, swatch.Width - segmentWidth * 2, swatch.Height);

        TextRenderer.DrawText(g, Palette.Name, Font, new Rectangle(6, 22, Width - 12, 22), Palette.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        using var border = new Pen(_selected ? Palette.Accent : Theme.Border, _selected ? 2f : 1f);
        g.DrawRectangle(border, new Rectangle(0, 0, Width - 1, Height - 1));
    }
}

internal sealed class Card : Panel
{
    public int CornerRadius { get; set; } = 10;
    public bool DrawBorder { get; set; } = true;

    public Card()
    {
        DoubleBuffered = true;
        BackColor = Theme.Background;
        Padding = new Padding(18);
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Theme.Rounded(rect, CornerRadius);
        using var fill = new SolidBrush(Theme.Surface);
        e.Graphics.FillPath(fill, path);
        if (DrawBorder)
        {
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawPath(pen, path);
        }
    }
}

internal sealed class FlatButton : Control
{
    private bool _hover;
    private bool _pressed;

    public bool Primary { get; set; }

    public FlatButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Opaque, true);
        BackColor = Theme.Surface;
        Cursor = Cursors.Hand;
        Font = Theme.UiFont(9.5f, FontStyle.Bold);
        Height = 34;
        Margin = Padding.Empty;
        Padding = Padding.Empty;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;

        Color fill, text;
        if (!Enabled)
        {
            fill = Theme.Field;
            text = Theme.Muted;
        }
        else if (Primary)
        {
            fill = _pressed ? Theme.Accent : _hover ? Theme.AccentHover : Theme.Accent;
            text = Color.FromArgb(24, 20, 14);
        }
        else
        {
            fill = _pressed ? Theme.Border : _hover ? Color.FromArgb(44, 49, 60) : Theme.Field;
            text = Theme.Text;
        }

        var rect = ClientRectangle;
        using var brush = new SolidBrush(fill);
        g.FillRectangle(brush, rect);
        TextRenderer.DrawText(g, Text, Font, rect, text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    protected override void OnClick(EventArgs e)
    {
        if (Enabled)
            base.OnClick(e);
    }
}

internal sealed class ThemedComboBox : ComboBox
{
    public ThemedComboBox()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        DrawMode = DrawMode.OwnerDrawFixed;
        DropDownStyle = ComboBoxStyle.DropDownList;
        FlatStyle = FlatStyle.Flat;
        BackColor = Theme.Field;
        ForeColor = Theme.Text;
        Font = Theme.UiFont(10f);
        Margin = Padding.Empty;
        Padding = new Padding(8, 4, 26, 4);
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || Items.Count == 0)
        {
            base.OnDrawItem(e);
            return;
        }

        e.DrawBackground();
        var backColor = (e.State & DrawItemState.Selected) == DrawItemState.Selected ? Theme.Accent : Theme.Surface;
        var textColor = (e.State & DrawItemState.Selected) == DrawItemState.Selected ? Color.FromArgb(24, 20, 14) : Theme.Text;

        using var fill = new SolidBrush(backColor);
        e.Graphics.FillRectangle(fill, e.Bounds);

        var itemText = Items[e.Index]?.ToString() ?? string.Empty;
        var bounds = new Rectangle(e.Bounds.X + 8, e.Bounds.Y + 1, e.Bounds.Width - 12, e.Bounds.Height - 2);
        TextRenderer.DrawText(e.Graphics, itemText, Font, bounds, textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        if ((e.State & DrawItemState.Focus) == DrawItemState.Focus)
        {
            using var focus = new Pen(Theme.Accent, 1f);
            e.Graphics.DrawRectangle(focus, new Rectangle(e.Bounds.X + 1, e.Bounds.Y + 1, e.Bounds.Width - 3, e.Bounds.Height - 3));
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var background = new SolidBrush(BackColor))
            g.FillRectangle(background, rect);

        var selectedText = SelectedItem?.ToString() ?? string.Empty;
        var textRect = new Rectangle(8, 0, Math.Max(0, Width - 34), Height);
        TextRenderer.DrawText(g, selectedText, Font, textRect, ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        using (var border = new Pen(Theme.Border, 1f))
            g.DrawRectangle(border, rect);

        var arrowRect = new Rectangle(Width - 18, (Height - 10) / 2, 8, 10);
        using var triangleBrush = new SolidBrush(Theme.Muted);
        var points = new[]
        {
            new Point(arrowRect.Left, arrowRect.Top),
            new Point(arrowRect.Right, arrowRect.Top),
            new Point(arrowRect.Left + arrowRect.Width / 2, arrowRect.Bottom)
        };
        g.FillPolygon(triangleBrush, points);
    }
}

internal sealed class ToggleSwitch : Control
{
    private bool _checked;

    public event EventHandler? CheckedChanged;

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value)
                return;
            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string Description { get; set; } = "";

    public ToggleSwitch()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Theme.Surface;
        Cursor = Cursors.Hand;
        Height = 62;
        Font = Theme.UiFont(10.5f, FontStyle.Bold);
        Margin = Padding.Empty;
        Padding = Padding.Empty;
    }

    protected override void OnClick(EventArgs e)
    {
        Checked = !Checked;
        base.OnClick(e);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        var track = new Rectangle(0, 9, 40, 20);
        var trackColor = _checked ? Theme.Accent : Theme.Field;
        using (var trackPath = Theme.Rounded(track, 10))
        using (var trackFill = new SolidBrush(trackColor))
        {
            g.FillPath(trackFill, trackPath);
        }

        var knobSize = 14;
        var knobX = _checked ? track.Right - knobSize - 2 : track.X + 2;
        var knobRect = new Rectangle(knobX, track.Y + 3, knobSize, knobSize);
        using (var knob = new SolidBrush(Color.White))
            g.FillEllipse(knob, knobRect);

        var titleArea = new Rectangle(52, 2, Width - 58, 20);
        TextRenderer.DrawText(g, Text, Font, titleArea, Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        using var small = Theme.UiFont(8.9f);
        var descArea = new Rectangle(52, 24, Width - 58, Height - 28);
        TextRenderer.DrawText(g, Description, small, descArea, Theme.Muted,
            TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

internal sealed class SlimProgress : Control
{
    private int _value;

    public int Value
    {
        get => _value;
        set
        {
            _value = Math.Clamp(value, 0, 100);
            Invalidate();
        }
    }

    public SlimProgress()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Height = 6;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Background);

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = Theme.Rounded(rect, Height / 2))
        using (var brush = new SolidBrush(Theme.Field))
            g.FillPath(brush, path);

        var filled = (int)((Width - 1) * (_value / 100.0));
        if (filled < Height)
            return;

        using var fillPath = Theme.Rounded(new Rectangle(0, 0, filled, Height - 1), Height / 2);
        using var fill = new LinearGradientBrush(rect, Theme.Accent, Theme.AccentHover, LinearGradientMode.Horizontal);
        g.FillPath(fill, fillPath);
    }
}

public sealed class ConverterForm : Form
{
    private const int Margin_ = 24;
    private const int ContentWidth = 812;
    private Panel? _activePage;
    private Dictionary<Control, bool>? _mainViewPreviousVisibility;

    private readonly TextBox _input = CreateField(readOnly: true);
    private readonly TextBox _outputFolder = CreateField(readOnly: true);
    private readonly TextBox _dealerId = CreateField(readOnly: false);
    private readonly TextBox _reference = CreateField(readOnly: true);

    private readonly ThemedComboBox _vehicleType = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        FlatStyle = FlatStyle.Flat,
        BackColor = Theme.Field,
        ForeColor = Theme.Text,
        Font = Theme.UiFont(10f)
    };

    private readonly Label _outputFile = new()
    {
        AutoSize = false,
        ForeColor = Theme.Muted,
        Font = Theme.UiFont(9f),
        AutoEllipsis = true
    };

    private readonly ToggleSwitch _patchOnly = new()
    {
        Text = "Definitions-only patch (recommended)",
        Description = "Small patch loaded above the original mod; copies and converts models and textures."
    };

    private readonly ToggleSwitch _moveVehicleAssets = new()
    {
        Text = "Move vehicle assets",
        Description = "Moves vehicle/truck models into vehicle/car (full conversion). Always copied in patch mode.",
        Checked = true
    };

    private readonly ToggleSwitch _translateDealer = new()
    {
        Text = "Create Road Trip car dealer",
        Description = "Moves the truck dealer entry to the car dealer under the dealer ID above.",
        Checked = true
    };

    private readonly FlatButton _convert = new()
    {
        Text = "PATCH FOR ROAD TRIP",
        Primary = true,
        Height = 46,
        Font = Theme.UiFont(11f, FontStyle.Bold)
    };

    private readonly FlatButton _openOutput = new()
    {
        Text = "Open output folder",
        Enabled = false
    };

    private readonly FlatButton _clearLog = new() { Text = "Clear" };

    private readonly SlimProgress _progress = new();

    private readonly Label _status = new()
    {
        AutoSize = false,
        Text = "Ready",
        ForeColor = Theme.Muted,
        Font = Theme.UiFont(9f),
        TextAlign = ContentAlignment.MiddleRight
    };

    private readonly RichTextBox _log = new()
    {
        ReadOnly = true,
        BackColor = Theme.Surface,
        ForeColor = Theme.Text,
        BorderStyle = BorderStyle.None,
        Font = Theme.MonoFont(9f),
        DetectUrls = true,
        ScrollBars = RichTextBoxScrollBars.Vertical
    };

    public ConverterForm()
    {
        Text = $"{Program.AppName} {Program.AppVersion}";
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.UiFont(9.5f);
        ClientSize = new Size(ContentWidth + Margin_ * 2, 900);
        MinimumSize = new Size(ContentWidth + Margin_ * 2 + 16, 700);
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;
        AllowDrop = true;
        using (var icon = LoadResource("app.ico"))
        {
            if (icon != null)
                Icon = new Icon(icon);
        }

        var y = BuildHeader();
        y = BuildFilesCard(y);
        y = BuildOptionsCard(y);
        y = BuildActionArea(y);
        BuildLogCard(y);

        _dealerId.Text = SettingsManager.Current.DefaultDealerId;
        _outputFolder.Text = SettingsManager.Current.DefaultOutputFolder;
        _input.TextChanged += (_, _) => { SuggestDealerId(); UpdateOutputPreview(); };
        _outputFolder.TextChanged += (_, _) => UpdateOutputPreview();
        _patchOnly.CheckedChanged += (_, _) => { UpdateMoveAssetsState(); UpdateOutputPreview(); };
        _convert.Click += async (_, _) => await ConvertAsync();
        _openOutput.Click += (_, _) => OpenOutputFolder();
        _clearLog.Click += (_, _) => _log.Clear();
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;

        _patchOnly.Checked = true;
        UpdateMoveAssetsState();
        UpdateOutputPreview();
        Write($"[INFO] {Program.AppName} {Program.AppVersion}");
        Write("[INFO] Drag an ATS mod (.scs/.zip) onto this window or click Browse to begin.");
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        TryEnableDarkTitleBar();
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private void TryEnableDarkTitleBar()
    {
        try
        {
            var enabled = 1;
            if (DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int)) != 0)
                DwmSetWindowAttribute(Handle, 19, ref enabled, sizeof(int));
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    private static Stream? LoadResource(string name) =>
        typeof(ConverterForm).Assembly.GetManifestResourceStream(name);

    private static TextBox CreateField(bool readOnly) => new()
    {
        ReadOnly = readOnly,
        BorderStyle = BorderStyle.None,
        BackColor = Theme.Field,
        ForeColor = Theme.Text,
        Font = Theme.UiFont(10f)
    };

    private int BuildHeader()
    {
        var logo = LoadResource("logo.png");
        if (logo != null)
        {
            Controls.Add(new PictureBox
            {
                Image = Image.FromStream(logo),
                SizeMode = PictureBoxSizeMode.Zoom,
                Location = new Point(Margin_, 22),
                Size = new Size(58, 58)
            });
        }

        Controls.Add(new Label
        {
            Text = Program.AppName,
            Font = Theme.UiFont(18f, FontStyle.Bold),
            ForeColor = Theme.Text,
            AutoSize = true,
            Location = new Point(Margin_ + 70, 20)
        });

        Controls.Add(new Label
        {
            Text = "Turn ATS truck-slot vehicle mods into Road Trip cars",
            Font = Theme.UiFont(10f),
            ForeColor = Theme.Muted,
            AutoSize = true,
            Location = new Point(Margin_ + 72, 56)
        });

        var badge = new Label
        {
            Text = Program.AppVersion,
            Font = Theme.UiFont(9f, FontStyle.Bold),
            ForeColor = Theme.Accent,
            BackColor = Theme.Surface,
            TextAlign = ContentAlignment.MiddleCenter,
            Size = new Size(48, 24),
            Location = new Point(Margin_ + ContentWidth - 48, 30),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        Controls.Add(badge);

        var settingsBtn = new FlatButton
        {
            Text = "Settings",
            Location = new Point(Margin_ + ContentWidth - 132, 30),
            Size = new Size(76, 24),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        settingsBtn.Click += (_, _) => ShowSettings();
        Controls.Add(settingsBtn);

        var changelogBtn = new FlatButton
        {
            Text = "Changelog",
            Location = new Point(Margin_ + ContentWidth - 216, 30),
            Size = new Size(76, 24),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        changelogBtn.Click += (_, _) => ShowChangelog();
        Controls.Add(changelogBtn);

        return 100;
    }

    private int BuildFilesCard(int y)
    {
        var card = AddCard(y, 222, "FILES");
        var inner = ContentWidth - 36;

        AddFieldRow(card, 46, "Input mod", _input, inner, () => BrowseFile(_input, "Choose ATS mod"));
        AddFieldRow(card, 112, "Output folder", _outputFolder, inner, () => BrowseFolder(_outputFolder, "Choose output folder"));

        _outputFile.Location = new Point(18, 182);
        _outputFile.Size = new Size(inner, 22);
        _outputFile.BackColor = Theme.Surface;
        _outputFile.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        card.Controls.Add(_outputFile);

        return y + 222 + 16;
    }

    private int BuildOptionsCard(int y)
    {
        var card = AddCard(y, 290, "OPTIONS");
        var half = (ContentWidth - 36 - 16) / 2;

        AddCaption(card, "Dealer ID", 18, 46);
        AddFieldBox(card, _dealerId, 18, 68, 130);
        AddCaption(card, "Vehicle type", 160, 46);
        _vehicleType.Items.Clear();
        _vehicleType.Items.AddRange(new object[] { "Sedan", "Hatchback", "Pickup", "Van" });
        var defaultVehicleType = SettingsManager.Current.DefaultVehicleType;
        var selectedVehicleType = string.Equals(defaultVehicleType, "sedan", StringComparison.OrdinalIgnoreCase) ? "Sedan" :
            string.Equals(defaultVehicleType, "hatchback", StringComparison.OrdinalIgnoreCase) ? "Hatchback" :
            string.Equals(defaultVehicleType, "pickup", StringComparison.OrdinalIgnoreCase) ? "Pickup" :
            string.Equals(defaultVehicleType, "van", StringComparison.OrdinalIgnoreCase) ? "Van" : "Pickup";
        _vehicleType.SelectedItem = selectedVehicleType;
        _vehicleType.Location = new Point(160, 72);
        _vehicleType.Width = 120;
        card.Controls.Add(_vehicleType);
        card.Controls.Add(new Label
        {
            Text = "Dealer brand (e.g. ram) and Road Trip job class",
            ForeColor = Theme.Muted,
            BackColor = Theme.Surface,
            Font = Theme.UiFont(8.75f),
            AutoSize = true,
            Location = new Point(18, 106)
        });

        var refX = 18 + half + 16 - 120;
        AddCaption(card, "Road Trip reference mod (optional)", refX, 46);
        AddFieldBox(card, _reference, refX, 68, ContentWidth - 36 - refX + 18 - 196);
        AddButton(card, "Browse", ContentWidth - 18 - 186, 66, 90, () => BrowseFile(_reference, "Choose reference mod"));
        AddButton(card, "Clear", ContentWidth - 18 - 90, 66, 90, () => _reference.Text = "");

        var divider = new Panel
        {
            BackColor = Theme.Border,
            Location = new Point(18, 140),
            Size = new Size(ContentWidth - 36, 1),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        card.Controls.Add(divider);

        PlaceToggle(card, _patchOnly, 18, 156, ContentWidth - 36);
        PlaceToggle(card, _translateDealer, 18, 218, half);
        PlaceToggle(card, _moveVehicleAssets, 18 + half + 16, 218, half);

        return y + 290 + 16;
    }

    private int BuildActionArea(int y)
    {
        _convert.Location = new Point(Margin_, y);
        _convert.Size = new Size(ContentWidth, 46);
        _convert.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        Controls.Add(_convert);

        _progress.Location = new Point(Margin_, y + 60);
        _progress.Size = new Size(ContentWidth - 200, 6);
        _progress.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        Controls.Add(_progress);

        _status.Location = new Point(Margin_ + ContentWidth - 190, y + 52);
        _status.Size = new Size(190, 22);
        _status.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        Controls.Add(_status);

        return y + 86;
    }

    private void BuildLogCard(int y)
    {
        var height = ClientSize.Height - y - Margin_;
        var card = AddCard(y, height, "LOG");
        card.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

        _openOutput.Location = new Point(ContentWidth - 18 - 170 - 8 - 70, 10);
        _openOutput.Size = new Size(170, 28);
        _openOutput.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        card.Controls.Add(_openOutput);

        _clearLog.Location = new Point(ContentWidth - 18 - 70, 10);
        _clearLog.Size = new Size(70, 28);
        _clearLog.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        card.Controls.Add(_clearLog);

        _log.Location = new Point(18, 48);
        _log.Size = new Size(ContentWidth - 36, height - 66);
        _log.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        card.Controls.Add(_log);
    }

    private Card AddCard(int y, int height, string title)
    {
        var card = new Card
        {
            Location = new Point(Margin_, y),
            Size = new Size(ContentWidth, height),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        card.Controls.Add(new Label
        {
            Text = title,
            Font = Theme.UiFont(8.5f, FontStyle.Bold),
            ForeColor = Theme.Accent,
            BackColor = Theme.Surface,
            AutoSize = true,
            Location = new Point(18, 16)
        });
        Controls.Add(card);
        return card;
    }

    private static void AddCaption(Control parent, string text, int x, int y)
    {
        parent.Controls.Add(new Label
        {
            Text = text,
            Font = Theme.UiFont(9f, FontStyle.Bold),
            ForeColor = Theme.Text,
            BackColor = Theme.Surface,
            AutoSize = true,
            Location = new Point(x, y)
        });
    }

    private static Panel AddFieldBox(Control parent, TextBox box, int x, int y, int width)
    {
        var frame = new Panel
        {
            BackColor = Theme.Field,
            Location = new Point(x, y),
            Size = new Size(width, 34),
            Padding = new Padding(10, 8, 10, 0)
        };
        frame.Paint += (_, e) =>
        {
            using var pen = new Pen(box.Focused ? Theme.Accent : Theme.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, frame.Width - 1, frame.Height - 1);
        };
        box.GotFocus += (_, _) => frame.Invalidate();
        box.LostFocus += (_, _) => frame.Invalidate();
        box.Dock = DockStyle.Fill;
        frame.Controls.Add(box);
        parent.Controls.Add(frame);
        return frame;
    }

    private void AddFieldRow(Control parent, int y, string caption, TextBox box, int innerWidth, Action browse)
    {
        AddCaption(parent, caption, 18, y);
        var frame = AddFieldBox(parent, box, 18, y + 22, innerWidth - 100);
        frame.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        var button = AddButton(parent, "Browse", 18 + innerWidth - 90, y + 22, 90, browse);
        button.Anchor = AnchorStyles.Top | AnchorStyles.Right;
    }

    private static FlatButton AddButton(Control parent, string text, int x, int y, int width, Action onClick)
    {
        var button = new FlatButton
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(width, 34)
        };
        button.Click += (_, _) => onClick();
        parent.Controls.Add(button);
        return button;
    }

    private static void PlaceToggle(Control parent, ToggleSwitch toggle, int x, int y, int width)
    {
        toggle.Location = new Point(x, y);
        toggle.Size = new Size(width, 62);
        toggle.BackColor = Theme.Surface;
        toggle.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        parent.Controls.Add(toggle);
    }

    private void BrowseFile(TextBox box, string title)
    {
        using var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = "ATS mod archives (*.scs;*.zip)|*.scs;*.zip|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
            box.Text = dialog.FileName;
    }

    private void BrowseFolder(TextBox box, string title)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = title,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
            box.Text = dialog.SelectedPath;
    }

    private static void OnDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
            return;

        var file = files[0];
        if (Directory.Exists(file))
        {
            _outputFolder.Text = file;
            return;
        }

        _input.Text = file;
        if (string.IsNullOrWhiteSpace(_outputFolder.Text))
            _outputFolder.Text = Path.GetDirectoryName(file) ?? "";
        Write($"[INFO] Input mod: {file}");
    }

    private void SuggestDealerId()
    {
        if (string.IsNullOrWhiteSpace(_input.Text))
            return;

        var name = Path.GetFileNameWithoutExtension(_input.Text);
        var first = new string(name
            .TakeWhile(c => char.IsLetterOrDigit(c))
            .ToArray())
            .ToLowerInvariant();

        if (first.Length >= 2 && !first.All(char.IsDigit) && _dealerId.Text == "volvo")
            _dealerId.Text = first;
    }

    private void UpdateMoveAssetsState()
    {
        _moveVehicleAssets.Enabled = !_patchOnly.Checked;
        _moveVehicleAssets.Description = _patchOnly.Checked
            ? "Always copied in patch mode to vehicle/car."
            : "Moves vehicle/truck models into vehicle/car (full conversion).";
        _moveVehicleAssets.Invalidate();
    }

    private void UpdateOutputPreview()
    {
        if (string.IsNullOrWhiteSpace(_input.Text) || string.IsNullOrWhiteSpace(_outputFolder.Text))
        {
            _outputFile.Text = "Output:  choose an input mod and output folder";
            return;
        }

        try
        {
            _outputFile.Text = "Output:  " + ModConverter.GetOutputFile(new ConversionSettings(
                _input.Text, _outputFolder.Text, "", "", false, false, _patchOnly.Checked));
        }
        catch
        {
            _outputFile.Text = "Output:  unable to calculate output path";
        }
    }

    private async Task ConvertAsync()
    {
        _log.Clear();
        _openOutput.Enabled = false;

        if (!File.Exists(_input.Text))
        {
            Fail("[ERROR] Please choose a valid .scs or .zip input file.");
            return;
        }

        if (!Directory.Exists(_outputFolder.Text))
        {
            Fail("[ERROR] Please choose a valid output folder.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_dealerId.Text))
        {
            Fail("[ERROR] Enter the dealer ID to use.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(_reference.Text) && !File.Exists(_reference.Text))
        {
            Fail("[ERROR] The selected reference mod no longer exists.");
            return;
        }

        _convert.Enabled = false;
        _convert.Text = "PATCHING...";
        _progress.Value = 0;
        SetStatus("Patching...", Theme.Info);

        try
        {
            var settings = new ConversionSettings(
                _input.Text,
                _outputFolder.Text,
                _dealerId.Text.Trim(),
                _reference.Text.Trim(),
                _moveVehicleAssets.Checked,
                _translateDealer.Checked,
                _patchOnly.Checked,
                _vehicleType.SelectedItem as string ?? "pickup");

            var result = await Task.Run(() =>
                ModConverter.Run(
                    settings,
                    Write,
                    p => UpdateProgress(p)));

            Write("");
            if (result.Success)
            {
                Write($"[SUCCESS] Converted file: {result.OutputFile}");
                SetStatus("Done", Theme.Success);
            }
            else
            {
                Write($"[WARNING] Converted file created, but {result.Issues.Count} validation warning/error(s) were reported.");
                SetStatus($"Done with {result.Issues.Count} issue(s)", Theme.Warning);
            }

            _progress.Value = 100;
            if (File.Exists(result.OutputFile))
                _openOutput.Enabled = true;
        }
        catch (Exception ex)
        {
            Write($"[FATAL] {ex.Message}");
            Write("[FATAL] No converted file was produced.");
            SetStatus("Failed", Theme.Error);
        }
        finally
        {
            _convert.Enabled = true;
            _convert.Text = "PATCH FOR ROAD TRIP";
        }
    }

    private void Fail(string message)
    {
        Write(message);
        SetStatus("Check inputs", Theme.Error);
    }

    private void SetStatus(string text, Color color)
    {
        _status.Text = text;
        _status.ForeColor = color;
    }

    private void UpdateProgress(int value)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<int>(UpdateProgress), value);
            return;
        }

        _progress.Value = value;
    }

    private static Color ColorFor(string line)
    {
        if (line.StartsWith("[SUCCESS]", StringComparison.Ordinal))
            return Theme.Success;
        if (line.StartsWith("[ERROR]", StringComparison.Ordinal) || line.StartsWith("[FATAL]", StringComparison.Ordinal))
            return Theme.Error;
        if (line.StartsWith("[WARN", StringComparison.Ordinal) || line.StartsWith("[COMPLETE]", StringComparison.Ordinal))
            return Theme.Warning;
        if (line.StartsWith("[INFO]", StringComparison.Ordinal))
            return Theme.Info;
        return Theme.Text;
    }

    private void Write(string line)
    {
        if (IsDisposed)
            return;

        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(Write), line);
            return;
        }

        _log.SelectionStart = _log.TextLength;
        _log.SelectionLength = 0;
        _log.SelectionColor = ColorFor(line);
        _log.AppendText(line + Environment.NewLine);
        _log.SelectionColor = _log.ForeColor;
        _log.ScrollToCaret();
    }

    private void OpenOutputFolder()
    {
        var folder = _outputFolder.Text;
        if (Directory.Exists(folder))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{folder}\"",
                UseShellExecute = true
            });
        }
    }

    private bool ShowMainPage(Panel page)
    {
        if (_activePage != null)
            return false;

        _mainViewPreviousVisibility = Controls
            .Cast<Control>()
            .ToDictionary(control => control, control => control.Visible);
        foreach (Control control in Controls)
            control.Visible = false;

        _activePage = page;
        Controls.Add(page);
        page.BringToFront();
        return true;
    }

    private void CloseActivePage()
    {
        if (_activePage == null)
            return;

        var page = _activePage;
        _activePage = null;
        Controls.Remove(page);
        page.Dispose();

        if (_mainViewPreviousVisibility == null)
            return;

        foreach (var (control, wasVisible) in _mainViewPreviousVisibility)
        {
            if (!control.IsDisposed)
                control.Visible = wasVisible;
        }
        _mainViewPreviousVisibility = null;
    }

    private void StartUpdater(LocalUpdatePlan plan)
    {
        var script = LocalUpdater.CreatePowerShellScript(plan, waitForAppExit: true, relaunchApp: true, showErrorDialog: true);
        var encodedScript = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-WindowStyle");
        startInfo.ArgumentList.Add("Hidden");
        startInfo.ArgumentList.Add("-EncodedCommand");
        startInfo.ArgumentList.Add(encodedScript);

        if (Process.Start(startInfo) == null)
            throw new InvalidOperationException("Could not start the updater process.");

        SettingsManager.Save();
        Close();
    }

    private async Task BeginGitHubUpdateAsync(FlatButton updateButton)
    {
        if (Environment.GetEnvironmentVariable("DOTNET_WATCH") == "1")
        {
            MessageBox.Show(this, "Close the Hot Reload session before updating this app.", "Update unavailable", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var originalText = updateButton.Text;
        GitHubReleasePackage? package = null;
        updateButton.Enabled = false;
        updateButton.Text = "Checking GitHub...";
        try
        {
            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath))
                throw new InvalidOperationException("Could not determine the current app executable.");

            package = await Task.Run(() => GitHubReleaseClient.DownloadLatestRelease(
                Program.AppVersion,
                Path.GetFileName(executablePath)));

            if (!LocalUpdater.TryCreatePlan(
                    package.BuildDirectory,
                    AppContext.BaseDirectory,
                    executablePath,
                    out var plan,
                    out var error,
                    package.TemporaryDirectory))
            {
                throw new InvalidOperationException(error);
            }

            var confirmation = MessageBox.Show(
                this,
                $"Version {package.TagName} is ready. The app will close, install the release, and restart. Continue?",
                "Install GitHub update",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (confirmation != DialogResult.Yes)
                return;

            StartUpdater(plan!);
            package = null;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "GitHub update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            if (package != null)
                GitHubReleaseClient.TryDeleteDirectory(package.TemporaryDirectory);
            if (!updateButton.IsDisposed)
            {
                updateButton.Enabled = true;
                updateButton.Text = originalText;
            }
        }
    }

    private void ShowSettings()
    {
        var settings = SettingsManager.Current;
        var settingsSaved = false;
        var settingsPage = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Background,
            ForeColor = Theme.Text
        };

        var mainPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Background
        };

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 76,
            BackColor = Theme.Background
        };
        var backBtn = new FlatButton
        {
            Text = "Back",
            Size = new Size(76, 34),
            Location = new Point(Margin_, 20)
        };
        backBtn.Click += (_, _) =>
        {
            if (!settingsSaved)
                SettingsManager.Save();
            CloseActivePage();
        };
        header.Controls.Add(backBtn);
        header.Controls.Add(new Label
        {
            Text = "Settings",
            Font = Theme.UiFont(17f, FontStyle.Bold),
            ForeColor = Theme.Text,
            AutoSize = true,
            Location = new Point(Margin_ + 92, 4)
        });
        header.Controls.Add(new Label
        {
            Text = $"{Program.AppName}  |  Version {Program.AppVersion}",
            Font = Theme.UiFont(9.5f),
            ForeColor = Theme.Muted,
            AutoSize = true,
            Location = new Point(Margin_ + 93, 38)
        });

        var sidebar = new Panel
        {
            Dock = DockStyle.Left,
            Width = 190,
            BackColor = Theme.Surface,
            Padding = new Padding(0, 20, 0, 0)
        };

        var selectedCategory = "General";
        var contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Background,
            Padding = new Padding(24, 24, 24, 24)
        };

        var contentScroll = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Background,
            AutoScroll = true
        };

        contentPanel.Controls.Add(contentScroll);
        mainPanel.Controls.Add(contentPanel);
        mainPanel.Controls.Add(sidebar);
        settingsPage.Controls.Add(mainPanel);
        settingsPage.Controls.Add(header);

        var defaultDealerId = settings.DefaultDealerId;
        var defaultVehicleType = settings.DefaultVehicleType;
        var defaultOutputFolder = settings.DefaultOutputFolder;
        var accentColor = settings.AccentColor;
        var fontFamily = settings.FontFamily;
        var fontSize = Math.Clamp(settings.FontSize, 8f, 16f);
        var previewFontSize = fontSize;
        var selectedTheme = settings.ThemeName;
        TextBox? accentInput = null;
        var autoSaveSettings = settings.AutoSaveSettings;
        var verboseLogging = settings.VerboseLogging;
        var backupOriginal = settings.BackupOriginal;

        void PreviewFontSettings()
        {
            var nextFontSize = Math.Clamp(fontSize, 8f, 16f);
            var scale = nextFontSize / previewFontSize;
            settings.FontFamily = fontFamily;
            settings.FontSize = nextFontSize;
            ApplySettingsAppearance(this, scale, Theme.Accent);
            ApplySettingsAppearance(settingsPage, scale, Theme.Accent);
            previewFontSize = nextFontSize;
        }

        void UpdateContent(string category)
        {
            contentScroll.Controls.Clear();
            var y = 0;
            var fieldWidth = Math.Max(260, contentScroll.ClientSize.Width - 30);

            var title = new Label
            {
                Text = category,
                Font = Theme.UiFont(16f, FontStyle.Bold),
                ForeColor = Theme.Text,
                AutoSize = true,
                Location = new Point(0, y)
            };
            contentScroll.Controls.Add(title);
            y += 38;

            void AddField(string label, string description, Control input)
            {
                contentScroll.Controls.Add(new Label
                {
                    Text = label,
                    Font = Theme.UiFont(10f, FontStyle.Bold),
                    ForeColor = Theme.Text,
                    AutoSize = true,
                    Location = new Point(0, y)
                });
                contentScroll.Controls.Add(new Label
                {
                    Text = description,
                    Font = Theme.UiFont(8.75f),
                    ForeColor = Theme.Muted,
                    AutoSize = true,
                    Location = new Point(0, y + 22)
                });
                input.Location = new Point(0, y + 44);
                input.Size = new Size(fieldWidth, 30);
                input.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                contentScroll.Controls.Add(input);
                y += 86;
            }

            switch (category)
            {
                case "General":
                    var dealerIdInput = new TextBox
                    {
                        Text = defaultDealerId,
                        BackColor = Theme.Field,
                        ForeColor = Theme.Text,
                        BorderStyle = BorderStyle.FixedSingle
                    };
                    dealerIdInput.TextChanged += (_, _) => defaultDealerId = dealerIdInput.Text;
                    AddField("Default Dealer ID", "Used for new conversions", dealerIdInput);

                    var vehicleTypeInput = new ThemedComboBox
                    {
                        DropDownStyle = ComboBoxStyle.DropDownList,
                        BackColor = Theme.Field,
                        ForeColor = Theme.Text,
                        FlatStyle = FlatStyle.Flat
                    };
                    vehicleTypeInput.Items.Clear();
                    vehicleTypeInput.Items.AddRange(new object[] { "Sedan", "Hatchback", "Pickup", "Van" });
                    var selectedVehicleTypeSetting = string.Equals(defaultVehicleType, "sedan", StringComparison.OrdinalIgnoreCase) ? "Sedan" :
                        string.Equals(defaultVehicleType, "hatchback", StringComparison.OrdinalIgnoreCase) ? "Hatchback" :
                        string.Equals(defaultVehicleType, "pickup", StringComparison.OrdinalIgnoreCase) ? "Pickup" :
                        string.Equals(defaultVehicleType, "van", StringComparison.OrdinalIgnoreCase) ? "Van" : "Pickup";
                    vehicleTypeInput.SelectedItem = selectedVehicleTypeSetting;
                    vehicleTypeInput.SelectedIndexChanged += (_, _) =>
                    {
                        defaultVehicleType = (vehicleTypeInput.SelectedItem?.ToString() ?? "Pickup").ToLowerInvariant();
                    };
                    AddField("Default Vehicle Type", "Used for new conversions", vehicleTypeInput);

                    var outputFolderRow = new Panel { Size = new Size(fieldWidth, 30), BackColor = Color.Transparent };
                    var outputFolderInput = new TextBox
                    {
                        Text = defaultOutputFolder,
                        BackColor = Theme.Field,
                        ForeColor = Theme.Text,
                        BorderStyle = BorderStyle.FixedSingle,
                        Location = new Point(0, 0),
                        Size = new Size(fieldWidth - 90, 30),
                        Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
                    };
                    outputFolderInput.TextChanged += (_, _) => defaultOutputFolder = outputFolderInput.Text;
                    var browseButton = new FlatButton
                    {
                        Text = "Browse",
                        Size = new Size(80, 30),
                        Location = new Point(fieldWidth - 80, 0),
                        Anchor = AnchorStyles.Top | AnchorStyles.Right
                    };
                    browseButton.Click += (_, _) =>
                    {
                        using var dialog = new FolderBrowserDialog { SelectedPath = outputFolderInput.Text };
                        if (dialog.ShowDialog(settingsPage) == DialogResult.OK)
                            outputFolderInput.Text = dialog.SelectedPath;
                    };
                    outputFolderRow.Controls.Add(outputFolderInput);
                    outputFolderRow.Controls.Add(browseButton);
                    AddField("Default Output Folder", "Where converted mods are written", outputFolderRow);
                    break;

                case "Customization":
                    contentScroll.Controls.Add(new Label
                    {
                        Text = "Color theme",
                        Font = Theme.UiFont(10f, FontStyle.Bold),
                        ForeColor = Theme.Text,
                        AutoSize = true,
                        Location = new Point(0, y)
                    });
                    contentScroll.Controls.Add(new Label
                    {
                        Text = "Choose a coordinated palette for the app.",
                        Font = Theme.UiFont(8.75f),
                        ForeColor = Theme.Muted,
                        AutoSize = true,
                        Location = new Point(0, y + 22)
                    });

                    var themeSwatchRow = new FlowLayoutPanel
                    {
                        Location = new Point(0, y + 44),
                        Size = new Size(fieldWidth, 58),
                        BackColor = Theme.Background,
                        FlowDirection = FlowDirection.LeftToRight,
                        WrapContents = true,
                        Margin = Padding.Empty,
                        Padding = Padding.Empty
                    };
                    foreach (var palette in Theme.Palettes)
                    {
                        var swatch = new ThemeSwatch(palette)
                        {
                            Selected = palette.Name.Equals(selectedTheme, StringComparison.OrdinalIgnoreCase)
                        };
                        swatch.Click += (_, _) =>
                        {
                            var previousAccent = Theme.Accent;
                            selectedTheme = palette.Name;
                            settings.ThemeName = selectedTheme;
                            var presetAccent = Theme.PresetAccent(selectedTheme);
                            accentColor = $"#{presetAccent.R:X2}{presetAccent.G:X2}{presetAccent.B:X2}";
                            settings.AccentColor = accentColor;
                            if (accentInput != null)
                                accentInput.Text = accentColor;
                            foreach (var option in themeSwatchRow.Controls.OfType<ThemeSwatch>())
                                option.Selected = option.Palette.Name == selectedTheme;
                            SettingsManager.Save();
                            ApplySettingsAppearance(this, 1f, previousAccent);
                            ApplySettingsAppearance(settingsPage, 1f, previousAccent);
                        };
                        themeSwatchRow.Controls.Add(swatch);
                    }
                    contentScroll.Controls.Add(themeSwatchRow);
                    y += 112;

                    var accentRow = new Panel { Size = new Size(fieldWidth, 30), BackColor = Color.Transparent };
                    accentInput = new TextBox
                    {
                        Text = accentColor,
                        BackColor = Theme.Field,
                        ForeColor = Theme.Text,
                        BorderStyle = BorderStyle.FixedSingle,
                        Location = new Point(0, 0),
                        Size = new Size(fieldWidth - 90, 30),
                        Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
                    };
                    accentInput.TextChanged += (_, _) =>
                    {
                        accentColor = accentInput.Text;
                        try
                        {
                            var selectedAccent = ColorTranslator.FromHtml(accentColor);
                            if (selectedAccent.IsEmpty)
                                return;

                            var previousAccent = Theme.Accent;
                            accentColor = $"#{selectedAccent.R:X2}{selectedAccent.G:X2}{selectedAccent.B:X2}";
                            settings.AccentColor = accentColor;
                            ApplySettingsAppearance(this, 1f, previousAccent);
                            ApplySettingsAppearance(settingsPage, 1f, previousAccent);
                        }
                        catch (ArgumentException)
                        {
                        }
                    };
                    var colorButton = new FlatButton
                    {
                        Text = "Choose...",
                        Size = new Size(80, 30),
                        Location = new Point(fieldWidth - 80, 0),
                        Anchor = AnchorStyles.Top | AnchorStyles.Right
                    };
                    colorButton.Click += (_, _) =>
                    {
                        using var dialog = new ColorDialog { FullOpen = true };
                        try
                        {
                            dialog.Color = ColorTranslator.FromHtml(accentInput.Text);
                        }
                        catch
                        {
                            dialog.Color = Theme.Accent;
                        }
                        if (dialog.ShowDialog(settingsPage) == DialogResult.OK)
                            accentInput.Text = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
                    };
                    accentRow.Controls.Add(accentInput);
                    accentRow.Controls.Add(colorButton);
                    AddField("Accent Color", "Hex color used for buttons and highlights", accentRow);

                    var fontFamilyInput = new ThemedComboBox
                    {
                        DropDownStyle = ComboBoxStyle.DropDownList,
                        BackColor = Theme.Field,
                        ForeColor = Theme.Text,
                        FlatStyle = FlatStyle.Flat
                    };
                    using (var installedFonts = new InstalledFontCollection())
                    {
                        var availableFonts = installedFonts.Families
                            .Where(family => family.IsStyleAvailable(FontStyle.Regular))
                            .Select(family => family.Name)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                            .Cast<object>()
                            .ToArray();
                        fontFamilyInput.Items.AddRange(availableFonts);
                    }
                    if (!fontFamilyInput.Items.Contains(fontFamily))
                        fontFamilyInput.Items.Insert(0, fontFamily);
                    fontFamilyInput.SelectedItem = fontFamily;
                    fontFamilyInput.SelectedIndexChanged += (_, _) =>
                    {
                        fontFamily = fontFamilyInput.SelectedItem?.ToString() ?? "Segoe UI";
                        PreviewFontSettings();
                    };
                    AddField("Font Family", "Typeface used throughout the interface", fontFamilyInput);

                    var fontSizeInput = new NumericUpDown
                    {
                        Minimum = 8,
                        Maximum = 16,
                        Increment = 0.5m,
                        DecimalPlaces = 1,
                        Value = (decimal)Math.Clamp(fontSize, 8f, 16f),
                        BackColor = Theme.Field,
                        ForeColor = Theme.Text
                    };
                    fontSizeInput.ValueChanged += (_, _) =>
                    {
                        fontSize = (float)fontSizeInput.Value;
                        PreviewFontSettings();
                    };
                    AddField("Font Size", "Base interface text size", fontSizeInput);
                    break;

                case "Advanced":
                    var autoSaveInput = new CheckBox
                    {
                        Text = "Automatically save settings after conversion",
                        Checked = autoSaveSettings,
                        ForeColor = Theme.Text,
                        BackColor = Color.Transparent,
                        AutoSize = true,
                        Location = new Point(0, y)
                    };
                    autoSaveInput.CheckedChanged += (_, _) => autoSaveSettings = autoSaveInput.Checked;
                    contentScroll.Controls.Add(autoSaveInput);
                    y += 42;

                    var verboseInput = new CheckBox
                    {
                        Text = "Show detailed conversion logs",
                        Checked = verboseLogging,
                        ForeColor = Theme.Text,
                        BackColor = Color.Transparent,
                        AutoSize = true,
                        Location = new Point(0, y)
                    };
                    verboseInput.CheckedChanged += (_, _) => verboseLogging = verboseInput.Checked;
                    contentScroll.Controls.Add(verboseInput);
                    y += 42;

                    var backupInput = new CheckBox
                    {
                        Text = "Create a backup of the original mod",
                        Checked = backupOriginal,
                        ForeColor = Theme.Text,
                        BackColor = Color.Transparent,
                        AutoSize = true,
                        Location = new Point(0, y)
                    };
                    backupInput.CheckedChanged += (_, _) => backupOriginal = backupInput.Checked;
                    contentScroll.Controls.Add(backupInput);
                    y += 46;

                    contentScroll.Controls.Add(new Label
                    {
                        Text = "Install a compiled build from a local output folder.",
                        Font = Theme.UiFont(8.75f),
                        ForeColor = Theme.Muted,
                        AutoSize = true,
                        Location = new Point(0, y)
                    });
                    y += 24;

                    var updateButton = new FlatButton
                    {
                        Text = "Check GitHub for updates...",
                        Size = new Size(220, 36),
                        Location = new Point(0, y)
                    };
                    updateButton.Click += async (_, _) => await BeginGitHubUpdateAsync(updateButton);
                    contentScroll.Controls.Add(updateButton);
                    y += 46;

                    break;
            }
        }

        var categories = new[] { "General", "Customization", "Advanced" };
        var categoryButtons = new List<FlatButton>();
        var sidebarY = 20;

        foreach (var category in categories)
        {
            var btn = new FlatButton
            {
                Text = category,
                Width = 160,
                Height = 36,
                Location = new Point(10, sidebarY),
                Primary = category == selectedCategory
            };
            btn.Click += (_, _) =>
            {
                selectedCategory = category;
                foreach (var b in categoryButtons)
                {
                    b.Primary = b.Text == category;
                    b.Invalidate();
                }
                UpdateContent(category);
            };
            sidebar.Controls.Add(btn);
            categoryButtons.Add(btn);
            sidebarY += 46;
        }

        if (!ShowMainPage(settingsPage))
        {
            settingsPage.Dispose();
            return;
        }

        UpdateContent(selectedCategory);

        var saveBtn = new FlatButton
        {
            Text = "Save",
            Primary = true,
            Size = new Size(100, 36),
            Location = new Point(settingsPage.ClientSize.Width - 124, 14),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        saveBtn.Click += (_, _) =>
        {
            Color parsedAccent;
            try
            {
                parsedAccent = ColorTranslator.FromHtml(accentColor);
                if (parsedAccent.IsEmpty)
                    throw new ArgumentException("Enter a valid hex color.");
            }
            catch
            {
                MessageBox.Show(settingsPage, "Enter a valid accent color, such as #FF9128.", "Invalid Color", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var previousAccent = Theme.Accent;
            var previousFontSize = Math.Clamp(settings.FontSize, 8f, 16f);
            settings.DefaultDealerId = defaultDealerId.Trim();
            settings.DefaultVehicleType = defaultVehicleType;
            settings.DefaultOutputFolder = defaultOutputFolder.Trim();
            settings.AccentColor = $"#{parsedAccent.R:X2}{parsedAccent.G:X2}{parsedAccent.B:X2}";
            settings.FontFamily = fontFamily;
            settings.FontSize = fontSize;
            settings.ThemeName = selectedTheme;
            settings.AutoSaveSettings = autoSaveSettings;
            settings.VerboseLogging = verboseLogging;
            settings.BackupOriginal = backupOriginal;
            SettingsManager.Save();
            settingsSaved = true;

            _dealerId.Text = settings.DefaultDealerId;
            _outputFolder.Text = settings.DefaultOutputFolder;
            _vehicleType.SelectedItem = _vehicleType.Items.Contains(settings.DefaultVehicleType) ? settings.DefaultVehicleType : "pickup";
            ApplySettingsAppearance(this, settings.FontSize / previousFontSize, previousAccent);
            Invalidate(true);
            CloseActivePage();
        };

        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 64,
            BackColor = Theme.Surface
        };
        footer.Controls.Add(saveBtn);
        settingsPage.Controls.Add(footer);
    }

    private static void ApplySettingsAppearance(Control control, float scale, Color previousAccent)
    {
        control.BackColor = Theme.RemapColor(control.BackColor, previousAccent);
        control.ForeColor = Theme.RemapColor(control.ForeColor, previousAccent);
        control.Font = new Font(SettingsManager.Current.FontFamily, Math.Max(1f, control.Font.Size * scale), control.Font.Style);

        foreach (Control child in control.Controls)
            ApplySettingsAppearance(child, scale, previousAccent);

        control.Invalidate();
    }

    private sealed class ChangelogGroup
    {
        public string Title { get; init; } = "";
        public List<string> Entries { get; } = new();
    }

    private sealed class ChangelogRelease
    {
        public string Title { get; init; } = "";
        public List<ChangelogGroup> Groups { get; } = new();
    }

    private static string LoadLocalChangelog()
    {
        using var stream = typeof(ConverterForm).Assembly.GetManifestResourceStream("CHANGELOG.md");
        if (stream == null)
            return "## Unreleased";

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static IReadOnlyList<ChangelogRelease> ParseChangelog(string markdown)
    {
        var releases = new List<ChangelogRelease>();
        ChangelogRelease? release = null;
        ChangelogGroup? group = null;

        foreach (var line in markdown.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                release = new ChangelogRelease { Title = line[3..].Trim() };
                releases.Add(release);
                group = null;
            }
            else if (line.StartsWith("### ", StringComparison.Ordinal) && release != null)
            {
                group = new ChangelogGroup { Title = line[4..].Trim() };
                release.Groups.Add(group);
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal) && group != null)
            {
                group.Entries.Add(line[2..].Trim());
            }
        }

        return releases;
    }

    private static void PopulateChangelog(FlowLayoutPanel list, string markdown)
    {
        var releases = ParseChangelog(markdown);
        var cardWidth = Math.Max(340, list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 12);

        foreach (var release in releases)
        {
            var card = new Card
            {
                Width = cardWidth,
                CornerRadius = 0,
                DrawBorder = false,
                Padding = new Padding(16),
                Margin = new Padding(0, 0, 0, 12),
                BackColor = Theme.Surface
            };
            var innerWidth = cardWidth - 32;
            var y = 8;
            var versionLabel = new Label
            {
                Text = release.Title,
                Font = Theme.UiFont(12f, FontStyle.Bold),
                ForeColor = Theme.Accent,
                BackColor = Theme.Surface,
                AutoSize = true,
                Location = new Point(0, y)
            };
            card.Controls.Add(versionLabel);
            y += 34;

            card.Controls.Add(new Panel
            {
                BackColor = Theme.Border,
                Location = new Point(0, y),
                Size = new Size(innerWidth, 1)
            });
            y += 14;

            if (release.Groups.Count == 0)
            {
                card.Controls.Add(new Label
                {
                    Text = "No changes recorded yet.",
                    Font = Theme.UiFont(9.5f),
                    ForeColor = Theme.Muted,
                    BackColor = Theme.Surface,
                    AutoSize = true,
                    Location = new Point(0, y)
                });
                y += 24;
            }

            foreach (var group in release.Groups)
            {
                var groupLabel = new Label
                {
                    Text = group.Title,
                    Font = Theme.UiFont(9.5f, FontStyle.Bold),
                    ForeColor = Theme.Text,
                    BackColor = Theme.Surface,
                    AutoSize = true,
                    Location = new Point(0, y)
                };
                card.Controls.Add(groupLabel);
                y += 24;

                foreach (var entry in group.Entries)
                {
                    var entryLabel = new Label
                    {
                        Text = $"•  {entry}",
                        Font = Theme.UiFont(9.25f),
                        ForeColor = Theme.Muted,
                        BackColor = Theme.Surface,
                        AutoSize = true,
                        MaximumSize = new Size(innerWidth, 0),
                        Location = new Point(0, y)
                    };
                    card.Controls.Add(entryLabel);
                    y += entryLabel.GetPreferredSize(new Size(innerWidth, 0)).Height + 7;
                }

                y += 10;
            }

            card.Height = Math.Max(80, y + 16);
            list.Controls.Add(card);
        }
    }

    private void ShowChangelog()
    {
        if (_activePage != null)
            return;

        var page = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Background,
            Padding = new Padding(Margin_, 20, Margin_, 12)
        };

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 78,
            BackColor = Theme.Background
        };
        var backButton = new FlatButton
        {
            Text = "Back",
            Size = new Size(76, 34),
            Location = new Point(0, 6)
        };
        backButton.Click += (_, _) => CloseActivePage();
        header.Controls.Add(backButton);
        header.Controls.Add(new Label
        {
            Text = "Changelog",
            Font = Theme.UiFont(17f, FontStyle.Bold),
            ForeColor = Theme.Text,
            AutoSize = true,
            Location = new Point(92, 2)
        });
        header.Controls.Add(new Label
        {
            Text = $"{Program.AppName}  |  Version {Program.AppVersion}",
            Font = Theme.UiFont(9.5f),
            ForeColor = Theme.Muted,
            AutoSize = true,
            Location = new Point(93, 34)
        });

        var releaseList = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Theme.Background,
            Padding = new Padding(0, 4, 8, 8)
        };
        page.Controls.Add(releaseList);
        page.Controls.Add(header);
        if (!ShowMainPage(page))
        {
            page.Dispose();
            return;
        }

        BeginInvoke(new Action(() =>
        {
            _ = LoadChangelogAsync(page, releaseList);
        }));
    }

    private static async Task LoadChangelogAsync(Panel page, FlowLayoutPanel releaseList)
    {
        var remoteChangelog = await Task.Run(GitHubReleaseClient.TryLoadChangelog);
        if (page.IsDisposed || releaseList.IsDisposed)
            return;

        PopulateChangelog(releaseList, remoteChangelog ?? LoadLocalChangelog());
    }

}
