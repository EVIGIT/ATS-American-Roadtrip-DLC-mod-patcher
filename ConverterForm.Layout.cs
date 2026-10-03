using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace ATSRoadTripConverter;
public sealed partial class ConverterForm : Form
{
    // The trailing underscore is deliberate: plain "Margin" collides with the inherited
    // Form.Margin and will not compile (CS0108).
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

    private readonly ToggleSwitch _mapCameras = new()
    {
        Text = "Use Road Trip car cameras",
        Description = "Points behind/bumper/interior cameras at the built-in camera.*.car units.",
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
        ClientSize = new Size(ContentWidth + Margin_ * 2, 900);
        MinimumSize = new Size(ContentWidth + Margin_ * 2 + 16, 700);
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
        RestoreWindowLayout();

        _dealerId.Text = SettingsManager.Current.DefaultDealerId;
        _outputFolder.Text = SettingsManager.Current.DefaultOutputFolder;
        _input.TextChanged += (_, _) => { SuggestDealerId(); UpdateOutputPreview(); };
        _outputFolder.TextChanged += (_, _) => UpdateOutputPreview();
        _patchOnly.CheckedChanged += (_, _) => { UpdateMoveAssetsState(); UpdateOutputPreview(); };
        _convert.Click += async (_, _) => await ConvertAsync();
        _openOutput.Click += (_, _) => OpenOutputFolder();
        _clearLog.Click += (_, _) => _log.Clear();
        _copyLog.Click += (_, _) => CopyLogToClipboard();
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;

        _patchOnly.Checked = true;
        UpdateMoveAssetsState();
        UpdateOutputPreview();
        Write($"[INFO] {Program.AppName} {Program.AppVersion}");
        Write("[INFO] Drag an ATS mod (.scs/.zip) onto this window or click Browse to begin.");
        _ = MaybeCheckForUpdatesAsync();
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
            Bounds = new Rectangle(settings.WindowLeft, settings.WindowTop, settings.WindowWidth, settings.WindowHeight);
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
        var card = AddCard(y, 352, "OPTIONS");
        var half = (ContentWidth - 36 - 16) / 2;

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

        PlaceToggle(card, _patchOnly, 18, 156, ContentWidth - 36);
        PlaceToggle(card, _translateDealer, 18, 218, half);
        PlaceToggle(card, _moveVehicleAssets, 18 + half + 16, 218, half);
        PlaceToggle(card, _mapCameras, 18, 280, ContentWidth - 36);

        return y + 352 + 16;
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

        _copyLog.Location = new Point(ContentWidth - 18 - 70 - 10 - 90, 10);
        _copyLog.Size = new Size(90, 28);
        _copyLog.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        card.Controls.Add(_copyLog);

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

    private static void PlaceToggle(Control parent, ToggleSwitch toggle, int x, int y, int width)
    {
        toggle.Location = new Point(x, y);
        toggle.Size = new Size(width, 62);
        toggle.BackColor = Theme.Surface;
        toggle.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        parent.Controls.Add(toggle);
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

}
