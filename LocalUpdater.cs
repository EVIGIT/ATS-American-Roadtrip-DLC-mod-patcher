namespace ATSRoadTripConverter;

internal sealed record LocalUpdatePlan(
    string SourceDirectory,
    string TargetDirectory,
    string ExecutableName,
    string ProcessName,
    string LogPath,
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
                cleanupDirectory == null ? null : Path.GetFullPath(cleanupDirectory));
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
$waitForAppExit = {{waitExpression}}
$relaunchApp = {{relaunchExpression}}
$showErrorDialog = {{dialogExpression}}
try {
    if ($waitForAppExit) {
        while (@(Get-Process -Name $processName -ErrorAction SilentlyContinue | Where-Object { $_.Id -ne $PID }).Count -gt 0) {
            Start-Sleep -Milliseconds 500
        }
    }
    & robocopy.exe $sourcePath $targetPath /E /COPY:DAT /R:2 /W:1 | Out-Null
    if ($LASTEXITCODE -ge 8) {
        throw "Copy failed with Robocopy exit code $LASTEXITCODE."
    }
    if ($relaunchApp) {
        Start-Process -FilePath (Join-Path $targetPath $executableName) -WorkingDirectory $targetPath
    }
    if ($cleanupDirectory -and (Test-Path -LiteralPath $cleanupDirectory)) {
        Remove-Item -LiteralPath $cleanupDirectory -Recurse -Force
    }
} catch {
    $message = $_.Exception.Message
    [System.IO.File]::WriteAllText($logPath, $message)
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