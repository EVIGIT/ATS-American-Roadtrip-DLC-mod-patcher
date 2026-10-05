
namespace TruckersToolKit;

internal static class Program
{
    public const string AppName = "Truckers Tool Kit";
    public const string AppVersion = "v1.4";

    [STAThread]
    static void Main()
    {
        SettingsManager.Load();
        ApplicationConfiguration.Initialize();

        ShowLaunchPage();

        Application.Run(new ConverterForm());
    }

    /// <summary>
    /// Shows the launch page: a quick start on a first run, otherwise the release notes for
    /// everything newer than the version last launched.
    /// <para>
    /// The page is best-effort. Any failure here is swallowed, because a launch page is a
    /// convenience and must never stop the converter from opening.
    /// </para>
    /// </summary>
    private static void ShowLaunchPage()
    {
        if (!SettingsManager.Current.ShowLaunchPage)
            return;

        var firstRun = string.IsNullOrWhiteSpace(SettingsManager.Current.LastLaunchedVersion);

        try
        {
            var changelog = LaunchForm.ReadEmbeddedChangelog();
            var changes = firstRun
                ? Array.Empty<ReleaseSection>()
                : ReleaseNotes.SectionsNewerThan(
                    changelog,
                    SettingsManager.Current.LastLaunchedVersion,
                    AppVersion);

            using var launch = new LaunchForm(firstRun, changes);
            launch.ShowDialog();
        }
        catch
        {
            // Fall through to the converter.
        }
        finally
        {
            // Recorded even if the page failed, so a broken changelog cannot trap the user in
            // a loop of failing dialogs.
            SettingsManager.Current.LastLaunchedVersion = AppVersion;
            SettingsManager.Save();
        }
    }
}
