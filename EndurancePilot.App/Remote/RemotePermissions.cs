namespace EndurancePilot.App;

internal static class RemotePermissions
{
    // Enforced before dispatch even if the configured Worker sends an unexpected command.
    internal static bool Allows(string command, RemoteConfiguration configuration) => command switch
    {
        "progress" or "statistics" or "accounts" => true,
        "screenshot" => configuration.AllowScreenshots,
        "switch" or "login" or "menu" or "start" or "stop" or "launch" or "kill" or "restart" => configuration.AllowControl,
        _ => false
    };
}
