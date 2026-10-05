using System.Text.Json;

namespace TruckersToolKit;

/// <summary>
/// Versioning for the settings file.
/// <para>
/// <see cref="ReadVersion"/> and <see cref="ReadLegacyThemeMode"/> work on the raw
/// JSON rather than on <c>AppSettings</c>, because their whole purpose is to read fields that
/// no longer exist on it. The legacy property has to be read before it can be dropped, and a
/// deserializer silently ignores properties it does not know.
/// </para>
/// <para>
/// This type is deliberately free of any dependency so the migration can be covered by the
/// headless SettingsMigrationVerify suite; the rest of the settings code is tied to WinForms.
/// </para>
/// </summary>
public static class SettingsSchema
{
    /// <summary>
    /// Schema version written by this build. Bump this whenever the on-disk shape changes,
    /// and add the matching migration step to <c>SettingsManager.Load</c>.
    /// </summary>
    public const int CurrentVersion = 1;

    /// <summary>
    /// Schema version recorded in <paramref name="json"/>. A settings file written before
    /// versioning existed carries no such field and is reported as version 0 (legacy).
    /// A missing, malformed or non-numeric value is also 0, so a damaged file still goes
    /// through the migration path instead of being trusted.
    /// </summary>
    public static int ReadVersion(string? json)
    {
        if (!TryGetProperty(json, "SchemaVersion", out var property) ||
            property.ValueKind != JsonValueKind.Number ||
            !property.TryGetInt32(out var version) ||
            version <= 0)
        {
            return 0;
        }

        return version;
    }

    /// <summary>
    /// Reads the pre-v1 <c>ThemeMode</c> preference. Only the migration path uses this; the
    /// field is gone from <c>AppSettings</c> and is never written again.
    /// </summary>
    public static string? ReadLegacyThemeMode(string? json) =>
        TryGetProperty(json, "ThemeMode", out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    /// <summary>
    /// Picks the theme a settings file should end up on.
    /// <para>
    /// An explicit theme name always wins, which is what keeps a v1 file that already stores
    /// <c>ThemeName</c> from being dragged back to a legacy preference. Only when no name has
    /// been chosen yet does the old <c>ThemeMode</c> decide, and only "Light" maps across -
    /// every other legacy value was dark.
    /// </para>
    /// </summary>
    /// <summary>
    /// Theme names an earlier build wrote, mapped to their current equivalents.
    /// <para>
    /// The rebrand renamed the default dark palette from "Roadtrip" to "Truckers". Without this
    /// table a settings file still saying "Roadtrip" would resolve to nothing and the app would
    /// silently fall back to its first palette - the user waking up to a different colour with
    /// nothing in the log to explain it. Same failure shape as renaming the settings folder.
    /// </para>
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> ThemeNameAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Roadtrip"] = "Truckers"
        };

    /// <summary>
    /// Picks the theme a settings file should end up on.
    /// <para>
    /// An explicit theme name always wins, which is what keeps a v1 file that already stores
    /// <c>ThemeName</c> from being dragged back to a legacy preference. Only when no name has
    /// been chosen yet does the old <c>ThemeMode</c> decide, and only "Light" maps across -
    /// every other legacy value was dark.
    /// </para>
    /// </summary>
    public static string ResolveThemeName(string? themeName, string? legacyThemeMode)
    {
        if (!string.IsNullOrWhiteSpace(themeName))
            return ThemeNameAliases.TryGetValue(themeName.Trim(), out var renamed)
                ? renamed
                : themeName;

        return string.Equals(legacyThemeMode, "Light", StringComparison.OrdinalIgnoreCase)
            ? "Daylight"
            : "Truckers";
    }

    /// <summary>
    /// Looks up a top-level property, ignoring case, and returns its value.
    /// <para>
    /// The value is cloned out while the <see cref="JsonDocument"/> is still alive: both the
    /// enumerator and the elements it yields are invalidated once the document is disposed.
    /// </para>
    /// </summary>
    private static bool TryGetProperty(string? json, string name, out JsonElement value)
    {
        value = default;

        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    continue;

                value = property.Value.Clone();
                return true;
            }

            return false;
        }
        catch (JsonException)
        {
            // A settings file that cannot be parsed is treated as legacy. The caller already
            // falls back to defaults on a parse failure; this only decides which migration
            // path a readable-but-odd file takes.
            return false;
        }
    }
}