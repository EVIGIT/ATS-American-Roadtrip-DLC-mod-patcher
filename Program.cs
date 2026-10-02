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

/// <summary>
/// Wraps the sign-in token with Windows DPAPI (CryptProtectData) so it can only be
/// decrypted by this user account on this machine. P/Invoked straight from crypt32.dll
/// so the app keeps its zero-dependency footprint - the ProtectedData NuGet package is
/// not needed for a net8.0-windows target.
/// </summary>
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

// Supporter sign-in state.
//
// A GitHub sign-in unlocks the GitHub tier of themes today. The Ko-fi tier is already
// wired up everywhere except the check itself: when the Ko-fi page goes live, replace
// the body of HasKoFiAccess with a membership check and nothing else has to change.
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

internal sealed record GitHubSignInRequest(
    string DeviceCode,
    string UserCode,
    string VerificationUri,
    int IntervalSeconds,
    int ExpiresInSeconds);

/// <summary>
/// GitHub OAuth device flow, the only flow a desktop app can run without shipping a
/// client secret. The user gets a short code, approves it in a browser, and the app
/// polls until the token arrives.
/// </summary>
internal static class GitHubDeviceSignIn
{
    /// <summary>
    /// Client id of the GitHub OAuth App used for device-flow sign-in. Set the
    /// ATS_GITHUB_OAUTH_CLIENT_ID environment variable to supply it without a rebuild
    /// (handy for testing); otherwise the compiled-in fallback is used.
    /// Create the app under Settings -> Developer settings -> OAuth Apps and tick
    /// "Enable device flow".
    /// </summary>
    public const string ClientIdEnvironmentVariable = "ATS_GITHUB_OAUTH_CLIENT_ID";

    private const string FallbackClientId = "Ov23liUpEYawjNtIO7UC";

    public static string ClientId
    {
        get
        {
            var fromEnvironment = Environment.GetEnvironmentVariable(ClientIdEnvironmentVariable);
            return !string.IsNullOrWhiteSpace(fromEnvironment) ? fromEnvironment.Trim() : FallbackClientId;
        }
    }

    private const string DeviceCodeUrl = "https://github.com/login/device/code";
    private const string AccessTokenUrl = "https://github.com/login/oauth/access_token";
    private const string UserUrl = "https://api.github.com/user";
    private const string Scope = "read:user";

    private static readonly HttpClient Http = CreateHttpClient();

    public static bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId)
        && !ClientId.StartsWith("REPLACE_", StringComparison.Ordinal);

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ATS-American-Roadtrip-Car-Patcher");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    public static async Task<GitHubSignInRequest> RequestCodeAsync(CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["scope"] = Scope
        });
        using var response = await Http.PostAsync(DeviceCodeUrl, content, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw DescribeFailure(response, body, "GitHub would not start the sign-in request.");

        using var document = ParseOrThrow(body, "GitHub did not return a sign-in code.");
        var root = document.RootElement;

        if (!root.TryGetProperty("device_code", out var deviceCode) || !root.TryGetProperty("user_code", out var userCode))
            throw new InvalidOperationException(ReadError(root, "GitHub did not return a sign-in code."));

        return new GitHubSignInRequest(
            deviceCode.GetString() ?? "",
            userCode.GetString() ?? "",
            root.TryGetProperty("verification_uri", out var uri) && !string.IsNullOrWhiteSpace(uri.GetString())
                ? uri.GetString()!
                : "https://github.com/login/device",
            Math.Max(5, root.TryGetProperty("interval", out var interval) ? interval.GetInt32() : 5),
            root.TryGetProperty("expires_in", out var expires) ? expires.GetInt32() : 900);
    }

    public static async Task<string> WaitForTokenAsync(
        GitHubSignInRequest request,
        Action<string> onStatus,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(request.ExpiresInSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = ClientId,
                ["device_code"] = request.DeviceCode,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
            });
            using var response = await Http.PostAsync(AccessTokenUrl, content, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw DescribeFailure(response, body, "GitHub could not complete the sign-in request.");

            using var document = ParseOrThrow(body, "GitHub did not return a sign-in token.");
            var root = document.RootElement;

            if (root.TryGetProperty("access_token", out var token) && !string.IsNullOrWhiteSpace(token.GetString()))
                return token.GetString()!;

            switch (root.TryGetProperty("error", out var error) ? error.GetString() : null)
            {
                case null:
                    throw new InvalidOperationException(ReadError(root, "GitHub did not return a sign-in token."));
                case "authorization_pending":
                    onStatus("Waiting for you to approve the request in your browser...");
                    break;
                case "slow_down":
                    onStatus("Still waiting for approval...");
                    break;
                case "expired_token":
                    throw new TimeoutException("That sign-in code expired. Start again for a fresh code.");
                case "access_denied":
                    throw new InvalidOperationException("The sign-in request was denied.");
                default:
                    throw new InvalidOperationException(ReadError(root, "GitHub could not complete the sign-in request."));
            }

            await Task.Delay(TimeSpan.FromSeconds(request.IntervalSeconds), cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException("The sign-in request timed out. Start again for a fresh code.");
    }

    public static async Task<string> ReadLoginAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, UserUrl);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(DescribeFailure(response, body, "GitHub would not confirm your profile.").Message);

        using var document = ParseOrThrow(body, "GitHub did not return your profile.");
        return document.RootElement.TryGetProperty("login", out var login) && !string.IsNullOrWhiteSpace(login.GetString())
            ? login.GetString()!
            : "GitHub user";
    }

    public static void OpenVerificationPage(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
        }
    }

    private static string ReadError(JsonElement root, string fallback)
    {
        if (root.TryGetProperty("error_description", out var description) && !string.IsNullOrWhiteSpace(description.GetString()))
            return description.GetString()!;
        if (root.TryGetProperty("error", out var error) && !string.IsNullOrWhiteSpace(error.GetString()))
            return error.GetString()!;
        return fallback;
    }

    private static JsonDocument ParseOrThrow(string body, string fallback)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            // Never surface a raw parser message to the user.
            throw new InvalidOperationException($"{fallback} Check your internet connection and try again.");
        }
    }

    // Turns any unsuccessful GitHub response into a readable error. GitHub usually
    // answers with JSON, but proxies and invalid hosts can return HTML or nothing.
    private static Exception DescribeFailure(HttpResponseMessage response, string body, string fallback)
    {
        var status = (int)response.StatusCode;
        if (status is 401 or 403 or 404)
        {
            // GitHub answers an unknown client id with a bare "Not Found", which tells
            // the reader nothing, so lead with what they can actually act on.
            return new InvalidOperationException(
                $"{fallback} GitHub returned HTTP {status}, which usually means the OAuth app client id is wrong or device flow is not enabled for that app.");
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            return new InvalidOperationException(ReadError(document.RootElement, $"{fallback} (HTTP {status})"));
        }
        catch (JsonException)
        {
            return new InvalidOperationException($"{fallback} GitHub returned HTTP {status}.");
        }
    }
}

internal static class Program
{
    public const string AppName = "ATS American Roadtrip Car Patcher";
    public const string AppVersion = "v1.3.2";

    [STAThread]
    static void Main()
    {
        SettingsManager.Load();
        AuthSession.Load();
        AuthSession.EnforceThemeAccess();
        ApplicationConfiguration.Initialize();

        // The sign-in window is optional by design: skipping it only leaves the
        // supporter themes locked.
        using (var signIn = new SignInForm())
            Application.Run(signIn);

        Application.Run(new ConverterForm());
    }
}

/// <summary>How a palette is unlocked.</summary>
internal enum ThemeAccess
{
    /// <summary>Anyone can use it, signed in or not.</summary>
    Free,
    /// <summary>Unlocked by signing in with GitHub.</summary>
    GitHub,
    /// <summary>Reserved for Ko-fi supporters once that sign-in exists.</summary>
    KoFi
}

internal sealed record ThemePalette(
    string Name,
    Color Background,
    Color Surface,
    Color Field,
    Color Border,
    Color Text,
    Color Muted,
    Color Accent,
    ThemeAccess Access = ThemeAccess.Free);

internal static class Theme
{
    public static IReadOnlyList<ThemePalette> Palettes { get; } = Array.AsReadOnly(new[]
    {
        new ThemePalette("Roadtrip", Color.FromArgb(13, 16, 21), Color.FromArgb(20, 25, 34), Color.FromArgb(27, 33, 44), Color.FromArgb(38, 46, 60), Color.FromArgb(233, 237, 244), Color.FromArgb(139, 149, 167), Color.FromArgb(255, 138, 61)),
        new ThemePalette("Midnight", Color.FromArgb(10, 16, 26), Color.FromArgb(16, 24, 37), Color.FromArgb(23, 33, 50), Color.FromArgb(33, 46, 69), Color.FromArgb(230, 238, 247), Color.FromArgb(134, 151, 174), Color.FromArgb(76, 194, 255), ThemeAccess.KoFi),
        new ThemePalette("Evergreen", Color.FromArgb(10, 18, 15), Color.FromArgb(16, 26, 22), Color.FromArgb(23, 36, 30), Color.FromArgb(33, 50, 42), Color.FromArgb(228, 240, 233), Color.FromArgb(134, 160, 150), Color.FromArgb(63, 217, 140), ThemeAccess.KoFi),
        new ThemePalette("Ember", Color.FromArgb(23, 15, 16), Color.FromArgb(31, 21, 23), Color.FromArgb(42, 28, 30), Color.FromArgb(57, 38, 41), Color.FromArgb(244, 233, 231), Color.FromArgb(166, 144, 142), Color.FromArgb(255, 107, 87), ThemeAccess.GitHub),
        new ThemePalette("Daylight", Color.FromArgb(245, 247, 250), Color.FromArgb(255, 255, 255), Color.FromArgb(238, 241, 246), Color.FromArgb(222, 228, 237), Color.FromArgb(16, 21, 28), Color.FromArgb(92, 103, 120), Color.FromArgb(37, 99, 235)),
        new ThemePalette("Steel", Color.FromArgb(17, 19, 23), Color.FromArgb(24, 27, 32), Color.FromArgb(32, 36, 43), Color.FromArgb(44, 49, 58), Color.FromArgb(232, 236, 240), Color.FromArgb(146, 155, 167), Color.FromArgb(158, 190, 219)),
        new ThemePalette("Lagoon", Color.FromArgb(9, 19, 21), Color.FromArgb(14, 29, 33), Color.FromArgb(20, 41, 46), Color.FromArgb(28, 57, 64), Color.FromArgb(224, 243, 243), Color.FromArgb(126, 165, 168), Color.FromArgb(45, 212, 191), ThemeAccess.KoFi),
        new ThemePalette("Obsidian", Color.FromArgb(10, 10, 12), Color.FromArgb(19, 19, 23), Color.FromArgb(28, 28, 34), Color.FromArgb(42, 42, 50), Color.FromArgb(242, 240, 234), Color.FromArgb(154, 151, 142), Color.FromArgb(227, 179, 65), ThemeAccess.GitHub),
        new ThemePalette("Crimson", Color.FromArgb(21, 12, 14), Color.FromArgb(31, 18, 21), Color.FromArgb(43, 25, 29), Color.FromArgb(58, 34, 40), Color.FromArgb(248, 234, 236), Color.FromArgb(190, 150, 157), Color.FromArgb(255, 77, 109), ThemeAccess.GitHub),
        new ThemePalette("Sandstone", Color.FromArgb(250, 246, 240), Color.FromArgb(255, 255, 255), Color.FromArgb(243, 236, 227), Color.FromArgb(226, 216, 203), Color.FromArgb(38, 31, 24), Color.FromArgb(126, 112, 95), Color.FromArgb(194, 112, 58), ThemeAccess.KoFi),
        new ThemePalette("Aurora", Color.FromArgb(14, 12, 26), Color.FromArgb(23, 20, 40), Color.FromArgb(32, 28, 55), Color.FromArgb(45, 39, 74), Color.FromArgb(237, 234, 255), Color.FromArgb(154, 145, 190), Color.FromArgb(167, 139, 250), ThemeAccess.KoFi),
        new ThemePalette("Vapor", Color.FromArgb(20, 11, 36), Color.FromArgb(29, 16, 51), Color.FromArgb(40, 26, 71), Color.FromArgb(59, 39, 102), Color.FromArgb(242, 234, 255), Color.FromArgb(168, 150, 201), Color.FromArgb(255, 79, 216), ThemeAccess.KoFi)
    });

    private static ThemePalette CurrentPalette
    {
        get
        {
            var palette = Palettes.FirstOrDefault(palette => palette.Name.Equals(SettingsManager.Current.ThemeName, StringComparison.OrdinalIgnoreCase)) ?? Palettes[0];
            // Defence in depth: a locked theme can never paint, even if the stored
            // preference somehow still points at one while signed out.
            return IsLocked(palette) ? Palettes[0] : palette;
        }
    }

    public static Color Background => CurrentPalette.Background;
    public static Color Surface => CurrentPalette.Surface;
    public static Color Field => CurrentPalette.Field;
    public static Color Border => CurrentPalette.Border;
    public static Color Text => CurrentPalette.Text;
    public static Color Muted => CurrentPalette.Muted;
    /// <summary>Warm gold used to outline the themes a GitHub sign-in unlocks.</summary>
    public static readonly Color SupporterGold = Color.FromArgb(227, 179, 65);

    /// <summary>Ko-fi's pink, used to outline the themes Ko-fi support will unlock.</summary>
    public static readonly Color KoFiPink = Color.FromArgb(255, 94, 91);

    /// <summary>The colour a locked palette is outlined with.</summary>
    public static Color LockColor(ThemeAccess access) => access == ThemeAccess.KoFi ? KoFiPink : SupporterGold;

    /// <summary>True when the current visitor can actually use this palette.</summary>
    public static bool IsUnlocked(ThemePalette palette) => palette.Access switch
    {
        ThemeAccess.Free => true,
        ThemeAccess.GitHub => AuthSession.HasGitHubAccess || AuthSession.HasKoFiAccess,
        ThemeAccess.KoFi => AuthSession.HasKoFiAccess,
        _ => true
    };

    /// <summary>True while a palette is reserved and cannot be selected.</summary>
    public static bool IsLocked(ThemePalette palette) => !IsUnlocked(palette);

    /// <summary>Lists one tier's palettes for prompts, e.g. "Crimson, Obsidian and Ember".</summary>
    public static string ThemeNamesFor(ThemeAccess access)
    {
        var names = Palettes.Where(palette => palette.Access == access).Select(palette => palette.Name).ToArray();
        return names.Length switch
        {
            0 => access == ThemeAccess.KoFi ? "the Ko-fi themes" : "the GitHub themes",
            1 => names[0],
            _ => string.Join(", ", names.Take(names.Length - 1)) + " and " + names[^1]
        };
    }

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
                return Color.FromArgb(255, 138, 61);
            }
        }
    }
    public static Color AccentHover => ControlPaint.Light(Accent, 0.2f);
    public static Color AccentPressed => ControlPaint.Dark(Accent, 0.14f);

    public static Color FieldHover =>
        IsLightBackground ? ControlPaint.Dark(Field, 0.06f) : ControlPaint.Light(Field, 0.14f);

    public static Color FieldPressed =>
        IsLightBackground ? ControlPaint.Dark(Field, 0.13f) : ControlPaint.Dark(Field, 0.06f);

    /// <summary>Readable foreground for text drawn on top of <paramref name="accent"/>.</summary>
    public static Color OnAccent(Color accent)
    {
        var luminance = 0.299 * accent.R + 0.587 * accent.G + 0.114 * accent.B;
        return luminance > 155 ? Color.FromArgb(14, 17, 22) : Color.White;
    }

    /// <summary>
    /// Colour of the surface sitting directly behind <paramref name="control"/>.
    /// Used so rounded corners erase cleanly instead of leaving stale pixels behind.
    /// </summary>
    public static Color HostColor(Control control)
    {
        for (var parent = control.Parent; parent != null; parent = parent.Parent)
        {
            if (parent is Card)
                return Surface;

            // Transparent layout panels paint nothing, so keep walking until we reach a
            // surface that really is drawn. Clearing to Color.Transparent renders as
            // black behind a non-layered window, which shows up in the rounded corners.
            if (parent.BackColor.A == 255)
                return parent.BackColor;
        }

        return Background;
    }

    public static LinearGradientBrush AccentGradient(Rectangle rect) =>
        new(rect, ControlPaint.Light(Accent, 0.16f), ControlPaint.Dark(Accent, 0.12f), LinearGradientMode.Vertical);

    public static readonly Color Success = Color.FromArgb(76, 201, 128);
    public static readonly Color Warning = Color.FromArgb(240, 196, 70);
    public static readonly Color Error = Color.FromArgb(240, 92, 92);
    public static readonly Color Info = Color.FromArgb(110, 168, 254);

    public static bool IsLightBackground
    {
        get
        {
            var background = Background;
            return (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) > 140;
        }
    }

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
        if (!Enabled)
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

/// <summary>Rounded, accent-tinted pill used for compact labels such as the version tag.</summary>
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

/// <summary>
/// Startup sign-in window. It never blocks the app: skipping simply leaves the
/// supporter themes locked, and the window can be reopened from Settings.
/// </summary>
internal sealed class SignInForm : Form
{
    private readonly FlatButton _primaryButton = new();
    private readonly FlatButton _secondaryButton = new();
    private readonly Label _statusTitle = new();
    private readonly Label _statusDetail = new();
    private readonly Panel _codeCard = new();
    private readonly Label _codeLabel = new();
    private CancellationTokenSource? _signIn;
    private bool _busy;

    public SignInForm()
    {
        Text = $"Sign in - {Program.AppName}";
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.UiFont(9.5f);
        ClientSize = new Size(600, 470);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;
        KeyPreview = true;
        using (var icon = ConverterForm.LoadResource("app.ico"))
        {
            if (icon != null)
                Icon = new Icon(icon);
        }

        BuildUi();
        if (AuthSession.IsSignedIn)
            ShowSignedInState();
        else
            ShowSignedOutState();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ConverterForm.ApplyDarkTitleBar(this);
    }

    private void BuildUi()
    {
        const int pad = 32;
        const int width = 600 - pad * 2;

        Controls.Add(new Label
        {
            Text = "Unlock the supporter themes",
            Font = Theme.UiFont(16f, FontStyle.Bold),
            ForeColor = Theme.Text,
            AutoSize = true,
            Location = new Point(pad, 28)
        });
        Controls.Add(new Label
        {
            Text = $"{Theme.ThemeNamesFor(ThemeAccess.GitHub)} unlock with a GitHub sign-in. "
                + $"{Theme.ThemeNamesFor(ThemeAccess.Free)} stay free with no account at all.",
            Font = Theme.UiFont(9f),
            ForeColor = Theme.Muted,
            AutoSize = false,
            Size = new Size(width, 44),
            Location = new Point(pad, 62)
        });

        _statusTitle.Font = Theme.UiFont(10f, FontStyle.Bold);
        _statusTitle.ForeColor = Theme.Text;
        _statusTitle.AutoSize = true;
        _statusTitle.Location = new Point(pad, 124);
        Controls.Add(_statusTitle);

        _statusDetail.Font = Theme.UiFont(8.75f);
        _statusDetail.ForeColor = Theme.Muted;
        _statusDetail.AutoSize = false;
        _statusDetail.Size = new Size(width, 34);
        _statusDetail.Location = new Point(pad, 146);
        Controls.Add(_statusDetail);

        _codeCard.Size = new Size(width, 96);
        _codeCard.Location = new Point(pad, 190);
        _codeCard.BackColor = Theme.Surface;
        _codeCard.Visible = false;
        Controls.Add(_codeCard);

        _codeCard.Controls.Add(new Label
        {
            Text = "Enter this code at github.com/login/device",
            Font = Theme.UiFont(8.75f),
            ForeColor = Theme.Muted,
            AutoSize = true,
            Location = new Point(18, 14)
        });
        _codeLabel.Font = Theme.MonoFont(20f);
        _codeLabel.ForeColor = Theme.Text;
        _codeLabel.AutoSize = true;
        _codeLabel.Location = new Point(18, 40);
        _codeCard.Controls.Add(_codeLabel);

        var openPageButton = new FlatButton
        {
            Text = "Open GitHub",
            Size = new Size(140, 32),
            Location = new Point(width - 158, 32)
        };
        openPageButton.Click += (_, _) =>
        {
            if (_codeLabel.Tag is string url)
                GitHubDeviceSignIn.OpenVerificationPage(url);
        };
        _codeCard.Controls.Add(openPageButton);

        Controls.Add(new Label
        {
            Text = "Sign-in only reads your public GitHub profile. Nothing is posted, and the app never stores your password.",
            Font = Theme.UiFont(8.75f),
            ForeColor = Theme.Muted,
            AutoSize = false,
            Size = new Size(width, 34),
            Location = new Point(pad, 306)
        });

        _primaryButton.Size = new Size(210, 38);
        _primaryButton.Location = new Point(pad, 372);
        _primaryButton.Primary = true;
        _primaryButton.Click += async (_, _) => await OnPrimaryAsync();
        Controls.Add(_primaryButton);

        _secondaryButton.Size = new Size(210, 38);
        _secondaryButton.Location = new Point(600 - pad - 210, 372);
        _secondaryButton.Click += (_, _) => OnSecondary();
        Controls.Add(_secondaryButton);

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                OnSecondary();
                e.Handled = true;
            }
        };
        FormClosing += (_, _) => _signIn?.Cancel();
    }

    private void ShowSignedInState()
    {
        SetStatus($"Signed in as {AuthSession.Login}", "The supporter themes are unlocked on this device.");
        _codeCard.Visible = false;
        _primaryButton.Text = "Continue";
        _primaryButton.Enabled = true;
        _secondaryButton.Text = "Sign out";
        _secondaryButton.Visible = true;
    }

    private void ShowSignedOutState()
    {
        SetStatus(
            "Not signed in",
            GitHubDeviceSignIn.IsConfigured
                ? "The two supporter themes stay locked until you sign in."
                : "Sign-in is not configured yet, so the supporter themes stay locked. Add your GitHub OAuth App client id to enable it.");
        _codeCard.Visible = false;
        _primaryButton.Text = "Sign in with GitHub";
        _primaryButton.Enabled = GitHubDeviceSignIn.IsConfigured;
        _secondaryButton.Text = "Continue without account";
        _secondaryButton.Visible = true;
    }

    private void SetStatus(string title, string detail)
    {
        _statusTitle.Text = title;
        _statusDetail.Text = detail;
    }

    private void OnSecondary()
    {
        if (_busy)
        {
            _signIn?.Cancel();
            return;
        }

        if (AuthSession.IsSignedIn)
        {
            AuthSession.SignOut();
            ShowSignedOutState();
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private async Task OnPrimaryAsync()
    {
        if (_busy)
            return;

        if (AuthSession.IsSignedIn)
        {
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        if (!GitHubDeviceSignIn.IsConfigured)
            return;

        _busy = true;
        _signIn = new CancellationTokenSource();
        var token = _signIn.Token;
        _primaryButton.Enabled = false;
        _secondaryButton.Text = "Cancel";
        _secondaryButton.Visible = true;
        try
        {
            SetStatus("Contacting GitHub", "Requesting a one-time sign-in code...");
            var request = await GitHubDeviceSignIn.RequestCodeAsync(token).ConfigureAwait(true);
            if (IsDisposed)
                return;

            _codeLabel.Text = request.UserCode;
            _codeLabel.Tag = request.VerificationUri;
            _codeCard.Visible = true;
            SetStatus("Waiting for GitHub", "Approve the request in your browser to continue.");
            GitHubDeviceSignIn.OpenVerificationPage(request.VerificationUri);

            var accessToken = await GitHubDeviceSignIn.WaitForTokenAsync(
                request,
                message => SetStatus("Waiting for GitHub", message),
                token).ConfigureAwait(true);
            if (IsDisposed)
                return;

            SetStatus("Finishing up", "Reading your GitHub profile...");
            var login = await GitHubDeviceSignIn.ReadLoginAsync(accessToken, token).ConfigureAwait(true);
            if (IsDisposed)
                return;

            AuthSession.Save(accessToken, login);
            SetStatus($"Signed in as {login}", "The supporter themes are unlocked on this device.");
            await Task.Delay(700).ConfigureAwait(true);
            if (!IsDisposed)
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        }
        catch (OperationCanceledException)
        {
            ShowSignedOutState();
        }
        catch (Exception ex)
        {
            SetStatus("Sign-in failed", ex.Message);
            _codeCard.Visible = false;
            _primaryButton.Enabled = GitHubDeviceSignIn.IsConfigured;
            _secondaryButton.Text = "Continue without account";
        }
        finally
        {
            _busy = false;
            _signIn?.Dispose();
            _signIn = null;
        }
    }
}

/// <summary>Small circular info badge used by the themed confirm dialog.</summary>
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

/// <summary>
/// Themed replacement for MessageBox, so in-app prompts follow the user's chosen
/// palette instead of the system look.
/// </summary>
internal sealed class ThemedConfirmForm : Form
{
    public ThemedConfirmForm(string title, string message, string acceptText, string cancelText)
        : this(title, message, acceptText, cancelText, showCancel: true)
    {
    }

    /// <param name="showCancel">False renders a single acknowledge button on the right.</param>
    public ThemedConfirmForm(string title, string message, string acceptText, string cancelText, bool showCancel)
    {
        Text = title;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.UiFont(9.5f);
        ClientSize = new Size(520, 244);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        DoubleBuffered = true;

        const int pad = 28;
        const int width = 520 - pad * 2;

        Controls.Add(new InfoDot { Location = new Point(pad, 24) });
        Controls.Add(new Label
        {
            Text = title,
            Font = Theme.UiFont(12f, FontStyle.Bold),
            ForeColor = Theme.Text,
            AutoSize = true,
            Location = new Point(pad + 42, 26)
        });
        Controls.Add(new Label
        {
            Text = message,
            Font = Theme.UiFont(9.5f),
            ForeColor = Theme.Muted,
            AutoSize = false,
            Size = new Size(width - 42, 60),
            Location = new Point(pad + 42, 56)
        });

        var accept = new FlatButton
        {
            Text = acceptText,
            Size = new Size(190, 36),
            Location = showCancel ? new Point(pad, 176) : new Point(520 - pad - 190, 176),
            Primary = true
        };
        accept.Click += (_, _) => Finish(DialogResult.OK);
        Controls.Add(accept);

        if (showCancel)
        {
            var cancel = new FlatButton
            {
                Text = cancelText,
                Size = new Size(150, 36),
                Location = new Point(520 - pad - 150, 176)
            };
            cancel.Click += (_, _) => Finish(DialogResult.Cancel);
            Controls.Add(cancel);
        }

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                Finish(DialogResult.Cancel);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Enter)
            {
                Finish(DialogResult.OK);
                e.Handled = true;
            }
        };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ConverterForm.ApplyDarkTitleBar(this);
    }

    private void Finish(DialogResult result)
    {
        DialogResult = result;
        Close();
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
        CornerRadius = 23,
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
        ApplyDarkTitleBar(this);
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

        return y + 290 + 16;
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
                VehicleTypeDisplayToId(_vehicleType.SelectedItem as string));

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

            if (SettingsManager.Current.AutoSaveSettings)
            {
                SettingsManager.Current.DefaultDealerId = _dealerId.Text.Trim();
                SettingsManager.Current.DefaultOutputFolder = _outputFolder.Text.Trim();
                SettingsManager.Current.DefaultVehicleType = VehicleTypeDisplayToId(_vehicleType.SelectedItem as string);
                SettingsManager.Save();
            }
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
                            : $"Choose a coordinated palette for the app. {Theme.ThemeNamesFor(ThemeAccess.Free)} are free; "
                                + "a gold outline unlocks with GitHub and a pink outline with Ko-fi.",
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
                                "Supporter theme",
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
                    y += 44 + themeSwatchRow.Height + 14;

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
