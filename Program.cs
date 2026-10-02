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


/// <summary>
/// GitHub OAuth device flow, the only flow a desktop app can run without shipping a
/// client secret. The user gets a short code, approves it in a browser, and the app
/// polls until the token arrives.
/// </summary>

internal static class Program
{
    public const string AppName = "ATS American Roadtrip Car Patcher";
    public const string AppVersion = "v1.3.7.1";

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









/// <summary>Rounded, accent-tinted pill used for compact labels such as the version tag.</summary>

/// <summary>
/// Builds the app logo and window icon tinted to the active palette so the header,
/// title bar and taskbar follow the selected theme. Results are cached per accent so
/// the GDI handles stay alive for the lifetime of the process.
/// </summary>

/// <summary>
/// Startup sign-in window. It never blocks the app: skipping simply leaves the
/// supporter themes locked, and the window can be reopened from Settings.
/// </summary>

/// <summary>Small circular info badge used by the themed confirm dialog.</summary>

/// <summary>
/// Themed replacement for MessageBox, so in-app prompts follow the user's chosen
/// palette instead of the system look.
/// </summary>
