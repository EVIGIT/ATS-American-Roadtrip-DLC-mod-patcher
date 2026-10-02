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

internal sealed class SignInForm : Form
{
    private readonly FlatButton _primaryButton = new();
    private readonly FlatButton _secondaryButton = new();
    private readonly FlatButton _kofiButton = new();
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
        if (ThemedIcon.WindowIcon() is { } themedIcon)
            Icon = themedIcon;
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

        // Ko-fi support has no sign-in flow yet. The button is shown in Ko-fi's pink so
        // the second unlock route is discoverable, but it is inert and keeps its colour
        // under every theme.
        _kofiButton.Size = new Size(240, 38);
        _kofiButton.Location = new Point(pad, 418);
        _kofiButton.Text = "Sign in with Ko-fi (soon)";
        _kofiButton.AccentOverride = Theme.KoFiPink;
        _kofiButton.Enabled = false;
        Controls.Add(_kofiButton);

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
        SetStatus($"Signed in as {AuthSession.Login}", "The GitHub themes are unlocked on this device.");
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
                ? $"{Theme.ThemeNamesFor(ThemeAccess.GitHub)} unlock once you sign in."
                : "Sign-in is not configured yet, so the GitHub themes stay locked. Add your GitHub OAuth App client id to enable it.");
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
