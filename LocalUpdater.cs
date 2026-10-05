namespace TruckersToolKit;
internal sealed record LocalUpdatePlan(
    string SourceDirectory,
    string TargetDirectory,
    string ExecutableName,
    string ProcessName,
    string LogPath,
    string ReadyPath,
    int AppProcessId,
    string? CleanupDirectory = null);

internal static class LocalUpdater
{
    public static bool TryCreatePlan(
        string sourceDirectory,
        string targetDirectory,
        string currentExecutablePath,
        out LocalUpdatePlan? plan,
        out string error,
        string? cleanupDirectory = null)
    {
        plan = null;
        error = "";

        try
        {
            var source = Path.GetFullPath(sourceDirectory);
            var target = Path.GetFullPath(targetDirectory);
            var executableName = Path.GetFileName(currentExecutablePath);
            if (!Directory.Exists(source) || !Directory.Exists(target) || string.IsNullOrWhiteSpace(executableName))
            {
                error = "Choose existing build and installation folders.";
                return false;
            }

            var assemblyName = $"{Path.GetFileNameWithoutExtension(executableName)}.dll";
            if (!File.Exists(Path.Combine(source, executableName)) || !File.Exists(Path.Combine(source, assemblyName)))
            {
                error = $"Choose a built app output folder containing {executableName} and {assemblyName}.";
                return false;
            }

            if (PathsOverlap(source, target))
            {
                error = "Choose a separate build output folder. The source and running app folders cannot overlap.";
                return false;
            }

            plan = new LocalUpdatePlan(
                source,
                target,
                executableName,
                Path.GetFileNameWithoutExtension(executableName),
                Path.Combine(Path.GetTempPath(), "ats-roadtrip-local-update.log"),
                Path.Combine(Path.GetTempPath(), "ats-roadtrip-local-update.ready"),
                Environment.ProcessId,
                cleanupDirectory == null ? null : Path.GetFullPath(cleanupDirectory));

            // A stale marker from a previous run would make the app believe the updater
            // is alive before this one has even started.
            try
            {
                File.Delete(plan.ReadyPath);
            }
            catch
            {
            }

            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            error = $"Could not validate the selected build folder: {ex.Message}";
            return false;
        }
    }

    public static string CreatePowerShellScript(
        LocalUpdatePlan plan,
        bool waitForAppExit,
        bool relaunchApp,
        bool showErrorDialog)
    {
        var waitExpression = waitForAppExit ? "$true" : "$false";
        var relaunchExpression = relaunchApp ? "$true" : "$false";
        var dialogExpression = showErrorDialog ? "$true" : "$false";

        return $$"""
$ErrorActionPreference = 'Stop'
$sourcePath = {{PowerShellLiteral(plan.SourceDirectory)}}
$targetPath = {{PowerShellLiteral(plan.TargetDirectory)}}
$executableName = {{PowerShellLiteral(plan.ExecutableName)}}
$processName = {{PowerShellLiteral(plan.ProcessName)}}
$logPath = {{PowerShellLiteral(plan.LogPath)}}
$cleanupDirectory = {{(plan.CleanupDirectory == null ? "$null" : PowerShellLiteral(plan.CleanupDirectory))}}
$appProcessId = {{plan.AppProcessId}}
$readyPath = {{PowerShellLiteral(plan.ReadyPath)}}
$waitForAppExit = {{waitExpression}}
$relaunchApp = {{relaunchExpression}}
$showErrorDialog = {{dialogExpression}}

# Signal the app that this helper is alive before the app closes, so the helper is
# never orphaned by the app exiting first.
try {
    Remove-Item -LiteralPath $readyPath -Force -ErrorAction SilentlyContinue
    Set-Content -LiteralPath $readyPath -Value 'ready' -Encoding ascii
    Set-Content -LiteralPath $logPath -Value "Updater started $(Get-Date -Format o)" -Encoding utf8
} catch {
}

try {
    if ($waitForAppExit) {
        # Wait on the exact process id rather than the name, so the helper never
        # mistakes another build of the same app for the running one.
        while (Get-Process -Id $appProcessId -ErrorAction SilentlyContinue) {
            Start-Sleep -Milliseconds 300
        }
        # Give Windows a moment to release the handles the app was holding.
        Start-Sleep -Milliseconds 800
    }
    # Fail with a readable message before robocopy gets a chance to report exit 16 with no
    # context. Exit 16 on its own told us nothing about which path or argument was wrong.
    if ([string]::IsNullOrWhiteSpace($sourcePath)) {
        throw "The build folder path is empty."
    }
    if ([string]::IsNullOrWhiteSpace($targetPath)) {
        throw "The installation folder path is empty."
    }
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Container)) {
        throw "The build folder '$sourcePath' does not exist."
    }
    if (-not (Test-Path -LiteralPath $targetPath -PathType Container)) {
        throw "The installation folder '$targetPath' does not exist."
    }
    $sourceFileCount = @(Get-ChildItem -LiteralPath $sourcePath -Recurse -File -Force).Count
    Add-Content -LiteralPath $logPath -Value "Copying $sourceFileCount file(s) from '$sourcePath' to '$targetPath' at $(Get-Date -Format o)" -Encoding utf8

    # Retry a bounded number of times with a short backoff. The first attempt can still trip
    # over an antivirus scanner or indexer holding a file the app released a moment ago.
    $exitCode = 1
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        # Robocopy reports progress on stdout and failures on stderr. The script runs under
        # $ErrorActionPreference = 'Stop', which can turn that stderr into a terminating error
        # and kill the copy before robocopy's exit code is ever read. Merging the streams and
        # temporarily relaxing the preference keeps the native exit code intact.
        $previousPreference = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try {
            $robocopyOutput = (& robocopy.exe $sourcePath $targetPath /E /COPY:DAT /R:2 /W:1 /NP 2>&1 | Out-String).Trim()
        }
        finally {
            $ErrorActionPreference = $previousPreference
        }
        $exitCode = $LASTEXITCODE
        Add-Content -LiteralPath $logPath -Value "Robocopy attempt $attempt/5 exited with code $exitCode at $(Get-Date -Format o)" -Encoding utf8
        if ($robocopyOutput) {
            Add-Content -LiteralPath $logPath -Value $robocopyOutput -Encoding utf8
        }
        if ($exitCode -lt 8) { break }
        # Exit 16 and above are structural: bad arguments, an unusable source, or a destination
        # that cannot be created. Retrying those just delays the same failure by a few seconds,
        # so stop immediately and let the log say why.
        if ($exitCode -ge 16) { break }
        Start-Sleep -Milliseconds 800
    }
    if ($exitCode -ge 8) {
        throw "Copy failed with Robocopy exit code $exitCode. Full Robocopy output is in the log at $logPath."
    }
    Add-Content -LiteralPath $logPath -Value "Files copied at $(Get-Date -Format o)" -Encoding utf8
    if ($relaunchApp) {
        Start-Process -FilePath (Join-Path $targetPath $executableName) -WorkingDirectory $targetPath
        Add-Content -LiteralPath $logPath -Value "App relaunched at $(Get-Date -Format o)" -Encoding utf8
    }
    if ($cleanupDirectory -and (Test-Path -LiteralPath $cleanupDirectory)) {
        Remove-Item -LiteralPath $cleanupDirectory -Recurse -Force
    }
} catch {
    $message = $_.Exception.Message
    Add-Content -LiteralPath $logPath -Value "FAILED: $message" -Encoding utf8
    if ($showErrorDialog) {
        Add-Type -AssemblyName System.Windows.Forms
        [System.Windows.Forms.MessageBox]::Show("The local update failed:`n$message`n`nDetails: $logPath", "Update failed", 'OK', 'Error') | Out-Null
    } else {
        [Console]::Error.WriteLine($message)
        exit 1
    }
}
exit 0
""";
    }

    private static bool PathsOverlap(string firstPath, string secondPath)
    {
        var first = Path.TrimEndingDirectorySeparator(firstPath);
        var second = Path.TrimEndingDirectorySeparator(secondPath);
        return first.Equals(second, StringComparison.OrdinalIgnoreCase)
            || first.StartsWith(second + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || second.StartsWith(first + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string PowerShellLiteral(string value) => $"'{value.Replace("'", "''")}'";
}
