using System.Diagnostics;
using System.IO.Compression;

namespace ATSRoadTripConverter;

internal sealed record GitHubReleasePackage(
    string TagName,
    string TemporaryDirectory,
    string BuildDirectory);

internal static class GitHubReleaseClient
{
    public const string Repository = "EVIGIT/ATS-American-Roadtrip-DLC-mod-patcher";
    public const string WindowsReleaseAsset = "ATS-American-Roadtrip-Car-Patcher-win-x64.zip";

    public static string? TryLoadChangelog()
    {
        try
        {
            // Read the changelog at the newest published release rather than the default
            // branch. The default branch can legitimately lag behind a release that was
            // tagged from another branch, which used to hide the newest entry in-app.
            var reference = TryGetLatestReleaseTag() ?? "HEAD";
            var encodedContent = RunGh(TimeSpan.FromSeconds(20),
                "api",
                $"repos/{Repository}/contents/CHANGELOG.md?ref={Uri.EscapeDataString(reference)}",
                "--jq",
                ".content");
            return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encodedContent.Trim()));
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveLatestReleaseTag()
    {
        // `gh release view latest` reports "release not found" on some setups even when a
        // published latest release exists, so read the tag straight from the REST API.
        return RunGh(TimeSpan.FromSeconds(20),
            "api",
            $"repos/{Repository}/releases/latest",
            "--jq",
            ".tag_name").Trim();
    }

    internal static string? TryGetLatestReleaseTag()
    {
        try
        {
            var tag = ResolveLatestReleaseTag();
            return string.IsNullOrWhiteSpace(tag) ? null : tag;
        }
        catch
        {
            return null;
        }
    }

    public static GitHubReleasePackage DownloadLatestRelease(string currentVersion, string expectedExecutableName)
    {
        RunGh(TimeSpan.FromSeconds(20), "auth", "status", "--hostname", "github.com");

        var latestTag = ResolveLatestReleaseTag();

        if (!TryParseVersion(latestTag, out var latestVersion)
            || !TryParseVersion(currentVersion, out var installedVersion))
        {
            throw new InvalidOperationException($"Could not compare installed version '{currentVersion}' with release '{latestTag}'.");
        }

        if (Normalize(latestVersion!).CompareTo(Normalize(installedVersion!)) <= 0)
            throw new InvalidOperationException($"The installed app ({currentVersion}) is already up to date.");

        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"ats-roadtrip-release-{Guid.NewGuid():N}");
        var downloadDirectory = Path.Combine(temporaryDirectory, "download");
        var extractDirectory = Path.Combine(temporaryDirectory, "extracted");
        Directory.CreateDirectory(downloadDirectory);
        Directory.CreateDirectory(extractDirectory);

        try
        {
            RunGh(TimeSpan.FromMinutes(3),
                "release", "download", latestTag,
                "--repo", Repository,
                "--pattern", WindowsReleaseAsset,
                "--dir", downloadDirectory,
                "--clobber");

            var archivePath = Path.Combine(downloadDirectory, WindowsReleaseAsset);
            if (!File.Exists(archivePath))
                throw new InvalidOperationException($"Release {latestTag} did not contain {WindowsReleaseAsset}.");

            ZipFile.ExtractToDirectory(archivePath, extractDirectory);
            var executablePath = Directory
                .EnumerateFiles(extractDirectory, expectedExecutableName, SearchOption.AllDirectories)
                .FirstOrDefault();
            if (executablePath == null)
                throw new InvalidOperationException($"The release package did not contain {expectedExecutableName}.");

            var buildDirectory = Path.GetDirectoryName(executablePath)!;
            var assemblyName = $"{Path.GetFileNameWithoutExtension(expectedExecutableName)}.dll";
            if (!File.Exists(Path.Combine(buildDirectory, assemblyName)))
                throw new InvalidOperationException($"The release package did not contain {assemblyName} beside the app executable.");

            return new GitHubReleasePackage(latestTag, temporaryDirectory, buildDirectory);
        }
        catch
        {
            TryDeleteDirectory(temporaryDirectory);
            throw;
        }
    }

    public static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch
        {
        }
    }

    private static string RunGh(TimeSpan timeout, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "gh",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        Process process;
        try
        {
            process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start GitHub CLI.");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException("GitHub CLI is required to read releases and download updates. Install `gh` and sign in with `gh auth login`.", ex);
        }

        using (process)
        {
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            process.Kill(true);
            throw new TimeoutException("GitHub CLI request timed out.");
        }

        var output = outputTask.GetAwaiter().GetResult();
        var error = errorTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
        {
            var details = string.IsNullOrWhiteSpace(error) ? output.Trim() : error.Trim();
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(details)
                    ? "GitHub CLI failed. Install `gh`, run `gh auth login`, and verify access to the repository."
                    : details);
        }

        return output;
        }
    }

    private static bool TryParseVersion(string value, out Version? version)
    {
        var normalized = value.Trim();
        if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[1..];
        var suffixIndex = normalized.IndexOfAny(new[] { '-', '+' });
        if (suffixIndex >= 0)
            normalized = normalized[..suffixIndex];
        return Version.TryParse(normalized, out version);
    }

    private static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));
}