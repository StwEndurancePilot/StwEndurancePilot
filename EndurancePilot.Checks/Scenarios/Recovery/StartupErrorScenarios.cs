using EndurancePilot.Core;

internal static class StartupErrorScenarios
{
    public static void Run(Action<string, bool> check, DateTimeOffset epoch)
    {
        var frame = new Observation(epoch, 2048, 1114, [
            new("ERROR", new(299, 275, 140, 35)),
            new("Fortnite was not started correctly and needs to be closed. We recommend trying to launch the game using the Epic Games", new(300, 375, 1440, 30)),
            new("Launcher or contact player support.", new(300, 417, 600, 30)),
            new("CONFIRM", new(960, 795, 140, 30))]);
        check("Specific startup error matches wrapped message", StartupErrorRecovery.Matches(frame));
        check("Startup error cannot trigger navigation clicks", new ScreenRecognizer().Recognize(frame).Kind == ScreenKind.NeedsAttention);
        check("Generic error cannot restart Fortnite", !StartupErrorRecovery.Matches(frame with { Regions = [frame.Regions[0], frame.Regions[3]] }));
        check("Startup text without dialog controls is rejected", !StartupErrorRecovery.Matches(frame with { Regions = frame.Regions.Skip(1).ToArray() }));
        var recovery = new StartupErrorRecovery();
        check("First startup error frame waits", !recovery.Observe(frame, 1));
        check("Duplicate captured frame cannot confirm restart", !recovery.Observe(frame, 1));
        check("Different game process requires new confirmation", !recovery.Observe(frame with { CapturedAt = epoch.AddSeconds(1) }, 2));
        check("Stable startup error requests restart", recovery.Observe(frame with { CapturedAt = epoch.AddSeconds(2) }, 2));
        for (int i = 1; i < 3; i++)
        {
            recovery.Observe(frame with { CapturedAt = epoch.AddSeconds(i * 10) }, i + 2);
            check("Confirmed startup error can retry within budget", recovery.Observe(frame with { CapturedAt = epoch.AddSeconds(i * 10 + 1) }, i + 2));
        }
        recovery.Observe(frame with { CapturedAt = epoch.AddSeconds(40) }, 5);
        check("Fourth restart is blocked", !recovery.Observe(frame with { CapturedAt = epoch.AddSeconds(41) }, 5) && recovery.Exhausted);
        recovery.Observe(frame with { CapturedAt = epoch.AddMinutes(31) }, 5);
        check("Restart budget recovers after cooldown", recovery.Observe(frame with { CapturedAt = epoch.AddMinutes(31).AddSeconds(1) }, 5) && !recovery.Exhausted);
    }
}
