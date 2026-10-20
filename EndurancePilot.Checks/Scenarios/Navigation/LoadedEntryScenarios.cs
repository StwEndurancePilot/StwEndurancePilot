using EndurancePilot.Core;

internal static class LoadedEntryScenarios
{
    internal static void Run(Action<string, bool> check, DateTimeOffset epoch)
    {
        var recognizer = new ScreenRecognizer();
        var profile = new RunnerProfile(HomebaseZone.TwinePeaks, true);
        Observation At(double seconds, params string[] text) => new(epoch.AddSeconds(seconds), 1920, 1080,
            text.Select((label, index) => new TextRegion(label, new(100, 100 + index * 45, 350, 30))).ToArray());
        var loaded = At(200, "Storm Shield", "Activate Storm Shield to Defend");
        foreach (bool choice in new[] { true, false })
        {
            var source = choice
                ? At(0, "COMMUNITY LOOKOUT", "MY STORM SHIELD", "STORM SHIELD ASSIST", "LAUNCH") with
                {
                    Visuals = [new("own-homebase-selected", new(500, 150, 30, 30), 1, 1)]
                }
                : At(0, "HERO LOADOUT", "STORM SHIELD", "LAUNCH");
            var controller = new AutomationController { ErrorRecoveryOnly = false, Profile = profile };
            controller.Start();
            ClickAction? Read(Observation frame) => controller.Observe(frame, recognizer.Recognize(frame));
            Read(source);
            var launch = Read(source with
            {
                CapturedAt = epoch.AddSeconds(.2)
            })!;
            check("Entry test launches " + choice, launch?.Step == (choice ? NavigationStep.LaunchHomebase : NavigationStep.LaunchLoadedHomebase));
            controller.Dispatched(launch!, epoch.AddSeconds(.2));

            Read(source with
            {
                CapturedAt = epoch.AddSeconds(91)
            });
            Read(source with
            {
                CapturedAt = epoch.AddSeconds(95)
            });
            var retry = Read(source with
            {
                CapturedAt = epoch.AddSeconds(95.2)
            })!;
            check("Entry test produces launch retry " + choice, retry?.Step == launch!.Step);
            controller.Dispatched(retry!, epoch.AddSeconds(95.2));
            check("Late loaded frame cannot pause after launch retry " + choice,
                Read(loaded) is null && controller.Status != ControllerStatus.PausedForAttention);
            var tab = Read(loaded with
            {
                CapturedAt = epoch.AddSeconds(200.2)
            });
            check("Loaded shield supersedes launch wait and opens backpack " + choice,
                tab is { Step: NavigationStep.OpenBackpack, Key: NavigationKey.Tab });
        }
    }
}
