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

    private void DiscordClicked(object sender, System.Windows.RoutedEventArgs e) => OpenRemote();

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
            return AccountMenu(config.AllowControl);
        }
        if (!config.AllowControl) return new("Game and account controls are disabled on this PC. Change permissions by pairing again locally.");
        switch (command.Command)
        {
            case "menu":
                Show(); WindowState = System.Windows.WindowState.Normal; Activate();
                return new("Pilot is visible on your PC.");
            case "start":
                if (remoteLogin is not null) return new("Finish account sign-in before starting.");
                if (host.Snapshot().Running) return new("Runner is already running.");
                AccountPopup.IsOpen = false;
                if (GameWindow.Find() is { } startGame && !await GameInput.FocusForAutomationAsync(startGame, token))
                    return new("Runner could not start because Windows did not let Pilot focus Fortnite. Unlock your PC, release any held keys and try /pilot start again.");
                token.ThrowIfCancellationRequested();
                if (remoteLogin is not null) return new("Finish account sign-in before starting.");
                if (!host.Snapshot().Running) ToggleFromControls();
                return new(host.Snapshot().Running ? "Runner started using saved settings." : "Runner could not start. Check Pilot on your PC.");
            case "stop":
                if (host.Snapshot().Running) host.ToggleRunner();
                return new("Runner stopped.");
            case "switch":
                var choices = EpicLauncher.Accounts.All.Take(100).ToArray();
                string accountSet = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
                    System.Text.Json.JsonSerializer.Serialize(choices.Select(a => a.AccountId)))));
                if (command.Account is null) return AccountMenu(true);
                if (command.AccountSet is not null && command.AccountSet != accountSet)
                    return new("Your saved accounts have changed. Run /pilot accounts again for a fresh list.");
                if (host.Snapshot().Running)
                    return new("Stop the runner with /pilot stop before changing the account for the next launch.");
                if (command.Account is not { } number || number < 1 || number > EpicLauncher.Accounts.All.Count) return new("Account number is unavailable. Run /pilot accounts again.");
                // Selecting a saved account supersedes an unfinished remote sign-in.
                // Its completion must not later overwrite this explicit selection.
                if (remoteLogin is { } pendingLogin)
                {
                    remoteLogin = null;
                    pendingLogin.Cancel();
                    remoteLoginStatus = "";
                }
                EpicLauncher.Accounts.Select(EpicLauncher.Accounts.All[number - 1].AccountId);
                RefreshAccountPicker();
                return new("Selected account changed for your next Fortnite launch. Any game already open keeps its current account.");
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

    private RemoteResult AccountMenu(bool allowControl)
    {
        var accounts = EpicLauncher.Accounts.All.Take(100).ToArray();
        if (accounts.Length == 0) return new("No saved accounts yet. Use /pilot login to connect one.");
        var choices = accounts.Select((a, i) => new RemoteAccountChoice(i + 1,
            string.IsNullOrWhiteSpace(SafeAccountName(a.DisplayName)) ? $"Account {i + 1}" : SafeAccountName(a.DisplayName),
            EpicLauncher.Accounts.SelectedAccountId == a.AccountId)).ToArray();
        string snapshot = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            System.Text.Json.JsonSerializer.Serialize(accounts.Select(a => a.AccountId)))));
        string content = allowControl ? "Choose a saved account below to use for your next Fortnite launch." : "Account switching is disabled for this pairing.";
        if (remoteLoginStatus != "No remote sign-in pending.") content += "\n\n" + remoteLoginStatus;
        return new(content, Accounts: choices, AccountSet: allowControl ? snapshot : null);
    }
    private async Task FinishRemoteLoginAsync(EpicSignIn signIn, RemoteConfiguration config, CancellationTokenSource source)
    {
        try
        {
            using var auth = new EpicAuthentication();
            var account = await auth.FinishSignInAsync(signIn, source.Token);
            source.Token.ThrowIfCancellationRequested();
            if (remoteLogin != source || remote?.Configuration != config || remote.Status != "Connected") throw new OperationCanceledException();
            if (host.Snapshot().Running) throw new InvalidOperationException();
            EpicLauncher.Accounts.Add(account);
            RefreshAccountPicker();
            remoteLoginStatus = "Epic account connected.";
        }
        catch { if (remoteLogin == source) remoteLoginStatus = "Epic sign-in ended without adding an account. Start again if needed."; }
        finally { if (remoteLogin == source) remoteLogin = null; source.Dispose(); }
    }

    private static string SafeAccountName(string name) => new(name.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_').Take(80).ToArray());
}
