using System.Reflection;

namespace ATSRoadTripConverter;

/// <summary>
/// The window shown before the converter. It replaces the old GitHub sign-in window, which
/// existed only to gate themes and now has nothing to do.
/// <para>
/// On a first run it shows a short quick start. On a later run it shows what changed since the
/// version the user last launched. The window is sized to its content, so the quick start never
/// scrolls, and the content sits directly on the form background rather than inside a sunken
/// panel that reads like a disabled textbox.
/// </para>
/// <para>
/// It cannot be dismissed by window chrome. Escape, Alt+F4 and the title-bar close are all
/// refused, because a launch page that vanishes on a stray click teaches people to ignore it.
/// Skip is the only way out.
/// </para>
/// </summary>
internal sealed class LaunchForm : Form
{
    private const int Pad = 30;
    private const int FormWidth = 620;
    private const int StepBadge = 22;
    private const int StepIndent = 34;
    private const int ButtonHeight = 36;
    private const int FooterGap = 24;

    /// <summary>
    /// Floor for the content area, so a near-empty page still leaves room for a line of body
    /// text between the header and the footer.
    /// </summary>
    private const int MinimumBodyHeight = 20;

    /// <summary>The quick-start steps, mirroring the README's "Typical use".</summary>
    private static readonly (string Title, string Body)[] Steps =
    {
        ("Pick your truck mod", "Choose the .scs or .zip on the input row. Browse, or use Recent to reopen the folder you used last time."),
        ("Choose where it goes", "Set an output folder and a dealer ID. The window shows the exact file that will be written."),
        ("Patch for Road Trip", "Click the button. Definitions-only mode writes <mod>_roadtrip_patch.scs and leaves your original untouched."),
        ("Load it above the original", "In ATS, add the patch above the untouched mod. That is the whole trick.")
    };

    /// <summary>Set once the user has committed via Skip, so the close is allowed through.</summary>
    private bool _dismissed;

    public LaunchForm(bool showQuickStart, IReadOnlyList<ReleaseSection> changes)
    {
        Text = Program.AppName;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.UiFont(9.5f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = false;
        DoubleBuffered = true;

        // No close button. The page is left with Skip, not with the title bar.
        ControlBox = false;

        var contentWidth = FormWidth - Pad * 2;
        var bodyFont = Theme.UiFont(8.75f);
        var stepFont = Theme.UiFont(10f, FontStyle.Bold);

        // The header is fixed-height, so the body starts at a known y.
        var bodyTop = Pad - 4 + 26 + 30;

        // Measure the body first, then size the window to it. Fixing a height up front and
        // hoping the content fits is what left the quick start permanently scrolled.
        var bodyHeight = showQuickStart
            ? MeasureQuickStart(contentWidth, bodyFont, stepFont)
            : MeasureReleaseNotes(changes, contentWidth, bodyFont);

        var needed = bodyTop + bodyHeight + FooterGap + ButtonHeight + Pad;

        // A short page (an empty "what's new", for instance) would otherwise produce a window
        // with no room for its own footer. This is the smallest height that still fits a
        // header, one line of body, and the Skip button without them colliding.
        var minimum = bodyTop + MinimumBodyHeight + FooterGap + ButtonHeight + Pad;

        // Never taller than the screen. The same rule the main window already follows.
        var working = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, FormWidth, needed);
        ClientSize = new Size(FormWidth, Math.Clamp(needed, minimum, Math.Max(minimum, working.Height)));

        Controls.Add(new Label
        {
            Text = Program.AppName,
            Font = Theme.UiFont(15f, FontStyle.Bold),
            ForeColor = Theme.Text,
            AutoSize = true,
            Location = new Point(Pad, Pad - 4)
        });

        Controls.Add(new Label
        {
            Text = showQuickStart
                ? $"Welcome. {Program.AppVersion} converts truck mods into Road Trip car mods."
                : $"What's new in {Program.AppVersion}",
            Font = Theme.UiFont(10f),
            ForeColor = Theme.Muted,
            AutoSize = true,
            Location = new Point(Pad, Pad - 4 + 26)
        });

        // Content sits straight on the form. A bordered Surface panel here looked like a
        // disabled text input, which is not what this is.
        var available = ClientSize.Height - bodyTop - FooterGap - ButtonHeight - Pad;
        var host = new Panel
        {
            // Clamped to the space that is genuinely free. Forcing a minimum height here
            // pushed the content down into the Skip button and clipped it.
            Bounds = new Rectangle(Pad, bodyTop, contentWidth, Math.Clamp(bodyHeight, 1, Math.Max(available, 1))),
            BackColor = Theme.Background,
            BorderStyle = BorderStyle.None,
            // Only engages if the content genuinely cannot fit the screen. Sized to the
            // content above, the quick start never reaches this.
            AutoScroll = bodyHeight > available
        };

        if (showQuickStart)
            BuildQuickStart(host, contentWidth, bodyFont, stepFont);
        else
            BuildReleaseNotes(host, contentWidth, changes, bodyFont);

        Controls.Add(host);

        var skip = new FlatButton
        {
            Text = showQuickStart ? "Skip" : "Close",
            Size = new Size(130, ButtonHeight),
            Location = new Point(FormWidth - Pad - 130, ClientSize.Height - Pad - ButtonHeight),
            Primary = true
        };
        skip.Click += (_, _) => Dismiss();
        Controls.Add(skip);
        skip.Select();

        // Escape and Alt+F4 both arrive here. Refusing them is deliberate.
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                Dismiss();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                System.Media.SystemSounds.Beep.Play();
                e.Handled = true;
            }
        };

        // Last line of defence. With ControlBox off the title bar offers no close button,
        // but Alt+F4 and a taskbar "close window" still reach this.
        FormClosing += (_, e) =>
        {
            if (_dismissed)
                return;
            e.Cancel = true;
            System.Media.SystemSounds.Beep.Play();
        };
    }

    private void Dismiss()
    {
        _dismissed = true;
        Close();
    }

    /// <summary>Height the quick start needs, measured from the real wrapped text.</summary>
    private static int MeasureQuickStart(int contentWidth, Font bodyFont, Font stepFont)
    {
        var width = contentWidth - StepIndent;
        var y = 0;

        foreach (var step in Steps)
            y += MeasureStep(step, width, bodyFont, stepFont);

        y += 20;
        return y + MeasureHeight("This tool is free. There is no account and no paid unlock.", contentWidth, bodyFont);
    }

    private void BuildQuickStart(Panel host, int contentWidth, Font bodyFont, Font stepFont)
    {
        var width = contentWidth - StepIndent;
        var y = 0;

        for (var index = 0; index < Steps.Length; index++)
        {
            var (title, body) = Steps[index];

            // A numbered badge per step, so the list reads as an ordered sequence rather
            // than four loose headings.
            host.Controls.Add(new StepBadge
            {
                Number = index + 1,
                Location = new Point(0, y + 1)
            });

            host.Controls.Add(new Label
            {
                Text = title,
                Font = stepFont,
                ForeColor = Theme.Text,
                AutoSize = true,
                Location = new Point(StepIndent, y)
            });
            y += 20;

            var bodyHeight = MeasureHeight(body, width, bodyFont);
            host.Controls.Add(new Label
            {
                Text = body,
                Font = bodyFont,
                ForeColor = Theme.Muted,
                AutoSize = false,
                Size = new Size(width, bodyHeight),
                Location = new Point(StepIndent, y)
            });
            y += bodyHeight + 12;
        }

        y += 8;
        host.Controls.Add(new Label
        {
            Text = "This tool is free. There is no account and no paid unlock.",
            Font = Theme.UiFont(8.5f, FontStyle.Bold),
            ForeColor = Theme.Accent,
            AutoSize = true,
            Location = new Point(0, y)
        });
    }

    private static int MeasureStep((string Title, string Body) step, int width, Font bodyFont, Font stepFont)
    {
        return 20 + MeasureHeight(step.Body, width, bodyFont) + 12;
    }

    /// <summary>Height the release notes need, measured from the real wrapped text.</summary>
    private static int MeasureReleaseNotes(IReadOnlyList<ReleaseSection> changes, int contentWidth, Font bodyFont)
    {
        if (changes.Count == 0)
            return MeasureHeight("No newer release notes were found. The full history is in the Changelog page.", contentWidth, bodyFont);

        var y = 0;
        foreach (var section in changes)
        {
            y += 24;
            foreach (var bullet in section.Bullets)
                y += MeasureHeight("•  " + bullet, contentWidth, bodyFont) + 6;
            y += 14;
        }

        return y;
    }

    private void BuildReleaseNotes(Panel host, int contentWidth, IReadOnlyList<ReleaseSection> changes, Font bodyFont)
    {
        if (changes.Count == 0)
        {
            host.Controls.Add(new Label
            {
                Text = "No newer release notes were found. The full history is in the Changelog page.",
                Font = bodyFont,
                ForeColor = Theme.Muted,
                AutoSize = false,
                Size = new Size(contentWidth, MeasureHeight("No newer release notes were found. The full history is in the Changelog page.", contentWidth, bodyFont)),
                Location = new Point(0, 0)
            });
            return;
        }

        var y = 0;

        foreach (var section in changes)
        {
            host.Controls.Add(new Label
            {
                Text = section.Heading,
                Font = Theme.UiFont(11f, FontStyle.Bold),
                ForeColor = Theme.Accent,
                AutoSize = true,
                Location = new Point(0, y)
            });
            y += 24;

            foreach (var bullet in section.Bullets)
            {
                var text = "•  " + bullet;
                var height = MeasureHeight(text, contentWidth, bodyFont);
                host.Controls.Add(new Label
                {
                    Text = text,
                    Font = bodyFont,
                    ForeColor = Theme.Text,
                    AutoSize = false,
                    Size = new Size(contentWidth, height),
                    Location = new Point(0, y)
                });
                y += height + 6;
            }

            y += 14;
        }
    }

    /// <summary>
    /// Height a string needs once wrapped to <paramref name="width"/>.
    /// <para>
    /// This measures the real text with TextRenderer instead of estimating from a character
    /// count. The estimate was what made wrapped step bodies overlap the following heading
    /// and what made the quick start overflow its panel.
    /// </para>
    /// </summary>
    private static int MeasureHeight(string text, int width, Font font)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var size = TextRenderer.MeasureText(
            text,
            font,
            new Size(Math.Max(1, width), int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        return size.Height;
    }

    /// <summary>Reads the embedded changelog, or null when the resource is missing.</summary>
    public static string? ReadEmbeddedChangelog()
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("CHANGELOG.md");
            if (stream == null)
                return null;

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch
        {
            // A missing changelog must never stop the app from starting.
            return null;
        }
    }
}

/// <summary>A numbered circle for a quick-start step.</summary>
internal sealed class StepBadge : Control
{
    public int Number { get; set; } = 1;

    public StepBadge()
    {
        Size = new Size(22, 22);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint, true);
        Font = Theme.UiFont(8.5f, FontStyle.Bold);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        // Fill the square with the form background so the circle leaves no corner artefact.
        using (var backdrop = new SolidBrush(Theme.Background))
            g.FillRectangle(backdrop, ClientRectangle);

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var fill = new SolidBrush(Theme.Accent))
            g.FillEllipse(fill, rect);

        TextRenderer.DrawText(g, Number.ToString(), Font, rect, Theme.OnAccent(Theme.Accent),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}
