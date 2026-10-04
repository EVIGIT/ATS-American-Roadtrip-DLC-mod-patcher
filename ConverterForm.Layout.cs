using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
namespace ATSRoadTripConverter;
public sealed partial class ConverterForm : Form
{
    // The trailing underscore is deliberate: plain "Margin" collides with the inherited
    // Form.Margin and will not compile (CS0108).
    //
    // The vertical budget lives in MainLayout so it can be asserted headlessly instead of being
    // re-derived by eye from this file. These aliases keep the existing call sites readable.
    private const int Margin_ = MainLayout.MainLayoutMargin;
    private const int ContentWidth = 812;

    /// <summary>
    /// Extra chrome above the log card that is not already counted in
    /// <see cref="MainLayout.ContentAboveLog"/>. Zero today; a tab strip will raise it, and the
    /// window budget has to rise by exactly the same amount or the log gets squeezed and the
    /// scrollbar comes back.
    /// </summary>
    private const int ExtraChromeAboveLog = 0;

    private Panel? _activePage;
    private Dictionary<Control, bool>? _mainViewPreviousVisibility;
    private Card? _logCard;
    private int _logCardTop;

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

    // Patch mode is OFF by default as of v1.3.9. It writes a 13-byte empty unit tree over the
    // original mod's truck_dealer entry, which overrides that definition with nothing and makes
    // ATS refuse to load any save that has driven the car ("invalid_vehicle"). Verified in game;
    // see the ROADMAP research note. It stays available because it is the only mode that leaves
    // the original mod untouched, and it is the right shape once the stub is fixed in v1.3.9.1.
    private readonly ToggleSwitch _patchOnly = new()
    {
        Text = "Definitions-only patch (known bug)",
        Description = "Leaves the original mod intact, but currently makes ATS refuse to load saves. Prefer full conversion.",
        Checked = false
    };

    private readonly ToggleSwitch _moveVehicleAssets = new()
    {
        Text = "Move vehicle assets",
        Description = "Moves vehicle/truck models into vehicle/car (full conversion). Always copied in patch mode.",
        Checked = true
    };

    private readonly ToggleSwitch _mapCameras = new()
    {
        Text = "Use Road Trip car cameras",
        Description = "Points behind/bumper/interior cameras at the built-in camera.*.car units.",
        Checked = true
    };

    private readonly ToggleSwitch _namespaceAnonymous = new()
    {
        Text = "Avoid clashes with other mods",
        Description = "Renames global _nameless units so two converted cars cannot replace each other.",
        Checked = true
    };

    private readonly ToggleSwitch _useSourceBrand = new()
    {
        Text = "Keep the mod's own brand",
        Description = "Uses the brand the mod already has, so its dealership logo keeps working.",
        Checked = true
    };

    private readonly ToggleSwitch _renameBrandLogo = new()
    {
        Text = "Add the logo to a renamed dealer",
        Description = "Only used when the dealer ID above renames the brand: copies the mod's logo to match.",
        Checked = true
    };

    private PictureBox? _logoBox;

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
        CornerRadius = 23,
        Font = Theme.UiFont(11f, FontStyle.Bold)
    };

    private readonly FlatButton _openOutput = new()
    {
        Text = "Open output folder",
        Enabled = false
    };

    private readonly FlatButton _clearLog = new() { Text = "Clear" };
    private readonly FlatButton _copyLog = new() { Text = "Copy log" };

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
        // Clamp to the working area so the window never opens taller than the screen. The height
        // tracks the measured options card, so a larger Font Size grows the window rather than
        // clipping the toggle text; the cards above the log need ~816px, which a 768px display
        // cannot show, and AutoScroll handles that overflow by scrolling instead of clipping.
        var workingArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1024, 768);
        var openingHeight = Math.Min(MainLayout.DefaultWindowHeightFor(MeasuredOptionsCardHeight()), workingArea.Height);
        ClientSize = new Size(ContentWidth + Margin_ * 2, openingHeight);
        // Small enough to still open on a 768px-tall display, which a 700px minimum could not.
        MinimumSize = new Size(ContentWidth + Margin_ * 2 + 16, Math.Min(560, workingArea.Height));
        AutoScroll = true;
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;
        AllowDrop = true;
        if (ThemedIcon.WindowIcon() is { } themedIcon)
            Icon = themedIcon;

        var y = BuildHeader();
        y = BuildFilesCard(y);
        y = BuildOptionsCard(y);
        y = BuildActionArea(y);
        BuildLogCard(y);
        ResizeMainContent();
        RestoreWindowLayout();

        _dealerId.Text = SettingsManager.Current.DefaultDealerId;
        _outputFolder.Text = SettingsManager.Current.DefaultOutputFolder;
        _useSourceBrand.Checked = SettingsManager.Current.KeepModBrand;
        _renameBrandLogo.Checked = SettingsManager.Current.CopyDealerLogo;
        _input.TextChanged += (_, _) => { SuggestDealerId(); UpdateOutputPreview(); };
        _outputFolder.TextChanged += (_, _) => UpdateOutputPreview();
        _patchOnly.CheckedChanged += (_, _) => { UpdateMoveAssetsState(); UpdateOutputPreview(); };
        _convert.Click += async (_, _) => await ConvertAsync();
        _openOutput.Click += (_, _) => OpenOutputFolder();
        _clearLog.Click += (_, _) => _log.Clear();
        _copyLog.Click += (_, _) => CopyLogToClipboard();
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;

        // Patch mode defaults to off in the field initializer; see the comment on _patchOnly.
        UpdateMoveAssetsState();
    UpdateOutputPreview();
        Write($"[INFO] {Program.AppName} {Program.AppVersion}");
        Write("[INFO] Drag an ATS mod (.scs/.zip) onto this window or click Browse to begin.");
        _ = MaybeCheckForUpdatesAsync();

        // Item 7: return to the page the user left off on, if that preference is enabled.
        // BeginInvoke so the window is on screen and laid out before the page appears over it.
        if (SettingsManager.Current.ReopenLastPage && SettingsManager.Current.LastPage != MainPage.Converter)
            BeginInvoke(new Action(RestoreLastPage));
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
        ApplyDarkTitleBar(this);
    }

    /// <summary>Re-tints the window and header artwork after the palette changes.</summary>
    private void ApplyThemedBranding()
    {
        if (ThemedIcon.WindowIcon() is { } icon)
            Icon = icon;
        if (_logoBox != null && ThemedIcon.Logo() is { } logo)
            _logoBox.Image = logo;
    }

    /// <summary>Restores the saved window position and size when the preference is on.</summary>
    private void RestoreWindowLayout()
    {
        var settings = SettingsManager.Current;
        if (!settings.RememberWindowLayout)
            return;

        var saved = IsRectOnScreen(settings.WindowLeft, settings.WindowTop, settings.WindowWidth, settings.WindowHeight);
        if (saved)
        {
            StartPosition = FormStartPosition.Manual;

            // A height saved on a larger display would otherwise reopen taller than this one
            // and reintroduce the clipped log card. Clamp to the area the rect actually sits on.
            var height = settings.WindowHeight;
            try
            {
                height = Math.Min(
                    height,
                    Screen.FromRectangle(new Rectangle(settings.WindowLeft, settings.WindowTop, settings.WindowWidth, settings.WindowHeight))
                        .WorkingArea.Height);
            }
            catch (InvalidOperationException)
            {
                // Keep the saved height; the scrollable form still keeps it reachable.
            }

            Bounds = new Rectangle(settings.WindowLeft, settings.WindowTop, settings.WindowWidth, height);
        }

        if (settings.WindowMaximized)
            WindowState = FormWindowState.Maximized;

        FormClosing += (_, _) => SaveWindowLayout();
    }

    private void SaveWindowLayout()
    {
        var settings = SettingsManager.Current;
        if (!settings.RememberWindowLayout)
            return;

        try
        {
            var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            settings.WindowMaximized = WindowState == FormWindowState.Maximized;
            settings.WindowLeft = bounds.Left;
            settings.WindowTop = bounds.Top;
            settings.WindowWidth = bounds.Width;
            settings.WindowHeight = bounds.Height;
            SettingsManager.Save();
        }
        catch
        {
            // A failed layout save must never stop the app from closing.
        }
    }

    /// <summary>Rejects a saved rectangle that would land off-screen after a monitor change.</summary>
    private static bool IsRectOnScreen(int left, int top, int width, int height)
    {
        if (width <= 0 || height <= 0 || left == -1 || top == -1)
            return false;

        try
        {
            var working = Screen.FromRectangle(new Rectangle(left, top, width, height)).WorkingArea;
            return working.IntersectsWith(new Rectangle(left, top, width, height));
        }
        catch
        {
            return false;
        }
    }

    internal static void ApplyDarkTitleBar(IWin32Window window)
    {
        try
        {
            var enabled = Theme.IsLightBackground ? 0 : 1;
            if (DwmSetWindowAttribute(window.Handle, 20, ref enabled, sizeof(int)) != 0)
                DwmSetWindowAttribute(window.Handle, 19, ref enabled, sizeof(int));
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    internal static Stream? LoadResource(string name) =>
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
        _logoBox = new PictureBox
        {
            SizeMode = PictureBoxSizeMode.Zoom,
            Location = new Point(Margin_, 22),
            Size = new Size(58, 58)
        };
        if (ThemedIcon.Logo() is { } logo)
            _logoBox.Image = logo;
        Controls.Add(_logoBox);

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

        var badge = new Badge
        {
            Text = Program.AppVersion,
            Size = new Size(58, 24),
            Location = new Point(Margin_ + ContentWidth - 58, 30),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        Controls.Add(badge);

        var settingsBtn = new FlatButton
        {
            Text = "Settings",
            Location = new Point(Margin_ + ContentWidth - 164, 30),
            Size = new Size(100, 24),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        settingsBtn.Click += (_, _) => ShowSettings();
        Controls.Add(settingsBtn);

        var changelogBtn = new FlatButton
        {
            Text = "Changelog",
            Location = new Point(Margin_ + ContentWidth - 272, 30),
            Size = new Size(100, 24),
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

        AddFieldRow(card, 46, "Input mod", _input, inner, () => BrowseFile(_input, "Choose ATS mod"), ShowRecentInputPicker);
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
        // Measure the rows before creating the card, because the card's height is derived from them.
        // A fixed row height is only correct at the default font size: Theme.UiFont scales the fonts
        // with the user's Font Size setting while control sizes stay fixed, so at 11 and above the
        // two-line descriptions no longer fit in 62px. TextRenderer ellipsises rather than throwing,
        // so this would otherwise be a silent "..." instead of a visible bug.
        var half = (ContentWidth - 36 - 16) / 2;
        using var probe = new Bitmap(1, 1);
        using var measure = Graphics.FromImage(probe);
        var rowHeight = MainLayout.RequiredToggleRowHeight(MeasureOptionsDescriptionHeight(measure, half));
        var card = AddCard(y, MainLayout.OptionsCardHeightFor(rowHeight), "OPTIONS");

        AddCaption(card, "Dealer ID", 18, 46);
        AddFieldBox(card, _dealerId, 18, 68, 130);
        AddCaption(card, "Vehicle type", 160, 46);
        _vehicleType.Items.Clear();
        _vehicleType.Items.AddRange(new object[] { "Sedan", "Hatchback", "Pickup", "Van" });
        _vehicleType.SelectedItem = VehicleTypeIdToDisplay(SettingsManager.Current.DefaultVehicleType);
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

        // The two branding toggles below fill the row that used to hold nothing, so the card does
        // not grow at the default font size. Rows are stacked at MainLayout.FirstToggleRowY with the
        // measured row height as the pitch, so a larger Font Size grows the card instead of clipping
        // the text; the form already scrolls when the content is taller than the screen.
        var rowY = MainLayout.FirstToggleRowY;
        PlaceToggle(card, _patchOnly, 18, rowY, ContentWidth - 36, rowHeight, stretch: true);
        rowY += rowHeight;
        PlaceTogglePair(card, _translateDealer, _moveVehicleAssets, rowY, rowHeight);
        rowY += rowHeight;
        PlaceTogglePair(card, _mapCameras, _namespaceAnonymous, rowY, rowHeight);
        rowY += rowHeight;
        PlaceTogglePair(card, _useSourceBrand, _renameBrandLogo, rowY, rowHeight);

        return y + card.Height + 16;
    }

    /// <summary>
    /// The options card height the current font size needs, measured from the real toggle text.
    /// <para>
    /// Used for the window's opening height, which has to be decided before <c>BuildOptionsCard</c>
    /// runs. It measures the same six paired toggles against the same width, so both callers agree
    /// on one number rather than each re-deriving it.
    /// </para>
    /// </summary>
    private int MeasuredOptionsCardHeight()
    {
        var half = (ContentWidth - 36 - 16) / 2;
        using var probe = new Bitmap(1, 1);
        using var measure = Graphics.FromImage(probe);
        var rowHeight = MainLayout.RequiredToggleRowHeight(MeasureOptionsDescriptionHeight(measure, half));
        return MainLayout.OptionsCardHeightFor(rowHeight);
    }

    /// <summary>
    /// The tallest description among the six options toggles, measured at the real paired width.
    /// <para>
    /// The full-width patch switch is excluded on purpose: it gets the whole card width, so it wraps
    /// to at most two lines regardless of font size, whereas the paired rows are the ones that grow.
    /// Including it would inflate every row to the patch switch's height and undo the packing that
    /// keeps the window at 1080px.
    /// </para>
    /// </summary>
    private int MeasureOptionsDescriptionHeight(Graphics graphics, int pairedWidth)
    {
        using var small = Theme.UiFont(8.9f);
        const TextFormatFlags flags =
            TextFormatFlags.Left | TextFormatFlags.WordBreak |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;

        var textWidth = Math.Max(1, pairedWidth - 58);
        var probe = new Size(textWidth, 1000);

        var worst = 0;
        foreach (var toggle in new[]
                 {
                     _translateDealer, _moveVehicleAssets,
                     _mapCameras, _namespaceAnonymous,
                     _useSourceBrand, _renameBrandLogo,
                 })
        {
            var title = TextRenderer.MeasureText(graphics, toggle.Text, toggle.Font, probe, flags);
            var description = TextRenderer.MeasureText(graphics, toggle.Description, small, probe, flags);
            worst = Math.Max(worst, Math.Max(title.Height, description.Height));
        }

        return worst;
    }

    private int BuildActionArea(int y)
    {
        _convert.Location = new Point(Margin_, y);
        _convert.Size = new Size(ContentWidth, 46);
        _convert.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        Controls.Add(_convert);

        _progress.Location = new Point(Margin_, y + 60);
        _progress.Size = new Size(ContentWidth - 200, 8);
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
        _logCardTop = y;
        _logCard = AddCard(y, MainLayout.MinimumLogCardHeight, "LOG");
        // Deliberately not anchored to the bottom: with AutoScroll on the form, a
        // bottom-anchored control fights the scroll extent. ResizeMainContent owns the height.
        _logCard.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        // Button row is laid out right-to-left from the card's inner edge. It used to be three
        // separate hard-coded expressions, and "Open output folder" was positioned as if the
        // "Copy log" button did not exist - so Copy log (624..714) sat entirely inside Open output
        // folder (546..716) and was invisible, added first and therefore painted over.
        const int cardPad = 18;
        const int clearWidth = 70;
        const int copyWidth = 90;
        const int openWidth = 170;
        const int gapCopyToClear = 10;
        const int gapOpenToCopy = 8;
        var rowRight = ContentWidth - cardPad;

        var clearX = rowRight - clearWidth;
        var copyX = clearX - gapCopyToClear - copyWidth;
        var openX = copyX - gapOpenToCopy - openWidth;

        _openOutput.Location = new Point(openX, 10);
        _openOutput.Size = new Size(openWidth, 28);
        _openOutput.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        var card = _logCard;
        card.Controls.Add(_openOutput);

        _clearLog.Location = new Point(clearX, 10);
        _clearLog.Size = new Size(clearWidth, 28);
        _clearLog.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        card.Controls.Add(_clearLog);

        _copyLog.Location = new Point(copyX, 10);
        _copyLog.Size = new Size(copyWidth, 28);
        _copyLog.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        card.Controls.Add(_copyLog);

        _log.Location = new Point(18, 48);
        _log.Size = new Size(ContentWidth - 36, MainLayout.MinimumLogCardHeight - 66);
        _log.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        card.Controls.Add(_log);
    }

    /// <summary>
    /// Gives the log card whatever height is left over, but never less than
    /// <see cref="MainLayout.MinimumLogCardHeight"/>. On a short screen the leftover goes negative,
    /// the log holds its minimum, and the form scrolls instead of clipping the card off the bottom.
    /// </summary>
    private void ResizeMainContent()
    {
        if (_logCard == null || _logCard.IsDisposed)
            return;

        _logCard.Height = MainLayout.LogCardHeight(ClientSize.Height, _logCardTop, ExtraChromeAboveLog);
        _log.Height = Math.Max(MainLayout.MinimumLogCardHeight - 66, 40);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ResizeMainContent();
    }

    private Card AddCard(int y, int height, string title)
    {
        var card = new Card
        {
            Location = new Point(Margin_, y),
            Size = new Size(ContentWidth, height),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        card.Controls.Add(new Panel
        {
            BackColor = Theme.Accent,
            Location = new Point(18, 17),
            Size = new Size(3, 12)
        });
        card.Controls.Add(new Label
        {
            Text = title,
            Font = Theme.UiFont(8.5f, FontStyle.Bold),
            ForeColor = Theme.Accent,
            BackColor = Theme.Surface,
            AutoSize = true,
            Location = new Point(27, 16)
        });
        Controls.Add(card);
        return card;
    }

    private static void AddCaption(Control parent, string text, int x, int y)
    {
        parent.Controls.Add(new Label
        {
            Text = text.ToUpperInvariant(),
            Font = Theme.UiFont(8.5f, FontStyle.Bold),
            ForeColor = Theme.Muted,
            BackColor = Theme.Surface,
            AutoSize = true,
            Location = new Point(x, y)
        });
    }

    private static Panel AddFieldBox(Control parent, TextBox box, int x, int y, int width)
    {
        var frame = new Panel
        {
            BackColor = Theme.Surface,
            Location = new Point(x, y),
            Size = new Size(width, 34),
            Padding = new Padding(10, 8, 10, 8)
        };
        frame.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new Rectangle(0, 0, frame.Width - 1, frame.Height - 1);
            using var path = Theme.Rounded(bounds, 8);
            using (var fill = new SolidBrush(Theme.Field))
                e.Graphics.FillPath(fill, path);
            using var pen = new Pen(box.Focused ? Theme.Accent : Theme.Border, box.Focused ? 1.5f : 1f);
            e.Graphics.DrawPath(pen, path);
        };
        box.GotFocus += (_, _) => frame.Invalidate();
        box.LostFocus += (_, _) => frame.Invalidate();
        box.Dock = DockStyle.Fill;
        frame.Controls.Add(box);
        parent.Controls.Add(frame);
        return frame;
    }

    private const int BrowseButtonWidth = 90;
    private const int RecentButtonWidth = 82;
    private const int FieldButtonGap = 6;

    private void AddFieldRow(Control parent, int y, string caption, TextBox box, int innerWidth, Action browse)
    {
        AddFieldRow(parent, y, caption, box, innerWidth, browse, recent: null);
    }

    private void AddFieldRow(Control parent, int y, string caption, TextBox box, int innerWidth, Action browse, Action? recent)
    {
        AddCaption(parent, caption, 18, y);
        var buttonsWidth = BrowseButtonWidth + (recent == null ? 0 : RecentButtonWidth + FieldButtonGap);
        var frame = AddFieldBox(parent, box, 18, y + 22, innerWidth - 16 - buttonsWidth);
        frame.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        var browseX = 18 + innerWidth - BrowseButtonWidth;
        var browseButton = AddButton(parent, "Browse", browseX, y + 22, BrowseButtonWidth, browse);
        browseButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        if (recent == null)
            return;

        var recentButton = AddButton(parent, "Recent", browseX - RecentButtonWidth - FieldButtonGap, y + 22, RecentButtonWidth, recent);
        recentButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
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

    /// <param name="stretch">
    /// True for a toggle that owns its whole row, so it may grow with the card. False keeps the
    /// control at its designed width, which is required whenever a row holds more than one
    /// control and they have to keep a gap between them.
    /// </param>
    private static void PlaceToggle(Control parent, ToggleSwitch toggle, int x, int y, int width, int height, bool stretch)
    {
        toggle.Location = new Point(x, y);
        toggle.Size = new Size(width, height);
        toggle.BackColor = Theme.Surface;
        // A left+right anchor stretches the control from its left edge, which walks it straight
        // into its neighbour on any row that holds two toggles.
        toggle.Anchor = stretch
            ? AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            : AnchorStyles.Top | AnchorStyles.Left;
        parent.Controls.Add(toggle);
    }

    /// <summary>
    /// Lays out two toggles side by side, keeping them flush against the card edges.
    /// <para>
    /// The left one is pinned to the left and the right one pinned to the right, both at a fixed
    /// width. Anchoring both left+right instead is what caused a real bug: widening the window
    /// grew the left toggle rightwards while the right toggle stayed put, so once the window grew
    /// by more than the 16px gap the two overlapped and the higher one painted over the other,
    /// making a switch appear to vanish behind the UI.
    /// </para>
    /// </summary>
    private static void PlaceTogglePair(Control parent, ToggleSwitch left, ToggleSwitch right, int y, int height)
    {
        foreach (var toggle in new[] { left, right })
        {
            toggle.BackColor = Theme.Surface;
            toggle.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            parent.Controls.Add(toggle);
        }

        // Re-run on every card resize so the pair stays split down the middle instead of
        // drifting apart as the window widens.
        void Layout()
        {
            var inner = parent.ClientSize.Width - 36;
            var half = Math.Max(160, (inner - 16) / 2);

            left.Size = new Size(half, height);
            right.Size = new Size(half, height);
            left.Location = new Point(18, y);
            right.Location = new Point(18 + half + 16, y);
        }

        parent.Resize += (_, _) => Layout();
        Layout();
    }

    // Track C item 1. The remembered folder is the entry point rather than the exact
    // file: mod folders hold many archives, and the previous file is frequently
    // renamed or replaced by a re-download. If the exact path still exists it is
    // preselected; otherwise the list opens with everything in that folder.
    private void ShowRecentInputPicker()
    {
        var lastFolder = Path.GetDirectoryName(SettingsManager.Current.LastInputPath);
        if (string.IsNullOrWhiteSpace(lastFolder) || !Directory.Exists(lastFolder))
        {
            Write("[INFO] No previous mod folder to reopen yet.");
            return;
        }

        var archives = new List<string>(Directory.EnumerateFiles(lastFolder, "*.scs"));
        archives.AddRange(Directory.EnumerateFiles(lastFolder, "*.zip"));
        archives.Sort(StringComparer.OrdinalIgnoreCase);

        if (archives.Count == 0)
        {
            Write($"[INFO] No .scs or .zip archives in {lastFolder}.");
            using var empty = new FolderBrowserDialog { SelectedPath = lastFolder };
            if (empty.ShowDialog(this) == DialogResult.OK)
                _outputFolder.Text = empty.SelectedPath;
            return;
        }

        var items = new string[archives.Count];
        for (var i = 0; i < archives.Count; i++)
            items[i] = Path.GetFileName(archives[i]);

        using var picker = new Form
        {
            Text = "Recent mod",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ClientSize = new Size(520, 340)
        };

        var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        list.Items.AddRange(items);

        // Preselect the exact file used last time when it is still there.
        var previousFile = Path.GetFileName(SettingsManager.Current.LastInputPath);
        var previousIndex = previousFile == null ? -1 : Array.FindIndex(items, name => name.Equals(previousFile, StringComparison.OrdinalIgnoreCase));
        list.SelectedIndex = previousIndex >= 0 ? previousIndex : 0;
        list.DoubleClick += (_, _) => picker.DialogResult = DialogResult.OK;
        picker.Controls.Add(list);

        var folderButton = new Button
        {
            Text = "Open folder...",
            DialogResult = DialogResult.Cancel,
            Dock = DockStyle.Bottom,
            Height = 32
        };
        picker.Controls.Add(folderButton);

        using (picker)
        {
            if (picker.ShowDialog(this) != DialogResult.OK)
            {
                if (picker.DialogResult == DialogResult.Cancel)
                    BrowseFolder(_outputFolder, "Choose output folder");
                return;
            }

            var chosen = Path.Combine(lastFolder, list.SelectedItem?.ToString() ?? "");
            if (File.Exists(chosen))
                _input.Text = chosen;
        }
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

    private static string VehicleTypeIdToDisplay(string? value) =>
        (value ?? "pickup").Trim().ToLowerInvariant() switch
        {
            "sedan" => "Sedan",
            "hatchback" => "Hatchback",
            "van" => "Van",
            _ => "Pickup"
        };

    private static string VehicleTypeDisplayToId(string? value) =>
        (value ?? "pickup").Trim().ToLowerInvariant() switch
        {
            "sedan" => "sedan",
            "hatchback" => "hatchback",
            "van" => "van",
            _ => "pickup"
        };

    private static bool IsDetailLogLine(string line) =>
        line.StartsWith("[EDITED]", StringComparison.Ordinal) ||
        line.StartsWith("[DEALER]", StringComparison.Ordinal) ||
        line.StartsWith("[ASSET]", StringComparison.Ordinal) ||
        line.StartsWith("[PATCH]", StringComparison.Ordinal) ||
        line.StartsWith("[COLLISION]", StringComparison.Ordinal) ||
        line.StartsWith("[REFERENCE]", StringComparison.Ordinal) ||
        line.StartsWith("[ARCHIVE]", StringComparison.Ordinal);

    private void BackupOriginalMod(Action<string> log)
    {
        try
        {
            var backupPath = _input.Text + ".bak";
            if (File.Exists(backupPath))
                return;

            File.Copy(_input.Text, backupPath);
            log($"[INFO] Backed up the original mod to {backupPath}");
        }
        catch (Exception ex)
        {
            log($"[WARNING] Could not back up the original mod: {ex.Message}");
        }
    }

    private void UpdateMoveAssetsState()
    {
        _moveVehicleAssets.Enabled = !_patchOnly.Checked;
        _moveVehicleAssets.Description = _patchOnly.Checked
            ? "Always copied in patch mode to vehicle/car."
            : "Moves vehicle/truck models into vehicle/car. Leave on so textures and models come with the car.";
        _moveVehicleAssets.Invalidate();
        _patchOnly.Invalidate();
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

}
