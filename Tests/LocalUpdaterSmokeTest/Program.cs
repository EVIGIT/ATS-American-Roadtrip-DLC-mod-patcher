using System.Diagnostics;
using System.Text;
using TruckersToolKit;

if (!OperatingSystem.IsWindows())
{
    Console.WriteLine("This updater smoke test requires Windows PowerShell and Robocopy.");
    return 2;
}

// Lifetime of the stand-in process the waitForAppExit scenario waits on.
const int HelperLifetimeMilliseconds = 2000;
var temporaryRoot = Path.Combine(Path.GetTempPath(), $"ats-updater-smoke-{Guid.NewGuid():N}");
var sourceDirectory = Path.Combine(temporaryRoot, "build");
var targetDirectory = Path.Combine(temporaryRoot, "installed app");
Directory.CreateDirectory(Path.Combine(sourceDirectory, "data", "nested"));
Directory.CreateDirectory(targetDirectory);

try
{
    const string executableName = "SmokeApp.exe";
    var currentExecutablePath = Path.Combine(targetDirectory, executableName);
    File.WriteAllText(Path.Combine(sourceDirectory, executableName), "new executable");
    File.WriteAllText(Path.Combine(sourceDirectory, "SmokeApp.dll"), "new assembly");
    File.WriteAllText(Path.Combine(sourceDirectory, "data", "nested", "settings.dat"), "new nested file");
    File.WriteAllText(currentExecutablePath, "old executable");
    File.WriteAllText(Path.Combine(targetDirectory, "SmokeApp.dll"), "old assembly");
    File.WriteAllText(Path.Combine(targetDirectory, "keep.dat"), "preserve existing files");

    if (!LocalUpdater.TryCreatePlan(sourceDirectory, targetDirectory, currentExecutablePath, out var plan, out var error))
        throw new InvalidOperationException($"Valid build was rejected: {error}");

    var firstScript = LocalUpdater.CreatePowerShellScript(plan!, waitForAppExit: false, relaunchApp: false, showErrorDialog: false);
    var firstRun = RunScript(firstScript, plan!.LogPath);
    if (firstRun.ExitCode != 0)
        throw new InvalidOperationException(
            $"Updater script failed ({firstRun.ExitCode}).\n{firstRun.StandardError}\n{firstRun.Output}\n{ReadLog(plan!.LogPath)}\nGenerated script:\n{firstScript}");

    AssertFileContains(Path.Combine(targetDirectory, executableName), "new executable");
    AssertFileContains(Path.Combine(targetDirectory, "SmokeApp.dll"), "new assembly");
    AssertFileContains(Path.Combine(targetDirectory, "data", "nested", "settings.dat"), "new nested file");
    AssertFileContains(Path.Combine(targetDirectory, "keep.dat"), "preserve existing files");

    var nestedTarget = Path.Combine(temporaryRoot, "build", "nested-install");
    Directory.CreateDirectory(nestedTarget);
    if (LocalUpdater.TryCreatePlan(sourceDirectory, nestedTarget, currentExecutablePath, out _, out _))
        throw new InvalidOperationException("Updater accepted overlapping source and target folders.");

    var incompleteSource = Path.Combine(temporaryRoot, "incomplete build");
    Directory.CreateDirectory(incompleteSource);
    File.WriteAllText(Path.Combine(incompleteSource, executableName), "missing assembly");
    if (LocalUpdater.TryCreatePlan(incompleteSource, targetDirectory, currentExecutablePath, out _, out _))
        throw new InvalidOperationException("Updater accepted a build missing its app DLL.");

    // The generated script must validate both paths before handing them to robocopy. The
    // original bug reached robocopy with an unusable path and surfaced only "exit code 16".
    foreach (var required in new[] { "IsNullOrWhiteSpace($sourcePath)", "IsNullOrWhiteSpace($targetPath)", "Test-Path -LiteralPath $sourcePath", "Test-Path -LiteralPath $targetPath" })
        if (!firstScript.Contains(required, StringComparison.Ordinal))
            throw new InvalidOperationException($"Generated script is missing the guard '{required}'.");

    // A target folder that does not exist must produce a readable message, not a bare exit 16.
    var missingTargetScript = LocalUpdater.CreatePowerShellScript(
        plan! with { TargetDirectory = Path.Combine(temporaryRoot, "no such folder") },
        waitForAppExit: false,
        relaunchApp: false,
        showErrorDialog: false);
    var missingTargetRun = RunScript(missingTargetScript, plan!.LogPath);
    if (missingTargetRun.ExitCode == 0)
        throw new InvalidOperationException("Updater reported success for a target folder that does not exist.");
    if (!ReadLog(plan!.LogPath).Contains("does not exist", StringComparison.Ordinal))
        throw new InvalidOperationException(
            $"Missing target folder did not produce a readable log message.\n{ReadLog(plan!.LogPath)}\n{missingTargetRun.StandardError}");
    if (ReadLog(plan!.LogPath).Contains("Robocopy attempt", StringComparison.Ordinal))
        throw new InvalidOperationException("Robocopy was invoked despite an invalid target folder.");

    // A missing *source* folder is likewise caught by the guard before robocopy runs. That is
    // the point of the guard: it converts a would-be structural robocopy failure into a
    // readable message, so robocopy is never invoked at all.
    var missingSourceScript = LocalUpdater.CreatePowerShellScript(
        plan! with { SourceDirectory = Path.Combine(temporaryRoot, "no such build") },
        waitForAppExit: false,
        relaunchApp: false,
        showErrorDialog: false);
    var missingSourceRun = RunScript(missingSourceScript, plan!.LogPath);
    if (missingSourceRun.ExitCode == 0)
        throw new InvalidOperationException("Updater reported success for a build folder that does not exist.");
    var missingSourceLog = ReadLog(plan!.LogPath);
    if (!missingSourceLog.Contains("does not exist", StringComparison.Ordinal))
        throw new InvalidOperationException($"Missing build folder did not produce a readable message.\n{missingSourceLog}");
    if (CountOccurrences(missingSourceLog, "Robocopy attempt") != 0)
        throw new InvalidOperationException("Robocopy ran despite an invalid source folder; the guard should have fired first.");

    // The exit>=16 short-circuit cannot be reached through the guards, so it is asserted
    // statically: a structural robocopy failure must break out rather than retry 5 times.
    foreach (var required in new[] { "if ($exitCode -ge 16) { break }", "if ($exitCode -lt 8) { break }" })
        if (!firstScript.Contains(required, StringComparison.Ordinal))
            throw new InvalidOperationException($"Generated script is missing the retry guard '{required}'.");

    // waitForAppExit: the copy must not start until the referenced process has exited.
    // The plan normally records this process's own id, which would deadlock the test, so
    // the record's init-only properties let us point it at a short-lived helper instead.
    var helper = StartHelperProcess();
    var waited = Stopwatch.StartNew();
    var waitScript = LocalUpdater.CreatePowerShellScript(
        plan! with { AppProcessId = helper.Id },
        waitForAppExit: true,
        relaunchApp: false,
        showErrorDialog: false);
    var waitRun = RunScript(waitScript, plan!.LogPath);
    waited.Stop();
    if (waitRun.ExitCode != 0)
        throw new InvalidOperationException(
            $"waitForAppExit run failed ({waitRun.ExitCode}).\n{waitRun.StandardError}\n{waitRun.Output}\n{ReadLog(plan!.LogPath)}");
    if (waited.ElapsedMilliseconds < HelperLifetimeMilliseconds - 400)
        throw new InvalidOperationException(
            $"Updater copied after {waited.ElapsedMilliseconds}ms without waiting for the helper process " +
            $"(~{HelperLifetimeMilliseconds}ms lifetime). waitForAppExit was not honoured.");

    Console.WriteLine("Updater smoke test passed: validates build folders, guards missing paths with readable errors, waits for app exit, copies updated and nested files, and preserves existing files.");
    return 0;
}
finally
{
    try
    {
        Directory.Delete(temporaryRoot, recursive: true);
    }
    catch
    {
    }
}

static void AssertFileContains(string path, string expected)
{
    if (!File.Exists(path) || File.ReadAllText(path) != expected)
        throw new InvalidOperationException($"Expected '{expected}' in '{path}'.");
}

static string ReadLog(string logPath) => File.Exists(logPath) ? File.ReadAllText(logPath) : "No updater log was written.";

static int CountOccurrences(string haystack, string needle)
{
    var count = 0;
    var index = haystack.IndexOf(needle, StringComparison.Ordinal);
    while (index >= 0)
    {
        count++;
        index = haystack.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
    }
    return count;
}

static Process StartHelperProcess()
{
    var startInfo = new ProcessStartInfo
    {
        FileName = "powershell.exe",
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };
    startInfo.ArgumentList.Add("-NoProfile");
    startInfo.ArgumentList.Add("-Command");
    startInfo.ArgumentList.Add($"Start-Sleep -Milliseconds {HelperLifetimeMilliseconds}");
    return Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the helper process.");
}

static (int ExitCode, string Output, string StandardError) RunScript(string script, string logPath)
{
    // The log lives at a fixed path in temp and is appended to, so clear it to keep each
    // scenario's output attributable to that scenario.
    try { File.Delete(logPath); } catch { }

    var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
    var startInfo = new ProcessStartInfo
    {
        FileName = "powershell.exe",
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true
    };
    startInfo.ArgumentList.Add("-NoProfile");
    startInfo.ArgumentList.Add("-ExecutionPolicy");
    startInfo.ArgumentList.Add("Bypass");
    startInfo.ArgumentList.Add("-EncodedCommand");
    startInfo.ArgumentList.Add(encodedScript);

    using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start PowerShell.");
    var outputTask = process.StandardOutput.ReadToEndAsync();
    var errorTask = process.StandardError.ReadToEndAsync();
    if (!process.WaitForExit(60000))
    {
        process.Kill(true);
        throw new TimeoutException("Updater copy script exceeded 60 seconds.");
    }

    return (process.ExitCode, outputTask.GetAwaiter().GetResult(), errorTask.GetAwaiter().GetResult());
}