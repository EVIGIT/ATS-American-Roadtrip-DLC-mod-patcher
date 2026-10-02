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
internal static class LocalSecretProtector
{
    private const int CryptprotectUiForbidden = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(
        ref DataBlob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(
        ref DataBlob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr handle);

    public static bool TryProtect(string plainText, out string protectedText)
    {
        return TryTransform(System.Text.Encoding.UTF8.GetBytes(plainText), protect: true, out protectedText);
    }

    public static bool TryUnprotect(string protectedText, out string plainText)
    {
        return TryTransform(Convert.FromBase64String(protectedText), protect: false, out plainText);
    }

    private static bool TryTransform(byte[] data, bool protect, out string result)
    {
        result = "";
        if (data.Length == 0)
            return false;

        var input = default(DataBlob);
        input.Size = data.Length;
        input.Data = Marshal.AllocHGlobal(data.Length);
        try
        {
            Marshal.Copy(data, 0, input.Data, data.Length);
            DataBlob output;
            var ok = protect
                ? CryptProtectData(ref input, "ATS Roadtrip Patcher session", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptprotectUiForbidden, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptprotectUiForbidden, out output);
            if (!ok)
                return false;
            try
            {
                var bytes = new byte[output.Size];
                Marshal.Copy(output.Data, bytes, 0, output.Size);
                result = protect ? Convert.ToBase64String(bytes) : System.Text.Encoding.UTF8.GetString(bytes);
                return true;
            }
            finally
            {
                if (output.Data != IntPtr.Zero)
                    LocalFree(output.Data);
            }
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(input.Data);
        }
    }
}

internal static class AuthSession
{
    private static readonly string SessionPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ATSRoadTripConverter",
        "auth.json");

    private static string? _accessToken;
    private static string? _login;

    public static string? Login => string.IsNullOrWhiteSpace(_login) ? null : _login;

    public static bool IsSignedIn => Login != null && !string.IsNullOrWhiteSpace(_accessToken);

    public static bool HasGitHubAccess => IsSignedIn;

    /// <summary>
    /// Ko-fi membership cannot be verified yet, so this tier stays locked. Swap the body
    /// for a real membership check once the Ko-fi sign-in flow exists.
    /// </summary>
    public static bool HasKoFiAccess => false;

    public static void Load()
    {
        try
        {
            if (!File.Exists(SessionPath))
                return;
            using var document = JsonDocument.Parse(File.ReadAllText(SessionPath));
            var root = document.RootElement;
            var storedToken = ReadString(root, "accessToken");
            _login = ReadString(root, "login");
            if (string.IsNullOrWhiteSpace(storedToken) || string.IsNullOrWhiteSpace(_login))
            {
                _accessToken = null;
                _login = null;
                return;
            }

            // Files written by older builds hold the raw token; re-save them encrypted.
            if (ReadBoolean(root, "protected"))
            {
                _accessToken = LocalSecretProtector.TryUnprotect(storedToken!, out var plain) ? plain : null;
                if (_accessToken == null)
                {
                    // Not decryptable by this Windows user (different account or machine).
                    _login = null;
                    DeleteSessionFile();
                }
            }
            else
            {
                _accessToken = storedToken;
                Save(_accessToken, _login!);
            }
        }
        catch
        {
            _accessToken = null;
            _login = null;
        }
    }

    internal static void Save(string accessToken, string login)
    {
        _accessToken = accessToken;
        _login = login;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SessionPath)!);
            if (!LocalSecretProtector.TryProtect(accessToken, out var protectedToken))
            {
                // Never write a readable token to disk: the sign-in still works for this
                // session, it just will not be remembered next launch.
                DeleteSessionFile();
                return;
            }

            File.WriteAllText(SessionPath, JsonSerializer.Serialize(new AuthSessionFile
            {
                AccessToken = protectedToken,
                Login = login,
                Protected = true
            }, FileOptions));
        }
        catch
        {
        }
    }

    private static readonly JsonSerializerOptions FileOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // Reads a string property regardless of casing so sessions written by older builds
    // (AccessToken) and the current camelCase format both load.
    private static string? ReadString(JsonElement root, string name)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
                return property.Value.GetString();
        }

        return null;
    }

    private static bool ReadBoolean(JsonElement root, string name)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return property.Value.ValueKind == JsonValueKind.True;
        }

        return false;
    }

    public static void SignOut()
    {
        _accessToken = null;
        _login = null;
        DeleteSessionFile();
        EnforceThemeAccess();
    }

    /// <summary>
    /// Falls back to a free theme when the stored preference points at a locked theme
    /// that the current visitor cannot use.
    /// </summary>
    public static void EnforceThemeAccess()
    {
        var stored = SettingsManager.Current.ThemeName;
        var palette = Theme.Palettes.FirstOrDefault(item => item.Name.Equals(stored, StringComparison.OrdinalIgnoreCase));
        if (palette != null && Theme.IsLocked(palette))
        {
            SettingsManager.Current.ThemeName = Theme.Palettes[0].Name;
            SettingsManager.Save();
        }
    }

    private static void DeleteSessionFile()
    {
        try
        {
            if (File.Exists(SessionPath))
                File.Delete(SessionPath);
        }
        catch
        {
        }
    }

    private sealed class AuthSessionFile
    {
        public string AccessToken { get; set; } = "";
        public string Login { get; set; } = "";
        public bool Protected { get; set; }
    }
}
