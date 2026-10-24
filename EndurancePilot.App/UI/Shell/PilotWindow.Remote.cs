namespace EndurancePilot.App;

public partial class PilotWindow
{
    private DiscordRemote? remote;
    private CancellationTokenSource? remoteLogin;
    private string remoteLoginStatus = "No remote sign-in pending.";

    private void InitializeRemote()
    {
        remote = new DiscordRemote((command, config, token) => Dispatcher.InvokeAsync(() => ExecuteRemoteAsync(command, config, token)).Task.Unwrap());
        remote.ConnectionStopped += () => remoteLogin?.Cancel();
        remote.Start();
    }

    private void OpenRemote()
    {
        AccountPopup.IsOpen = false;
        if (remote is null) return;
        var dialog = new DiscordRemoteDialog(remote) { Owner = this };
        dialog.ShowDialog();
    }

    private async Task<RemoteResult> ExecuteRemoteAsync(RemoteCommand command, RemoteConfiguration config, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (command.Command is "progress" or "statistics") return await host.RemoteReadAsync(command.Command, token);
        if (command.Command == "screenshot") return config.AllowScreenshots
            ? await host.RemoteReadAsync("screenshot", token) : new("Screenshots are disabled on this PC. Change permissions by pairing again locally.");
        if (command.Command == "accounts")
        {
            var accounts = EpicLauncher.Accounts.All;
            if (accounts.Count == 0) return new("No saved accounts. " + remoteLoginStatus);
            string text = string.Join("\n", accounts.Select((a, i) => $"{i + 1}. {SafeAccountName(a.DisplayName)}{(EpicLauncher.Accounts.SelectedAccountId == a.AccountId ? " (selected)" : "")}"));
            return new((text.Length > 1700 ? text[..1700] : text) + "\n" + remoteLoginStatus);
        }
        if (!config.AllowControl) return new("Game and account controls are disabled on this PC. Change permissions by pairing again locally.");
        switch (command.Command)
        {
            case "menu":
                Show(); WindowState = System.Windows.WindowState.Normal; Activate();
                return new("Pilot is visible on your PC.");
            case "start":
                if (remoteLogin is not null) return new("Finish account sign-in before starting.");
                if (!host.Snapshot().Running) ToggleFromControls();
                return new(host.Snapshot().Running ? "Runner started using saved settings." : "Runner could not start. Check Pilot on your PC.");
            case "stop":
                if (host.Snapshot().Running) host.ToggleRunner();
                return new("Runner stopped.");
            case "switch":
                if (host.Snapshot().Running || remoteLogin is not null || EpicLauncher.Inspect() is { GamePresent: true } or { BootstrapperPresent: true })
                    return new("Stop the runner, close Fortnite and finish sign-in before switching accounts.");
                if (command.Account is not { } number || number < 1 || number > EpicLauncher.Accounts.All.Count) return new("Account number is unavailable. Run /pilot accounts again.");
                EpicLauncher.Accounts.Select(EpicLauncher.Accounts.All[number - 1].AccountId);
                RefreshAccountPicker();
                return new("Selected account changed. Use /pilot launch when ready.");
            case "launch":
                if (host.Snapshot().Running || remoteLogin is not null) return new("Stop the runner and finish sign-in before launching.");
                await EpicLauncher.LaunchAsync(token);
                return new("Fortnite launch requested.");
            case "login":
                if (host.Snapshot().Running || accountsPane is not null) return new("Stop the runner and close the local accounts pane before remote sign-in.");
                if (remoteLogin is not null) return new("An Epic sign-in is already pending. Approve it or wait for it to expire.");
                using (var auth = new EpicAuthentication())
                {
                    var signIn = await auth.BeginSignInAsync(token);
                    token.ThrowIfCancellationRequested();
                    // Link lifetime follows the connection, not the short command response deadline.
                    remoteLogin = new CancellationTokenSource(TimeSpan.FromMinutes(15));
                    remoteLoginStatus = "Epic sign-in pending.";
                    _ = FinishRemoteLoginAsync(signIn, config, remoteLogin);
                    return new($"Approve this PC on Epic’s website: {signIn.VerificationUri.AbsoluteUri}\nUse /pilot accounts to check completion. Never enter your Epic password in Discord.");
                }
            default: return new("Unsupported command.");
        }
    }

    private async Task FinishRemoteLoginAsync(EpicSignIn signIn, RemoteConfiguration config, CancellationTokenSource source)
    {
        try
        {
            using var auth = new EpicAuthentication();
            var account = await auth.FinishSignInAsync(signIn, source.Token);
            source.Token.ThrowIfCancellationRequested();
            if (remote?.Configuration != config || remote.Status != "Connected") throw new OperationCanceledException();
            if (host.Snapshot().Running) throw new InvalidOperationException();
            EpicLauncher.Accounts.Add(account);
            RefreshAccountPicker();
            remoteLoginStatus = "Epic account connected.";
        }
        catch { remoteLoginStatus = "Epic sign-in ended without adding an account. Start again if needed."; }
        finally { remoteLogin = null; source.Dispose(); }
    }

    private static string SafeAccountName(string name) => new(name.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_').Take(80).ToArray());
}
