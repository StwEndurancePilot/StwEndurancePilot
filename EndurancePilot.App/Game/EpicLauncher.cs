using System.Diagnostics;
using EndurancePilot.Core;

namespace EndurancePilot.App;

internal static class EpicLauncher
{
    public const string LaunchUri = "com.epicgames.launcher://apps/Fortnite?action=launch&silent=true";

    public static GameProcessSnapshot Inspect()
    {
        bool game = AnyProcess("FortniteClient-Win64-Shipping") || AnyProcess("FortniteClient-Win64-Shipping_EAC_EOS")
            || AnyProcess("FortniteClient-Win64-Shipping_BE");
        bool boot = AnyProcess("FortniteLauncher") || AnyProcess("FortniteBootstrapper");
        return new(DateTimeOffset.UtcNow, game, boot, GameWindow.Find() is not null);
    }

    public static void Launch()
    {
        var current = Inspect();
        if (current.GamePresent || current.BootstrapperPresent)
        {
            throw new InvalidOperationException("Fortnite is already running or starting. No second launch was sent.");
        }

        Process.Start(new ProcessStartInfo(LaunchUri) { UseShellExecute = true });
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
