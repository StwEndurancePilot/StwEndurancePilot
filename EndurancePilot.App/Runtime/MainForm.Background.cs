using System.Runtime.InteropServices;
using EndurancePilot.Core;

namespace EndurancePilot.App;

// Background endurance: while waves are simply running, Pilot no longer forces
// Fortnite back to the foreground. It keeps reading waves through background capture and
// only takes focus back when an action is needed (result screen, error, sleep prompt,
// target wave exit...). Once the next endurance is running again, it returns to the
// window the user was using.
public sealed partial class MainForm
{
    private static readonly TimeSpan BackgroundStaleWave = TimeSpan.FromMinutes(12);
    private static readonly TimeSpan BackgroundIdleBeforeFocus = TimeSpan.FromSeconds(2);
    private nint backgroundReturnWindow;
    private bool userLeftDuringEndurance;

    // True while an endurance is running and nothing needs input from Pilot.
    private bool PassiveEndurance()
    {
        if (!runner.IsRunning || runner.Mode != RunnerMode.Runner) return false;
        if (controller.Status != ControllerStatus.Observing || controller.Pending is not null || controller.TargetExitRequested) return false;
        if (tracker.Current is not { Outcome: RunOutcome.InProgress }) return false;
        // Safety net: if no wave was confirmed for a long time, take the game back to look.
        if (DateTimeOffset.UtcNow - tracker.LastConfirmedWaveAt > BackgroundStaleWave) return false;
        return lastScreen is ScreenKind.ActiveEndurance or ScreenKind.EnduranceCountdown or ScreenKind.Unknown;
    }

    private static bool UserIdleFor(TimeSpan span)
    {
        var info = new BackgroundLastInput { Size = (uint)Marshal.SizeOf<BackgroundLastInput>() };
        if (!BackgroundGetLastInputInfo(ref info)) return false;
        return unchecked((uint)Environment.TickCount - info.Tick) >= (uint)span.TotalMilliseconds;
    }

    // Called just before Pilot takes focus back from the user.
    private void RememberReturnWindow(GameWindow game)
    {
        if (!userLeftDuringEndurance) return;
        var current = BackgroundGetForegroundWindow();
        if (current != 0 && current != game.Handle) backgroundReturnWindow = current;
    }

    // Called on foreground frames: once the next endurance runs again, give the PC back.
    private async Task<bool> ReturnToUserWindowAsync(GameWindow game)
    {
        if (backgroundReturnWindow == 0 || lastScreen != ScreenKind.ActiveEndurance || !PassiveEndurance()) return false;
        var target = backgroundReturnWindow;
        backgroundReturnWindow = 0;
        if (!BackgroundIsWindow(target) || !BackgroundIsWindowVisible(target)) return false;
        bool returned;
        busy = true;
        try { returned = await GameInput.FocusWindowAsync(target, closing.Token); }
        finally { busy = false; }
        if (returned)
        {
            AddActivity("Endurance running · returned to your previous window");
            log.Write("background-return", new { Wave = tracker.Current?.HighestWave });
        }
        return returned;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BackgroundLastInput
    {
        public uint Size; public uint Tick;
    }
    [DllImport("user32.dll", EntryPoint = "GetLastInputInfo")] private static extern bool BackgroundGetLastInputInfo(ref BackgroundLastInput info);
    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")] private static extern nint BackgroundGetForegroundWindow();
    [DllImport("user32.dll", EntryPoint = "IsWindow")] private static extern bool BackgroundIsWindow(nint window);
    [DllImport("user32.dll", EntryPoint = "IsWindowVisible")] private static extern bool BackgroundIsWindowVisible(nint window);
}
