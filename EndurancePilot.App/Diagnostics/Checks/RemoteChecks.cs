using EndurancePilot.Core;

namespace EndurancePilot.App;

internal static class RemoteChecks
{
    internal static IReadOnlyList<StartupCheck> Run()
    {
        var checks = new List<StartupCheck>();
        void Check(string name, bool passed) => checks.Add(new(name, passed, passed ? "Passed" : "Failed"));
        Observation WaveFrame(params string[] lines) => new(DateTimeOffset.UtcNow, 1280, 720,
            lines.Select((line, index) => new TextRegion(line, new Box(10, 10 + index * 40, 300, 30))).ToArray());
        Check("Remote progress reads the visible wave", MainForm.RemoteWave(WaveFrame("WAVE 12"), ScreenKind.ActiveEndurance) == "12 / 30");
        Check("Remote progress labels upcoming wave", MainForm.RemoteWave(WaveFrame("DEFENSE WAVE 13 STARTS"), ScreenKind.EnduranceCountdown) == "Wave 13 starts next");
        Check("Remote progress rejects conflicting wave readings", MainForm.RemoteWave(WaveFrame("WAVE 12", "WAVE 17"), ScreenKind.ActiveEndurance) is null);
        Check("Remote progress ignores lobby wave text", MainForm.RemoteWave(WaveFrame("WAVE 12"), ScreenKind.SaveTheWorldLobby) is null);
        string pairCode = "EP1.111111111111111111." + new string('a', 32);
        Check("Pairing code carries the user and full random secret", RemotePairingCode.TryParse("  " + pairCode + "\r\n", out var pairUser, out var pairSecret)
            && pairUser == "111111111111111111" && pairSecret == new string('a', 32));
        foreach (var invalid in new[] { "", new string('a', 32), pairCode + ".extra", pairCode.Replace("EP1", "EP2"),
            pairCode.Replace("111111111111111111", "../other"), pairCode[..^1], pairCode + "\nignored", new string('a', 129) })
            Check("Malformed pairing code is rejected", !RemotePairingCode.TryParse(invalid, out _, out _));
        var configuration = new RemoteConfiguration("https://pilot.example", "111111111111111111", "", false, false,
            "333333333333333333", new string('0', 64), 0, long.MaxValue);
        foreach (var command in new[] { "progress", "statistics", "accounts" })
            Check($"Discord permits paired read command {command}", RemotePermissions.Allows(command, configuration));
        foreach (var command in new[] { "switch", "login", "menu", "start", "stop", "launch", "kill", "restart" })
        {
            Check($"Discord denies {command} without local control permission", !RemotePermissions.Allows(command, configuration));
            Check($"Discord permits {command} with local control permission", RemotePermissions.Allows(command, configuration with { AllowControl = true }));
        }
        Check("Discord control permission does not grant screenshots", !RemotePermissions.Allows("screenshot", configuration with { AllowControl = true }));
        Check("Discord screenshots require their own permission", RemotePermissions.Allows("screenshot", configuration with { AllowScreenshots = true }));
        Check("Discord screenshot permission does not grant account changes", !RemotePermissions.Allows("login", configuration with { AllowScreenshots = true }));
        foreach (var command in new[] { "shell", "read-file", "open-url", "", "START" })
            Check($"Discord rejects unknown action '{command}'", !RemotePermissions.Allows(command, configuration with { AllowControl = true, AllowScreenshots = true }));
        foreach (var origin in new[] { "http://pilot.example", "https://user:password@pilot.example", "https://pilot.example/path", "https://pilot.example?token=x", "https://pilot.example#fragment", "https://pilot.example:8443" })
        {
            bool rejected = false;
            try { DiscordRemote.Validate(origin, configuration.UserId); }
            catch (InvalidDataException) { rejected = true; }
            Check("Discord rejects unsafe or non-origin endpoint " + origin.Split('?')[0], rejected);
        }
        Check("Discord accepts HTTPS Worker origin", DiscordRemote.Validate(configuration.Origin, configuration.UserId).Host == "pilot.example");
        bool invalidUser = false;
        try { DiscordRemote.Validate(configuration.Origin, "../another-user"); }
        catch (InvalidDataException) { invalidUser = true; }
        Check("Discord user ID cannot alter request routing", invalidUser);
        var publicSettings = new DiscordPublicSettings(configuration.Origin, configuration.ApplicationId, configuration.DiscordPublicKey);
        Check("Discord build embeds a valid public service identity", DiscordPublicSettings.Load() is not null);
        Check("Discord accepts valid bundled public identity", DiscordPublicSettings.Parse(publicSettings) is not null);
        Check("Discord rejects bundled identity without a public key", DiscordPublicSettings.Parse(publicSettings with { PublicKey = "" }) is null);
        Check("Discord rejects malformed bundled public key", DiscordPublicSettings.Parse(publicSettings with { PublicKey = "not-a-key" }) is null);
        Check("Discord rejects insecure bundled endpoint", DiscordPublicSettings.Parse(publicSettings with { WorkerOrigin = "http://pilot.example" }) is null);
        Check("Discord rejects invalid bundled application ID", DiscordPublicSettings.Parse(publicSettings with { ApplicationId = "../user" }) is null);
        return checks;
    }
}
