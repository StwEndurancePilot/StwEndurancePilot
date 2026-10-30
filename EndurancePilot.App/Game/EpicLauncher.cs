using System.Diagnostics;
using EndurancePilot.Core;

namespace EndurancePilot.App;

internal static class EpicLauncher
{
    public const string LaunchUri = "com.epicgames.launcher://apps/Fortnite?action=launch&silent=true";
    public static EpicAccounts Accounts { get; } = new();
    private static string? launchedAccount;
    private static DateTimeOffset launchedAt;
    internal static string ProgressAccount
    {
        get
        {
            // A selected account may differ from the game that is already open.
            bool known = false;
            foreach (var process in Process.GetProcessesByName("FortniteClient-Win64-Shipping"))
            using (process)
            {
                try { known |= launchedAccount is not null && process.StartTime.ToUniversalTime() >= launchedAt.UtcDateTime
                    && process.StartTime.ToUniversalTime() <= launchedAt.UtcDateTime.AddMinutes(2); }
                catch { }
            }
            string name = known ? launchedAccount! : Accounts.Selected?.DisplayName ?? "No account selected";
            name = new string(name.Where(c => !char.IsControl(c) && c != '`').Take(80).ToArray());
            return known ? name + " (launched by Pilot)" : name + " (selected · game login unverified)";
        }
    }
    private static readonly SemaphoreSlim LaunchGate = new(1, 1);
    private static readonly string PathFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EndurancePilot", "fortnite-path.txt");

    public static GameProcessSnapshot Inspect()
    {
        bool game = AnyProcess("FortniteClient-Win64-Shipping") || AnyProcess("FortniteClient-Win64-Shipping_EAC_EOS")
            || AnyProcess("FortniteClient-Win64-Shipping_BE");
        bool boot = AnyProcess("FortniteLauncher") || AnyProcess("FortniteBootstrapper");
        return new(DateTimeOffset.UtcNow, game, boot, GameWindow.Find() is not null);
    }

    public static async Task LaunchAsync(CancellationToken token = default)
    {
        if (!await LaunchGate.WaitAsync(0, token))
            throw new InvalidOperationException("A Fortnite launch is already in progress.");
        try
        {
        var current = Inspect();
        if (current.GamePresent || current.BootstrapperPresent)
        {
            throw new InvalidOperationException("Fortnite is already running or starting. No second launch was sent.");
        }

        token.ThrowIfCancellationRequested();
        var account = Accounts.Selected;
        if (account is null)
        {
            Process.Start(new ProcessStartInfo(LaunchUri) { UseShellExecute = true });
            return;
        }

        string executable = FindFortniteLauncher() ?? throw new FileNotFoundException(
            "FortniteLauncher.exe was not found. Check the Fortnite installation in Epic Games Launcher.");
        using var auth = new EpicAuthentication();
        string code = await auth.LaunchExchangeCodeAsync(account, token);
        if (Inspect() is { GamePresent: true } or { BootstrapperPresent: true })
            throw new InvalidOperationException("Fortnite started while the account was being authenticated. No second launch was sent.");
        token.ThrowIfCancellationRequested();
        string[] args = ["-AUTH_LOGIN=unused", $"-AUTH_PASSWORD={code}", "-AUTH_TYPE=exchangecode",
            "-epicapp=Fortnite", "-epicenv=Prod", "-EpicPortal",
            $"-epicusername={account.DisplayName}", $"-epicuserid={account.AccountId}"];
        var start = new ProcessStartInfo(executable) { WorkingDirectory = Path.GetDirectoryName(executable)!, UseShellExecute = false };
        foreach (string arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Windows did not start FortniteLauncher.exe.");
        launchedAccount = account.DisplayName;
        launchedAt = DateTimeOffset.UtcNow;
        }
        finally { LaunchGate.Release(); }
    }

    internal static async Task StopGameAsync(CancellationToken token, Func<bool>? canContinue = null)
    {
        await LaunchGate.WaitAsync(token);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            int session = Process.GetCurrentProcess().SessionId;
            // Exact Fortnite processes in this Windows session only. Never terminate
            // Epic Games Launcher, anti-cheat services, or unrelated child processes.
            foreach (string name in new[] { "FortniteLauncher", "FortniteBootstrapper", "FortniteClient-Win64-Shipping", "FortniteClient-Win64-Shipping_EAC_EOS", "FortniteClient-Win64-Shipping_BE" })
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    using (process)
                    {
                        token.ThrowIfCancellationRequested();
                        if (canContinue?.Invoke() == false)
                            throw new GameObservationChangedException("Startup recovery stopped because game controls are unavailable.");
                        try
                        {
                            if (process.HasExited || process.SessionId != session) continue;
                            process.Kill();
                            await process.WaitForExitAsync(timeout.Token);
                        }
                        catch (InvalidOperationException) when (process.HasExited) { }
                    }
                }
            }
        }
        finally { LaunchGate.Release(); }
    }

    public static string? CustomInstallPath => File.Exists(PathFile) ? File.ReadAllText(PathFile).Trim() : null;

    public static void SetInstallPath(string path)
    {
        string? executable = ResolveExecutable(path);
        if (executable is null) throw new FileNotFoundException("Choose a Fortnite folder containing FortniteLauncher.exe.");
        Directory.CreateDirectory(Path.GetDirectoryName(PathFile)!);
        File.WriteAllText(PathFile, path);
    }

    private static string? FindFortniteLauncher()
    {
        if (CustomInstallPath is { } chosen && ResolveExecutable(chosen) is { } found) return found;
        string manifest = @"C:\ProgramData\Epic\UnrealEngineLauncher\LauncherInstalled.dat";
        if (File.Exists(manifest))
        {
            try
            {
                using var data = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifest));
                foreach (var item in data.RootElement.GetProperty("InstallationList").EnumerateArray())
                    if (item.TryGetProperty("AppName", out var name) && name.GetString() == "Fortnite" &&
                        item.TryGetProperty("InstallLocation", out var location) && ResolveExecutable(location.GetString() ?? "") is { } installed)
                        return installed;
            }
            catch (Exception error) when (error is IOException or System.Text.Json.JsonException or KeyNotFoundException or UnauthorizedAccessException) { }
        }
        return ResolveExecutable(@"C:\Program Files\Epic Games\Fortnite");
    }

    private static string? ResolveExecutable(string folder)
    {
        string direct = Path.Combine(folder, "FortniteLauncher.exe");
        string nested = Path.Combine(folder, "FortniteGame", "Binaries", "Win64", "FortniteLauncher.exe");
        return File.Exists(direct) ? direct : File.Exists(nested) ? nested : null;
    }


    private static bool AnyProcess(string name)
    {
        var processes = Process.GetProcessesByName(name);
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }
}
