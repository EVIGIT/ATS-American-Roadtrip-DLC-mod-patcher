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
/// <summary>
/// Wraps the sign-in token with Windows DPAPI (CryptProtectData) so it can only be
/// decrypted by this user account on this machine. P/Invoked straight from crypt32.dll
/// so the app keeps its zero-dependency footprint - the ProtectedData NuGet package is
/// not needed for a net8.0-windows target.
/// </summary>
// Supporter sign-in state.
//
// A GitHub sign-in unlocks the GitHub tier of themes today. The Ko-fi tier is already
// wired up everywhere except the check itself: when the Ko-fi page goes live, replace
// the body of HasKoFiAccess with a membership check and nothing else has to change.
/// <summary>
/// GitHub OAuth device flow, the only flow a desktop app can run without shipping a
/// client secret. The user gets a short code, approves it in a browser, and the app
/// polls until the token arrives.
/// </summary>

internal sealed class ThemeSwatch : Control
{
    private bool _selected;

    public ThemePalette Palette { get; }

    // Kept here so the settings layout can size the swatch grid (rows x columns)
    // from the same numbers the control actually uses.
    public static readonly Size SwatchSize = new(88, 50);
    public static readonly int SwatchMargin = 3;

    /// <summary>True while this palette is reserved and the visitor cannot use it yet.</summary>
    public bool IsLocked => Theme.IsLocked(Palette);

    /// <summary>Raised instead of <see cref="Control.Click"/> when a locked swatch is used.</summary>
    public event EventHandler? LockedClicked;

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
        Size = SwatchSize;
        Margin = new Padding(SwatchMargin);
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

    protected override void OnClick(EventArgs e)
    {
        if (IsLocked)
        {
            LockedClicked?.Invoke(this, EventArgs.Empty);
            return;
        }

        base.OnClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Background);

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var cardPath = Theme.Rounded(rect, 10);
        using (var surface = new SolidBrush(Palette.Surface))
            g.FillPath(surface, cardPath);

        var swatch = new Rectangle(7, 7, Width - 14, 14);
        using (var swatchPath = Theme.Rounded(swatch, 7))
        {
            g.SetClip(swatchPath);
            using var background = new SolidBrush(Palette.Background);
            using var swatchSurface = new SolidBrush(Palette.Surface);
            using var accent = new SolidBrush(Palette.Accent);
            var segmentWidth = swatch.Width / 3;
            g.FillRectangle(background, swatch.X, swatch.Y, segmentWidth + 1, swatch.Height);
            g.FillRectangle(swatchSurface, swatch.X + segmentWidth, swatch.Y, segmentWidth + 1, swatch.Height);
            g.FillRectangle(accent, swatch.X + segmentWidth * 2, swatch.Y, swatch.Width - segmentWidth * 2, swatch.Height);
            g.ResetClip();
        }

        TextRenderer.DrawText(g, Palette.Name, Font, new Rectangle(8, 25, Width - 16, 20), Palette.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        if (IsLocked)
            PaintLocked(g, cardPath);

        // A selected swatch keeps its own accent; a locked one is outlined in the colour
        // of the tier that unlocks it, gold for GitHub and pink for Ko-fi.
        var borderColor = _selected ? Palette.Accent : IsLocked ? Theme.LockColor(Palette.Access) : Theme.Border;
        using var border = new Pen(borderColor, _selected ? 2f : IsLocked ? 1.5f : 1f);
        g.DrawPath(border, cardPath);
    }

    // A reserved palette stays visible so people know it exists, but it is dimmed back
    // towards the page background and stamped with a padlock.
    private void PaintLocked(Graphics g, GraphicsPath cardPath)
    {
        using (var veil = new SolidBrush(Color.FromArgb(168, Theme.Background)))
            g.FillPath(veil, cardPath);

        var nameRect = new Rectangle(8, 25, Width - 30, 20);
        TextRenderer.DrawText(g, Palette.Name, Font, nameRect, Theme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        var lockColor = Theme.LockColor(Palette.Access);
        var lockLeft = Width - 19f;
        var lockTop = 31f;
        using (var shackle = new Pen(lockColor, 1.6f))
            g.DrawArc(shackle, lockLeft + 1.6f, lockTop - 4.5f, 6.8f, 6.8f, 180f, 180f);
        using (var body = new SolidBrush(lockColor))
            g.FillRectangle(body, lockLeft, lockTop, 10f, 8f);
    }
}

internal sealed class Card : Panel
{
    public int CornerRadius { get; set; } = 14;
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
    public int CornerRadius { get; set; } = 10;

    /// <summary>
    /// Paints the button in this colour regardless of the active theme. Used for
    /// brand-locked actions such as Ko-fi, which must stay Ko-fi pink.
    /// </summary>
    public Color? AccentOverride { get; set; }

    public FlatButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
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
        // Erase with the surrounding surface so the rounded corners never leave stale pixels.
        e.Graphics.Clear(Theme.HostColor(this));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        var radius = Math.Min(CornerRadius, Math.Min(rect.Width, rect.Height) / 2);
        using var path = Theme.Rounded(rect, radius);

        Color text;
        if (AccentOverride is { } accentOverride)
        {
            using (var fill = new SolidBrush(accentOverride))
                g.FillPath(fill, path);
            text = Theme.OnAccent(accentOverride);
        }
        else if (!Enabled)
        {
            using var fill = new SolidBrush(Theme.Field);
            g.FillPath(fill, path);
            text = Theme.Muted;
        }
        else if (Primary)
        {
            using (var fill = Theme.AccentGradient(ClientRectangle))
                g.FillPath(fill, path);
            text = Theme.OnAccent(Theme.Accent);
        }
        else
        {
            var fill = _pressed ? Theme.FieldPressed : _hover ? Theme.FieldHover : Theme.Field;
            using (var brush = new SolidBrush(fill))
                g.FillPath(brush, path);
            text = Theme.Text;

            using var ring = new Pen(Theme.Border);
            g.DrawPath(ring, path);
        }

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

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Match the host surface so rounded corners blend into the card / page behind them.
        e.Graphics.Clear(Theme.HostColor(this));
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
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        var radius = Math.Min(10, Math.Min(rect.Width, rect.Height) / 2);
        using var path = Theme.Rounded(rect, radius);

        using (var background = new SolidBrush(BackColor))
            g.FillPath(background, path);
        using (var border = new Pen(Theme.Border, 1f))
            g.DrawPath(border, path);

        var selectedText = SelectedItem?.ToString() ?? string.Empty;
        var textRect = new Rectangle(10, 0, Math.Max(0, Width - 36), Height);
        TextRenderer.DrawText(g, selectedText, Font, textRect, ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        var arrowRect = new Rectangle(Width - 19, (Height - 8) / 2, 9, 6);
        using (var triangleBrush = new SolidBrush(Theme.Muted))
        {
            g.SmoothingMode = SmoothingMode.None;
            g.FillPolygon(triangleBrush, new[]
            {
                new Point(arrowRect.Left, arrowRect.Top),
                new Point(arrowRect.Right, arrowRect.Top),
                new Point(arrowRect.Left + arrowRect.Width / 2, arrowRect.Bottom)
            });
        }
        g.SmoothingMode = SmoothingMode.AntiAlias;
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
        e.Graphics.Clear(Theme.HostColor(this));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.HostColor(this));

        var track = new Rectangle(0, 8, 44, 22);
        using var trackPath = Theme.Rounded(track, 11);
        using (var trackFill = new SolidBrush(_checked ? Theme.Accent : Theme.Field))
            g.FillPath(trackFill, trackPath);

        if (!_checked)
        {
            using var trackRing = new Pen(Theme.Border, 1f);
            g.DrawPath(trackRing, trackPath);
        }

        var knobSize = 16;
        var knobX = _checked ? track.Right - knobSize - 3 : track.X + 3;
        var knobRect = new Rectangle(knobX, track.Y + 3, knobSize, knobSize);
        using (var halo = new SolidBrush(Color.FromArgb(48, 0, 0, 0)))
            g.FillEllipse(halo, knobRect.X + 1, knobRect.Y + 1, knobRect.Width, knobRect.Height);
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
        Height = 8;
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

internal sealed class Badge : Control
{
    public Badge()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Background;
        Size = new Size(58, 24);
        Font = Theme.UiFont(8.5f, FontStyle.Bold);
        Margin = Padding.Empty;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var accent = Theme.Accent;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        var radius = Math.Min(12, Math.Min(rect.Width, rect.Height) / 2);
        using var path = Theme.Rounded(rect, radius);

        using (var baseFill = new SolidBrush(Theme.Background))
            g.FillPath(baseFill, path);
        using (var tint = new SolidBrush(Color.FromArgb(34, accent.R, accent.G, accent.B)))
            g.FillPath(tint, path);
        using (var border = new Pen(Color.FromArgb(100, accent.R, accent.G, accent.B), 1f))
            g.DrawPath(border, path);

        TextRenderer.DrawText(g, Text, Font, rect, accent,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

internal sealed class InfoDot : Control
{
    public InfoDot()
    {
        Size = new Size(30, 30);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint, true);
        Font = Theme.UiFont(11f, FontStyle.Bold);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        // Fill the square with whatever is behind the badge so the rounded corners of
        // the circle never show a stray control colour.
        using (var backdrop = new SolidBrush(Theme.HostColor(this)))
            g.FillRectangle(backdrop, ClientRectangle);
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var fill = new SolidBrush(Theme.Info))
            g.FillEllipse(fill, rect);
        TextRenderer.DrawText(g, "i", Font, rect, Theme.OnAccent(Theme.Info),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}
