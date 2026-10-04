using System.Text.Json;
using System.Text.Json.Serialization;
namespace ATSRoadTripConverter;
public sealed class VehicleTypeCustom
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int SpeedLimit { get; set; } = 163;
    public string Tag { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsBuiltIn { get; set; } = false;
}

public sealed class AppSettings
{
    /// <summary>
    /// Schema version of this settings file. See <see cref="SettingsSchema"/>. Files written
    /// before versioning existed read as 0 and are migrated on load; see
    /// <c>SettingsManager.Load</c>.
    /// </summary>
    public int SchemaVersion { get; set; } = SettingsSchema.CurrentVersion;

    public string DefaultDealerId { get; set; } = "volvo";
    public string DefaultVehicleType { get; set; } = "pickup";
    public string DefaultOutputFolder { get; set; } = "";
    public string LastInputPath { get; set; } = "";
    public string LastWorkFolder { get; set; } = "";
    public string AccentColor { get; set; } = "#FF8A3D";
    public float FontSize { get; set; } = 9.5f;
    public string FontFamily { get; set; } = "Segoe UI";
    public bool AutoSaveSettings { get; set; } = true;
    public bool VerboseLogging { get; set; } = false;
    public bool BackupOriginal { get; set; } = false;

    // Window layout
    public bool RememberWindowLayout { get; set; } = true;
    public int WindowLeft { get; set; } = -1;
    public int WindowTop { get; set; } = -1;
    public int WindowWidth { get; set; } = 0;
    public int WindowHeight { get; set; } = 0;
    public bool WindowMaximized { get; set; } = false;

    // Conversion behaviour
    public bool ValidateInputBeforeConverting { get; set; } = true;
    public bool OpenOutputFolderAfterConversion { get; set; } = false;

    // Dealer branding. Both default to on: keeping the mod's own brand is the safe choice
    // because the game finds that dealer's logo by name, and copying the logo is what makes
    // a renamed dealer look right too. They only conflict when a mod is converted twice under
    // two different brand tokens, where the base game's own logo for one of them is replaced.
    public bool KeepModBrand { get; set; } = true;
    public bool CopyDealerLogo { get; set; } = true;

    public int MaxLogLines { get; set; } = 2000;

    // Updates
    public bool CheckForUpdatesAutomatically { get; set; } = true;
    public string LastUpdateCheckUtc { get; set; } = "";

    /// <summary>
    /// The release tag the user was last told about and dismissed, so the same version is not
    /// announced twice. Storing the tag rather than a boolean means a genuinely newer release is
    /// still announced. Empty means nothing has been dismissed.
    /// </summary>
    public string DismissedUpdateTag { get; set; } = "";

    // Reopen the last page on launch (roadmap item 7). Off by default: silently opening on
    // Settings instead of the converter would surprise people, so this is opt-in.
    public bool ReopenLastPage { get; set; } = false;

    /// <summary>
    /// Which overlay page was last open. See <see cref="MainPage"/>.
    /// <para>
    /// The converter on the property is load-bearing: without it, a value this build does not
    /// recognise fails the whole settings deserialise and resets every preference. See
    /// <see cref="MainPageJsonConverter"/>.
    /// </para>
    /// </summary>
    [JsonConverter(typeof(MainPageJsonConverter))]
    public MainPage LastPage { get; set; } = MainPage.Converter;

    /// <summary>
    /// Last Settings category opened, restored on the next launch. Empty or unrecognised values
    /// fall back to the first category.
    /// </summary>
    public string LastSettingsCategory { get; set; } = "";

    // Launch page. LastLaunchedVersion is empty on a first run, which is what makes the
    // launch page show the quick start instead of release notes.
    public bool ShowLaunchPage { get; set; } = true;
    public string LastLaunchedVersion { get; set; } = "";

    // Theme settings. ThemeName is the live preference; ThemeMode was the pre-v1.3.9
    // preference and has been removed. Its value is read once, during migration, by
    // SettingsSchema.ReadLegacyThemeMode, which is why no property for it remains here.
    public string ThemeName { get; set; } = "";

    public List<VehicleTypeCustom> CustomVehicleTypes { get; set; } = new();
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
                var schemaVersion = SettingsSchema.ReadVersion(json);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null)
                    Current = settings;

                // v0 -> v1: the old ThemeMode preference becomes ThemeName, and the file is
                // stamped with the current schema version so the migration runs only once.
                // Resolving the theme from the raw JSON is what allows ThemeMode to be gone
                // from AppSettings; the deserializer above would silently drop it.
                if (schemaVersion < SettingsSchema.CurrentVersion)
                {
                    Current.ThemeName = SettingsSchema.ResolveThemeName(
                        Current.ThemeName,
                        SettingsSchema.ReadLegacyThemeMode(json));
                    Current.SchemaVersion = SettingsSchema.CurrentVersion;
                    Save();
                }
            }
        }
        catch
        {
            Current = new AppSettings();
        }

        // A fresh install has no file and a damaged one falls back to defaults above, so
        // neither goes through the migration above. Both still need a valid theme. Routed
        // through SettingsSchema so the default is defined in exactly one place.
        Current.ThemeName = SettingsSchema.ResolveThemeName(Current.ThemeName, null);
        if (!Theme.Palettes.Any(palette => palette.Name.Equals(Current.ThemeName, StringComparison.OrdinalIgnoreCase)))
            Current.ThemeName = "Roadtrip";

        // Ensure built-in vehicle types exist
        EnsureBuiltInVehicleTypes();
    }

    /// <summary>
    /// Restores every preference to its default. Converted mods and the output folder on
    /// disk are not touched; this only affects preferences.
    /// </summary>
    public static void Reset()
    {
        Current = new AppSettings();
        Save();
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
            // Settings are not critical: a failed save must never interrupt a conversion.
            // Kept behind DEBUG so no debug hook ships in a release build.
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
