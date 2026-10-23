namespace EndurancePilot.App;

internal sealed class DiscordRemoteDialog : Form
{
    internal DiscordRemoteDialog(DiscordRemote remote)
    {
        Text = "Discord remote control";
        Width = 530; Height = 720;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(20), AutoScroll = true };
        Controls.Add(panel);
        FlowLayoutPanel target = panel;
        void Label(string text) => target.Controls.Add(new Label { Text = text, Width = 465, AutoSize = true, MaximumSize = new Size(465, 0), Margin = new Padding(0, 6, 0, 6) });
        TextBox Input(string label, string text = "", bool secret = false)
        {
            Label(label);
            var box = new TextBox { Width = 455, Text = text, UseSystemPasswordChar = secret };
            target.Controls.Add(box); return box;
        }
        var defaults = DiscordPublicSettings.Load();
        Label("Run /pilot pair in Discord, then enter your user ID and pairing code here. The service operator and Discord can see replies and requested screenshots.");
        var user = Input("Your Discord user ID", remote.Configuration?.UserId ?? "");
        var advanced = new CheckBox { Text = "Advanced: use a self-hosted Discord application", Width = 465, Height = 30 };
        var details = new FlowLayoutPanel { Width = 470, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
        panel.Controls.Add(advanced);
        panel.Controls.Add(details);
        target = details;
        var origin = Input("Worker address (https://…)", remote.Configuration?.Origin ?? defaults?.WorkerOrigin ?? "");
        Label("Get the application ID and public verification key from the publisher's trusted setup guide (or your Discord Developer Portal). These values are public, not tokens.");
        var application = Input("Discord application ID", remote.Configuration?.ApplicationId ?? defaults?.ApplicationId ?? "");
        var publicKey = Input("Discord public verification key (64 hex characters)", remote.Configuration?.DiscordPublicKey ?? defaults?.PublicKey ?? "");
        target = panel;
        advanced.Checked = defaults is null || (remote.Configuration is { } saved &&
            (saved.Origin != defaults.WorkerOrigin || saved.ApplicationId != defaults.ApplicationId || !saved.DiscordPublicKey.Equals(defaults.PublicKey, StringComparison.OrdinalIgnoreCase)));
        details.Visible = advanced.Checked;
        advanced.Enabled = defaults is not null && remote.Configuration is null;
        advanced.CheckedChanged += (_, _) =>
        {
            details.Visible = advanced.Checked;
            if (!advanced.Checked && defaults is not null)
            {
                origin.Text = defaults.WorkerOrigin;
                application.Text = defaults.ApplicationId;
                publicKey.Text = defaults.PublicKey;
            }
        };
        if (defaults is null) Label("This build has no complete public service configuration. Enter trusted deployment details above.");
        var code = Input("One-time pairing code", secret: true);
        var control = new CheckBox { Text = "Allow account sign-in, switching, launch and runner controls", Width = 465, Height = 30 };
        var screenshots = new CheckBox { Text = "Allow Fortnite screenshots (may include player names/chat)", Width = 465, Height = 30 };
        control.Checked = remote.Configuration?.AllowControl ?? false;
        screenshots.Checked = remote.Configuration?.AllowScreenshots ?? false;
        control.Enabled = screenshots.Enabled = remote.Configuration is null;
        panel.Controls.Add(control); panel.Controls.Add(screenshots);
        Label("Progress, statistics and saved account display names are shared when requested. No passwords, device secrets, file paths or desktop captures are sent. Disconnect and pair again to change permissions.");
        var pair = new Button { Text = "Pair this PC", Width = 150, Height = 32, Enabled = remote.Configuration is null };
        var disconnect = new Button { Text = "Disconnect and revoke", Width = 200, Height = 32 };
        var status = new Label { Text = remote.Status, Width = 465, Height = 55 };
        var lastCommand = new Label { Text = remote.LastCommandStatus, Width = 465, Height = 80 };
        var statusTimer = new System.Windows.Forms.Timer { Interval = 500 };
        statusTimer.Tick += (_, _) =>
        {
            if (remote.Configuration is not null && disconnect.Enabled) status.Text = remote.Status;
            lastCommand.Text = remote.LastCommandStatus;
        };
        statusTimer.Start();
        var dialogLifetime = new CancellationTokenSource();
        FormClosed += (_, _) => { dialogLifetime.Cancel(); statusTimer.Dispose(); };
        panel.Controls.Add(pair); panel.Controls.Add(disconnect); panel.Controls.Add(status); panel.Controls.Add(lastCommand);
        pair.Click += async (_, _) =>
        {
            pair.Enabled = disconnect.Enabled = advanced.Enabled = false;
            try { await remote.PairAsync(origin.Text, user.Text, code.Text, control.Checked, screenshots.Checked, application.Text, publicKey.Text, dialogLifetime.Token); code.Clear(); status.Text = "Paired for 90 days. You can close this window."; }
            catch { status.Text = "Pairing failed. Check the address and user ID, then get a fresh code from Discord."; }
            finally { pair.Enabled = control.Enabled = screenshots.Enabled = remote.Configuration is null; advanced.Enabled = defaults is not null && remote.Configuration is null; disconnect.Enabled = true; }
        };
        disconnect.Click += async (_, _) =>
        {
            pair.Enabled = disconnect.Enabled = advanced.Enabled = false;
            try { await remote.DisconnectAsync(); status.Text = remote.Status; }
            catch { status.Text = "Could not remove saved settings. Use /pilot unlink and close Pilot to revoke access."; }
            finally { pair.Enabled = control.Enabled = screenshots.Enabled = remote.Configuration is null; advanced.Enabled = defaults is not null && remote.Configuration is null; disconnect.Enabled = true; }
        };
    }
}
