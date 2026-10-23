namespace EndurancePilot.App;

internal static class RemoteChecks
{
    internal static IReadOnlyList<StartupCheck> Run()
    {
        var checks = new List<StartupCheck>();
        void Check(string name, bool passed) => checks.Add(new(name, passed, passed ? "Passed" : "Failed"));
        var configuration = new RemoteConfiguration("https://pilot.example", "111111111111111111", "", false, false,
            "333333333333333333", new string('0', 64), 0, long.MaxValue);
        foreach (var command in new[] { "progress", "statistics", "accounts" })
            Check($"Discord permits paired read command {command}", RemotePermissions.Allows(command, configuration));
        foreach (var command in new[] { "switch", "login", "menu", "start", "stop", "launch" })
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
        return checks;
    }
}
