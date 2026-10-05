namespace TruckersToolKit;
internal sealed class ThemedConfirmForm : Form
{
    public ThemedConfirmForm(string title, string message, string acceptText, string cancelText)
        : this(title, message, acceptText, cancelText, showCancel: true)
    {
    }

    /// <param name="showCancel">False renders a single acknowledge button on the right.</param>
    public ThemedConfirmForm(string title, string message, string acceptText, string cancelText, bool showCancel)
    {
        Text = title;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.UiFont(9.5f);
        ClientSize = new Size(520, 244);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        DoubleBuffered = true;

        const int pad = 28;
        const int width = 520 - pad * 2;

        Controls.Add(new InfoDot { Location = new Point(pad, 24) });
        Controls.Add(new Label
        {
            Text = title,
            Font = Theme.UiFont(12f, FontStyle.Bold),
            ForeColor = Theme.Text,
            AutoSize = true,
            Location = new Point(pad + 42, 26)
        });
        Controls.Add(new Label
        {
            Text = message,
            Font = Theme.UiFont(9.5f),
            ForeColor = Theme.Muted,
            AutoSize = false,
            Size = new Size(width - 42, 60),
            Location = new Point(pad + 42, 56)
        });

        var accept = new FlatButton
        {
            Text = acceptText,
            Size = new Size(190, 36),
            Location = showCancel ? new Point(pad, 176) : new Point(520 - pad - 190, 176),
            Primary = true
        };
        accept.Click += (_, _) => Finish(DialogResult.OK);
        Controls.Add(accept);

        if (showCancel)
        {
            var cancel = new FlatButton
            {
                Text = cancelText,
                Size = new Size(150, 36),
                Location = new Point(520 - pad - 150, 176)
            };
            cancel.Click += (_, _) => Finish(DialogResult.Cancel);
            Controls.Add(cancel);
        }

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                Finish(DialogResult.Cancel);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Enter)
            {
                Finish(DialogResult.OK);
                e.Handled = true;
            }
        };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ConverterForm.ApplyDarkTitleBar(this);
    }

    private void Finish(DialogResult result)
    {
        DialogResult = result;
        Close();
    }
}
