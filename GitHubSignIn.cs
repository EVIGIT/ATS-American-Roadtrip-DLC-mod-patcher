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

internal sealed record GitHubSignInRequest(
    string DeviceCode,
    string UserCode,
    string VerificationUri,
    int IntervalSeconds,
    int ExpiresInSeconds);

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
