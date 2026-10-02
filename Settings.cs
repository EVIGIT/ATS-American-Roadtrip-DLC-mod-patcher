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

public sealed class AppSettings
{
    public string DefaultDealerId { get; set; } = "volvo";
    public string DefaultVehicleType { get; set; } = "pickup";
    public string DefaultOutputFolder { get; set; } = "";
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
    public int MaxLogLines { get; set; } = 2000;

    // Updates
    public bool CheckForUpdatesAutomatically { get; set; } = true;
    public string LastUpdateCheckUtc { get; set; } = "";

    // Theme settings
    public string ThemeMode { get; set; } = "Dark"; // Legacy preference migrated to ThemeName.
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

    /// <summary>
    /// Restores every preference to its default. Sign-in state lives in a separate file
    /// and is deliberately untouched, so resetting can never change what is unlocked.
    /// </summary>
    public static void Reset()
    {
        Current = new AppSettings();
        Save();
        AuthSession.EnforceThemeAccess();
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
