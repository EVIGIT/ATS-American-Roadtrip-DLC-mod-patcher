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

        if (SettingsManager.Current.ValidateInputBeforeConverting && !ValidateInputMod(_input.Text))
            return;

        _convert.Enabled = false;
        _convert.Text = "PATCHING...";
        _progress.Value = 0;
        SetStatus("Patching...", Theme.Info);

        var verboseLogging = SettingsManager.Current.VerboseLogging;
        Action<string> logSink = verboseLogging
            ? Write
            : line =>
            {
                if (!IsDetailLogLine(line))
                    Write(line);
            };

        try
        {
            if (SettingsManager.Current.BackupOriginal)
                BackupOriginalMod(logSink);

            var settings = new ConversionSettings(
                _input.Text,
                _outputFolder.Text,
                _dealerId.Text.Trim(),
                _reference.Text.Trim(),
                _moveVehicleAssets.Checked,
                _translateDealer.Checked,
                _patchOnly.Checked,
                VehicleTypeDisplayToId(_vehicleType.SelectedItem as string),
                _mapCameras.Checked);

            var result = await Task.Run(() =>
                ModConverter.Run(
                    settings,
                    logSink,
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

            if (SettingsManager.Current.OpenOutputFolderAfterConversion && File.Exists(result.OutputFile))
                OpenOutputFolder();

            if (SettingsManager.Current.AutoSaveSettings)
            {
                SettingsManager.Current.DefaultDealerId = _dealerId.Text.Trim();
                SettingsManager.Current.DefaultOutputFolder = _outputFolder.Text.Trim();
                SettingsManager.Current.DefaultVehicleType = VehicleTypeDisplayToId(_vehicleType.SelectedItem as string);
                SettingsManager.Current.LastInputPath = _input.Text.Trim();
                SettingsManager.Current.LastWorkFolder = _outputFolder.Text.Trim();
                SettingsManager.Save();
            }
        }
        catch (Exception ex)
        {
            Write($"[FATAL] {ex.Message}");
            Write("[FATAL] No converted file was produced.");

            // Track C item 2: a failed run leaves the output folder holding partial
            // work. That is usually what someone wants when diagnosing, so say where
            // it is instead of leaving them to guess. Remembers the folder for the
            // next run regardless of AutoSaveSettings, which is a UI preference and
            // not a statement about whether this path is still worth keeping.
            if (Directory.Exists(_outputFolder.Text))
            {
                Write($"[INFO] Partial work folder kept for inspection: {_outputFolder.Text}");
                if (!string.Equals(SettingsManager.Current.LastWorkFolder, _outputFolder.Text.Trim(), StringComparison.Ordinal))
                {
                    SettingsManager.Current.LastWorkFolder = _outputFolder.Text.Trim();
                    SettingsManager.Save();
                }
            }

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

        TrimLogToLimit();

        _log.SelectionStart = _log.TextLength;
        _log.SelectionLength = 0;
        _log.SelectionColor = ColorFor(line);
        _log.AppendText(line + Environment.NewLine);
        _log.SelectionColor = _log.ForeColor;
        _log.ScrollToCaret();
    }

    /// <summary>
    /// Keeps the log bounded so a long, verbose conversion cannot grow the control
    /// without limit. The oldest lines are dropped first.
    /// </summary>
    private void TrimLogToLimit()
    {
        var limit = Math.Clamp(SettingsManager.Current.MaxLogLines, 200, 100_000);
        if (_log.Lines.Length <= limit)
            return;

        var text = string.Join(Environment.NewLine, _log.Lines.Skip(_log.Lines.Length - limit));
        _log.Lines = text.Split(Environment.NewLine);
    }

    private void CopyLogToClipboard()
    {
        try
        {
            if (_log.TextLength == 0)
            {
                Write("[INFO] There is no log to copy yet.");
                return;
            }

            Clipboard.SetText(_log.Text);
            Write("[INFO] Conversion log copied to the clipboard.");
        }
        catch (Exception ex)
        {
            Write($"[WARNING] Could not copy the log: {ex.Message}");
        }
    }

    /// <summary>
    /// Quietly records that an update check ran, at most once a day, when the
    /// preference is enabled. Never blocks or fails the app.
    /// </summary>
    private async Task MaybeCheckForUpdatesAsync()
    {
        var settings = SettingsManager.Current;
        if (!settings.CheckForUpdatesAutomatically)
            return;

        try
        {
            if (DateTime.TryParse(settings.LastUpdateCheckUtc, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var last)
                && (DateTime.UtcNow - last).TotalHours < 24)
            {
                return;
            }

            var tag = await Task.Run(() => GitHubReleaseClient.TryGetLatestReleaseTag());
            settings.LastUpdateCheckUtc = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
            SettingsManager.Save();

            if (string.IsNullOrWhiteSpace(tag))
                return;

            if (tag.Trim().TrimStart('v') == Program.AppVersion.Trim().TrimStart('v'))
                return;

            Write($"[INFO] A newer release is available: {tag}. Use Settings > Advanced to install it.");
        }
        catch
        {
            // Update checks are best effort and must never interrupt startup.
        }
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

    /// <summary>
    /// Cheap pre-flight check so a bad input is reported before a conversion starts.
    /// Read-only: nothing is written and the archive is never modified.
    /// </summary>
    private bool ValidateInputMod(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Length == 0)
            {
                Fail("[ERROR] The selected input file is empty (0 bytes).");
                return false;
            }

            Write($"[INFO] Checking {info.Name} ({info.Length / 1024} KB) before converting...");

            using var archive = System.IO.Compression.ZipFile.OpenRead(path);
            var entries = archive.Entries
                .Where(entry => !string.IsNullOrEmpty(entry.Name))
                .ToArray();

            if (entries.Length == 0)
            {
                Fail("[ERROR] The selected input archive is empty.");
                return false;
            }

            var hasVehicleDefinitions = entries.Any(entry =>
                entry.FullName.Replace('\\', '/').Contains("def/vehicle", StringComparison.OrdinalIgnoreCase));

            if (!hasVehicleDefinitions)
            {
                Fail("[ERROR] No def/vehicle entries were found in the selected input. " +
                     "This does not look like an ATS mod, so it cannot be converted.");
                return false;
            }

            var encrypted = entries
                .Where(entry => !entry.FullName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                .Where(entry => EncryptedModScanner.EntryLooksEncrypted(entry))
                .Select(entry => entry.FullName)
                .ToArray();

            if (encrypted.Length > 0)
            {
                Write($"[WARNING] {encrypted.Length} file(s) inside the archive are encrypted and cannot be converted.");
                Write("          Encrypted files (first few): " + string.Join(", ", encrypted.Take(3)));
            }

            Write($"[INFO] Input looks valid: {entries.Length} entr{(entries.Length == 1 ? "y" : "ies")}.");
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or System.IO.IOException or UnauthorizedAccessException)
        {
            // A password-protected archive lands here; report it plainly.
            Fail("[ERROR] The selected input could not be opened: " + ex.Message +
                 (ex is InvalidDataException ? " It may be password protected or corrupted." : string.Empty));
            return false;
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

        var updater = Process.Start(startInfo);
        if (updater == null)
            throw new InvalidOperationException("Could not start the updater process.");

        // Do not close the app until the helper has confirmed it is alive. Closing
        // straight away is what made updates "close the app and do nothing".
        if (!WaitForUpdaterReady(plan, updater, TimeSpan.FromSeconds(20)))
        {
            try
            {
                if (!updater.HasExited)
                    updater.Kill(true);
            }
            catch
            {
            }

            throw new InvalidOperationException(
                "The updater helper did not start, so nothing was changed. " +
                "Check that Windows PowerShell is available on this machine.");
        }

        SettingsManager.Save();
        Close();
    }

    /// <summary>
    /// Waits for the helper to write its ready marker. Returns false if it exited early
    /// or never reported in, so the app can stay open and explain what went wrong.
    /// </summary>
    private static bool WaitForUpdaterReady(LocalUpdatePlan plan, Process updater, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (File.Exists(plan.ReadyPath))
                    return true;
            }
            catch
            {
                return false;
            }

            try
            {
                if (updater.HasExited)
                    return false;
            }
            catch
            {
                return false;
            }

            Thread.Sleep(150);
        }

        return false;
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
        var rememberWindowLayout = settings.RememberWindowLayout;
        var validateInputBeforeConverting = settings.ValidateInputBeforeConverting;
        var checkForUpdatesAutomatically = settings.CheckForUpdatesAutomatically;
        var openOutputFolderAfterConversion = settings.OpenOutputFolderAfterConversion;
        var maxLogLines = settings.MaxLogLines;

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

            // Checkbox option row, matching the style used on the Advanced tab.
            void AddOption(string caption, bool isChecked, Action<bool> onChanged)
            {
                var box = new CheckBox
                {
                    Text = caption,
                    Checked = isChecked,
                    ForeColor = Theme.Text,
                    BackColor = Color.Transparent,
                    AutoSize = true,
                    Location = new Point(0, y)
                };
                box.CheckedChanged += (_, _) => onChanged(box.Checked);
                contentScroll.Controls.Add(box);
                y += 42;
            }

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

            // Re-opens the sign-in window from inside Settings and repaints everything that
        // depends on the account state.
        void PromptSignIn()
        {
            using var signIn = new SignInForm();
            signIn.ShowDialog(settingsPage);
            ApplyAccountChange();
        }

        // Signing in or out can unlock/relock themes, so the staged theme and accent are
        // resynced with the stored settings and the page is rebuilt.
        void ApplyAccountChange()
        {
            var previousAccent = Theme.Accent;
            var staged = Theme.Palettes.FirstOrDefault(palette => palette.Name.Equals(selectedTheme, StringComparison.OrdinalIgnoreCase));
            if (staged != null && Theme.IsLocked(staged))
            {
                selectedTheme = Theme.Palettes[0].Name;
                settings.ThemeName = selectedTheme;
                var presetAccent = Theme.PresetAccent(selectedTheme);
                accentColor = $"#{presetAccent.R:X2}{presetAccent.G:X2}{presetAccent.B:X2}";
                settings.AccentColor = accentColor;
                if (accentInput != null)
                    accentInput.Text = accentColor;
            }

            ApplySettingsAppearance(this, 1f, previousAccent);
            ApplySettingsAppearance(settingsPage, 1f, previousAccent);
            TryEnableDarkTitleBar();
            ApplyThemedBranding();
            UpdateContent(selectedCategory);
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
                    vehicleTypeInput.SelectedItem = VehicleTypeIdToDisplay(defaultVehicleType);
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

                    contentScroll.Controls.Add(new Label
                    {
                        Text = "Window and updates",
                        Font = Theme.UiFont(10f, FontStyle.Bold),
                        ForeColor = Theme.Text,
                        AutoSize = true,
                        Location = new Point(0, y)
                    });
                    y += 28;

                    AddOption("Remember window size and position", rememberWindowLayout, value => rememberWindowLayout = value);
                    AddOption("Check the selected mod before converting", validateInputBeforeConverting, value => validateInputBeforeConverting = value);
                    AddOption("Check for updates automatically", checkForUpdatesAutomatically, value => checkForUpdatesAutomatically = value);

                    contentScroll.Controls.Add(new Label
                    {
                        Text = DescribeLastUpdateCheck(settings.LastUpdateCheckUtc),
                        Font = Theme.UiFont(8.75f),
                        ForeColor = Theme.Muted,
                        AutoSize = true,
                        Location = new Point(0, y)
                    });
                    y += 40;
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
                        Text = AuthSession.HasGitHubAccess && AuthSession.HasKoFiAccess
                            ? "Choose a coordinated palette for the app. Every theme is unlocked."
                            : $"Free: {Theme.ThemeNamesFor(ThemeAccess.Free)}. Gold outline: GitHub sign-in. Pink outline: Ko-fi.",
                        Font = Theme.UiFont(8.75f),
                        ForeColor = Theme.Muted,
                        // Capped at the content width so a long line can never widen the
                        // scrollable area and add a horizontal scrollbar to the page.
                        AutoSize = false,
                        Size = new Size(fieldWidth, 32),
                        Location = new Point(0, y + 22)
                    });

                    var themeSwatchRow = new FlowLayoutPanel
                    {
                        Location = new Point(0, y + 60),
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
                        swatch.LockedClicked += (_, _) =>
                        {
                            if (palette.Access == ThemeAccess.KoFi)
                            {
                                using var comingSoon = new ThemedConfirmForm(
                                    "Ko-fi theme",
                                    $"{palette.Name} unlocks with Ko-fi support, along with {Theme.ThemeNamesFor(ThemeAccess.KoFi)}. "
                                        + "Ko-fi sign-in is not available yet, so this one stays locked for now.",
                                    "Got it",
                                    string.Empty,
                                    showCancel: false);
                                comingSoon.ShowDialog(settingsPage);
                                return;
                            }

                            using var prompt = new ThemedConfirmForm(
                                "GitHub theme",
                                $"{palette.Name} unlocks with a GitHub sign-in, along with {Theme.ThemeNamesFor(ThemeAccess.GitHub)}.",
                                "Sign in with GitHub",
                                "Not now");
                            if (prompt.ShowDialog(settingsPage) == DialogResult.OK)
                                PromptSignIn();
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
                            TryEnableDarkTitleBar();
                            ApplyThemedBranding();
                        };
                        themeSwatchRow.Controls.Add(swatch);
                    }
                    contentScroll.Controls.Add(themeSwatchRow);

                    // The swatch strip wraps, so size the panel to the number of rows the
                    // current width produces instead of assuming a single line.
                    var swatchStride = ThemeSwatch.SwatchSize.Width + ThemeSwatch.SwatchMargin * 2;
                    var swatchesPerRow = Math.Max(1, fieldWidth / swatchStride);
                    var swatchRows = (Theme.Palettes.Count + swatchesPerRow - 1) / swatchesPerRow;
                    themeSwatchRow.Height = swatchRows * (ThemeSwatch.SwatchSize.Height + ThemeSwatch.SwatchMargin * 2);
                    y += 60 + themeSwatchRow.Height + 14;

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

                case "Accounts":
                    contentScroll.Controls.Add(new Label
                    {
                        Text = "Account",
                        Font = Theme.UiFont(10f, FontStyle.Bold),
                        ForeColor = Theme.Text,
                        AutoSize = true,
                        Location = new Point(0, y)
                    });
                    var accountRow = new Panel { Size = new Size(fieldWidth, 34), BackColor = Color.Transparent, Location = new Point(0, y + 28) };
                    var accountSignInButton = new FlatButton
                    {
                        Text = "Sign in with GitHub",
                        Size = new Size(190, 34),
                        Location = new Point(0, 0),
                        Visible = !AuthSession.IsSignedIn
                    };
                    var accountSignOutButton = new FlatButton
                    {
                        Text = "Sign out",
                        Size = new Size(100, 34),
                        Location = new Point(200, 0),
                        Visible = AuthSession.IsSignedIn
                    };
                    accountSignInButton.Click += (_, _) => PromptSignIn();
                    accountSignOutButton.Click += (_, _) =>
                    {
                        AuthSession.SignOut();
                        ApplyAccountChange();
                    };
                    accountRow.Controls.Add(accountSignInButton);
                    accountRow.Controls.Add(accountSignOutButton);
                    contentScroll.Controls.Add(accountRow);

                    // Ko-fi is not wired up yet, so the button stays greyed out and inert
                    // until the membership check lands in HasKoFiAccess.
                    var kofiRow = new Panel { Size = new Size(fieldWidth, 34), BackColor = Color.Transparent, Location = new Point(0, y + 68) };
                    var kofiSignInButton = new FlatButton
                    {
                        Text = "Sign in with Ko-fi (coming soon)",
                        Size = new Size(250, 34),
                        Location = new Point(0, 0),
                        Enabled = false
                    };
                    kofiRow.Controls.Add(kofiSignInButton);
                    contentScroll.Controls.Add(kofiRow);
                    y += 116;

                    // Mirrors the swatch outlines so the two unlock routes are obvious: gold for
                    // GitHub, pink for Ko-fi.
                    void AddAccessSection(Color dot, string title, string description)
                    {
                        contentScroll.Controls.Add(new Panel
                        {
                            Size = new Size(10, 10),
                            BackColor = dot,
                            Location = new Point(1, y + 6)
                        });
                        contentScroll.Controls.Add(new Label
                        {
                            Text = title,
                            Font = Theme.UiFont(10f, FontStyle.Bold),
                            ForeColor = Theme.Text,
                            AutoSize = true,
                            Location = new Point(20, y)
                        });
                        contentScroll.Controls.Add(new Label
                        {
                            Text = description,
                            Font = Theme.UiFont(8.75f),
                            ForeColor = Theme.Muted,
                            AutoSize = false,
                            Size = new Size(fieldWidth - 24, 42),
                            Location = new Point(20, y + 22)
                        });
                        y += 68;
                    }

                    AddAccessSection(
                        Theme.Muted,
                        "Free themes",
                        $"{Theme.ThemeNamesFor(ThemeAccess.Free)} need no account at all.");
                    AddAccessSection(
                        Theme.SupporterGold,
                        "GitHub themes",
                        AuthSession.HasGitHubAccess
                            ? $"{Theme.ThemeNamesFor(ThemeAccess.GitHub)} are unlocked with this account."
                            : $"{Theme.ThemeNamesFor(ThemeAccess.GitHub)} unlock with a GitHub sign-in.");
                    AddAccessSection(
                        Theme.KoFiPink,
                        "Ko-fi themes",
                        $"{Theme.ThemeNamesFor(ThemeAccess.KoFi)} unlock with Ko-fi support, which is coming soon.");
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
                        Text = "Downloads the latest GitHub release and restarts with the update installed.",
                        Font = Theme.UiFont(8.75f),
                        ForeColor = Theme.Muted,
                        AutoSize = true,
                        Location = new Point(0, y)
                    });
                    y += 24;

                    var updateButton = new FlatButton
                    {
                        Text = "Install latest GitHub release...",
                        Size = new Size(220, 36),
                        Location = new Point(0, y)
                    };
                    updateButton.Click += async (_, _) => await BeginGitHubUpdateAsync(updateButton);
                    contentScroll.Controls.Add(updateButton);
                    y += 56;

                    AddOption("Open the output folder when a conversion finishes", openOutputFolderAfterConversion,
                        value => openOutputFolderAfterConversion = value);

                    var maxLogInput = new NumericUpDown
                    {
                        Minimum = 200,
                        Maximum = 100_000,
                        Increment = 200,
                        Value = Math.Clamp(maxLogLines, 200, 100_000),
                        BackColor = Theme.Field,
                        ForeColor = Theme.Text,
                        Location = new Point(0, y),
                        Size = new Size(120, 30)
                    };
                    maxLogInput.ValueChanged += (_, _) => maxLogLines = (int)maxLogInput.Value;
                    AddField("Maximum log lines", "Older log lines are dropped once the conversion log passes this limit", maxLogInput);

                    var resetButton = new FlatButton
                    {
                        Text = "Reset all settings",
                        Size = new Size(180, 36),
                        Location = new Point(0, y)
                    };
                    resetButton.Click += (_, _) =>
                    {
                        using var confirm = new ThemedConfirmForm(
                            "Reset all settings",
                            "Every preference returns to its default. Your account, unlocked themes and converted mods are not affected.",
                            "Reset settings",
                            "Cancel");
                        if (confirm.ShowDialog(settingsPage) != DialogResult.OK)
                            return;

                        SettingsManager.Reset();
                        settingsSaved = false;
                        UpdateContent("General");
                    };
                    contentScroll.Controls.Add(resetButton);
                    y += 46;

                    break;
            }
        }

        var categories = new[] { "General", "Customization", "Accounts", "Advanced" };
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
            settings.RememberWindowLayout = rememberWindowLayout;
            settings.ValidateInputBeforeConverting = validateInputBeforeConverting;
            settings.CheckForUpdatesAutomatically = checkForUpdatesAutomatically;
            settings.OpenOutputFolderAfterConversion = openOutputFolderAfterConversion;
            settings.MaxLogLines = Math.Clamp(maxLogLines, 200, 100_000);
            SettingsManager.Save();
            settingsSaved = true;

            _dealerId.Text = settings.DefaultDealerId;
            _outputFolder.Text = settings.DefaultOutputFolder;
            _vehicleType.SelectedItem = VehicleTypeIdToDisplay(settings.DefaultVehicleType);
            ApplySettingsAppearance(this, settings.FontSize / previousFontSize, previousAccent);
            Invalidate(true);
            TryEnableDarkTitleBar();
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

    /// <summary>Human-readable "last checked" text for the update preference.</summary>
    internal static string DescribeLastUpdateCheck(string storedUtc) =>
        DateTime.TryParse(
            storedUtc,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out var parsed)
            ? "Last update check: " + parsed.ToLocalTime().ToString("g")
            : "No update check has run yet.";

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
                var title = line[3..].Trim();
                if (title.Equals("Unreleased", StringComparison.OrdinalIgnoreCase))
                {
                    release = null;
                    group = null;
                    continue;
                }

                release = new ChangelogRelease { Title = title };
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
