using System.Diagnostics;
using System.Text;
using ATSRoadTripConverter;

if (!OperatingSystem.IsWindows())
{
    Console.WriteLine("This updater smoke test requires Windows PowerShell and Robocopy.");
    return 2;
}

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

    var script = LocalUpdater.CreatePowerShellScript(plan!, waitForAppExit: false, relaunchApp: false, showErrorDialog: false);
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
    if (!process.WaitForExit(30000))
    {
        process.Kill(true);
        throw new TimeoutException("Updater copy script exceeded 30 seconds.");
    }

    var output = outputTask.GetAwaiter().GetResult();
    var standardError = errorTask.GetAwaiter().GetResult();
    if (process.ExitCode != 0)
    {
        var log = File.Exists(plan!.LogPath) ? File.ReadAllText(plan.LogPath) : "No updater log was written.";
        throw new InvalidOperationException($"Updater script failed ({process.ExitCode}).\n{standardError}\n{output}\n{log}\nGenerated script:\n{script}");
    }

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

    Console.WriteLine("Updater smoke test passed: validates build folders, copies updated and nested files, and preserves existing files.");
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