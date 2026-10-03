using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ATSRoadTripConverter;

internal static class Program
{
    public const string AppName = "ATS American Roadtrip Car Patcher";
    public const string AppVersion = "v1.3.8";

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
