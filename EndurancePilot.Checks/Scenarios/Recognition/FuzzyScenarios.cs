using EndurancePilot.Core;

internal static class FuzzyScenarios
{
    public static void Run(Action<string, bool> check, DateTimeOffset epoch)
    {
        var recognizer = new ScreenRecognizer();
        var profile = new RunnerProfile(HomebaseZone.TwinePeaks, true);
        TextRegion Label(string text, double x, double y, double width = 220, double height = 30) => new(text, new(x, y, width, height));
        Observation Frame(params TextRegion[] labels) => new(epoch, 1920, 1080, labels);
        check("Character score handles omitted letters", TextSimilarity.Score("Storm Shied", "Storm Shield") is > .85 and < 1);
        check("Character score ignores spacing", TextSimilarity.Score("StormShield", "Storm Shield") == 1);
        check("Wave digits never become fuzzy replacements", TextSimilarity.Score("Wave 3", "Wave 8") == 0);
        check("Shield level digits remain distinct", TextSimilarity.Score("Storm Shield Level 1", "Storm Shield Level 10") == 0);
        check("Short labels cannot be guessed from one changed letter", TextSimilarity.Find(Frame(Label("NAP", 20, 20)), "MAP").Count == 0);
        var homebase = Frame(Label("Storm Shied", 20, 100), Label("Activat Storm Shield to Defend", 20, 145, 400));
        var evidence = recognizer.Recognize(homebase);
        check("Noisy header and objective can jointly recognize homebase", evidence is { Kind: ScreenKind.Homebase, UsesFuzzyMatching: true, AllowsInput: true, MatchScore: >= .9 });
        check("Fuzzy homebase uses detected objective for Tab", NavigationPlanner.Decide(homebase, evidence, profile).Plan is { Step: NavigationStep.OpenBackpack, Key: NavigationKey.Tab });
        var deactivated = homebase with
        {
            Regions = [homebase.Regions[0], Label("Deactivate Storm Shield to Defend", 20, 145, 400)]
        };
        check("Similar opposite verb cannot authorize activation", recognizer.Recognize(deactivated).Kind != ScreenKind.Homebase);
        var console = Frame(Label("Storm Shied Endurance", 300, 500, 300), Label("Daily Wargmes", 800, 300), Label("Storag", 1200, 200));
        evidence = recognizer.Recognize(console);
        check("Several imperfect console labels can authorize the specific menu", evidence is { Kind: ScreenKind.StormShieldConsole, UsesFuzzyMatching: true, AllowsInput: true });
        check("Fuzzy console uses the fixed Endurance tile", NavigationPlanner.Decide(console, evidence, profile).Plan
            is { Step: NavigationStep.SelectEndurance } consolePlan
            && consolePlan.Target == MenuGeometry.Target(NavigationStep.SelectEndurance, console));
        var duplicate = console with
        {
            Regions = [.. console.Regions, Label("Storm Shied Endurance", 300, 700, 300)]
        };
        check("Two equally plausible Endurance targets block clicking", !recognizer.Recognize(duplicate).AllowsInput);
        var repeats = console with
        {
            Regions = console.Regions.Concat(console.Regions.Select(r => r with { Pass = "another-ocr-pass" })).ToArray()
        };
        check("Repeating OCR evidence cannot increase confidence", recognizer.Recognize(repeats).MatchScore == evidence.MatchScore);
        var weak = Frame(Label("Stom Shied Endurane", 300, 500, 300), Label("Daly Wargmes", 800, 300), Label("Storag", 1200, 200));
        var probable = recognizer.Recognize(weak);
        check("Weak readings report a probable screen without input", probable is { Kind: ScreenKind.Unknown, LikelyKind: ScreenKind.StormShieldConsole, AllowsInput: false });
        check("Probable screen never generates a navigation action", NavigationPlanner.Decide(weak, probable, profile).Plan is null);
        var ready = Frame(Label("Twine Peaks Storm Shied", 30, 150, 350), Label("Hero Loadout", 30, 270), Label("Launck", 1500, 850));
        var readyEvidence = recognizer.Recognize(ready);
        check("Launch typo needs loadout and homebase corroboration", readyEvidence is { Kind: ScreenKind.HomebaseReady, UsesFuzzyMatching: true }
            && NavigationPlanner.Decide(ready, readyEvidence, profile).Plan?.Step == NavigationStep.LaunchLoadedHomebase);
        check("Launch typo alone stays inert", NavigationPlanner.Decide(Frame(ready.Regions[2]), recognizer.Recognize(Frame(ready.Regions[2])), profile).Plan is null);
        var controller = new AutomationController { Profile = profile, ErrorRecoveryOnly = false };
        controller.Start();
        check("One high-score frame does not click", controller.Observe(console, evidence) is null);
        check("Two fresh high-score frames permit the located control", controller.Observe(console with
        {
            CapturedAt = epoch.AddMilliseconds(600)
        }, evidence)?.Step == NavigationStep.SelectEndurance);
        var active = console with
        {
            Regions = [.. console.Regions, Label("Endurance: Wave 8", 800, 100)]
        };

        controller.Start(runInProgress: true);
        controller.Observe(active, recognizer.Recognize(active));
        check("Fuzzy menu cannot restart an active run", controller.Observe(active with { CapturedAt = epoch.AddSeconds(1) }, recognizer.Recognize(active)) is null);
    }
}
