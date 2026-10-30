using EndurancePilot.Core;

internal static class DetectedHomebaseScenarios
{
    internal static void Run(Action<string, bool> check, DateTimeOffset epoch)
    {
        var recognizer = new ScreenRecognizer();
        var profile = new RunnerProfile(HomebaseZone.TwinePeaks, true);
        foreach (var (width, height) in new[] { (1680, 1050), (1920, 1080), (2560, 1440), (3440, 1440) })
        {
            var icon = new Box(width * .38, height * .47, height * .055, height * .055);
            var select = new Box(width * .83, height * .85, width * .07, height * .03);
            var frame = new Observation(epoch, width, height,
            [new("MAP", new(100, 30, 100, 25)), new("COMMAND", new(300, 30, 100, 25)),
                new("ARMORY", new(500, 30, 100, 25)), new("TWINE PEAKS STORM SHIELD", new(100, 160, 400, 30)),
                new("REWARDS", new(100, 400, 200, 25)), new("ZOOM", new(900, 900, 100, 25)),
                new("SELECT", select)], [new("homebase-icon", icon, .94, .2)]);
            NavigationDecision Decide(Observation f, bool confirmed = false) => NavigationPlanner.Decide(f, recognizer.Recognize(f), profile, confirmed);
            check($"{width}x{height}: navigation uses detected icon away from fixed coordinates", Decide(frame).Plan?.Target == icon);
            check($"{width}x{height}: SELECT requires a confirmed homebase click", Decide(frame).Plan?.Step == NavigationStep.SelectHomebase
                && Decide(frame, true).Plan is { Step: NavigationStep.OpenHomebase } plan && plan.Target == select);
            var ambiguous = frame with { Visuals = [new("homebase-icon", icon, .94, .2), new("homebase-icon", icon with { X = icon.X + 150 }, .94, .2)] };
            check($"{width}x{height}: ambiguous icon cannot fall back to fixed input", Decide(ambiguous).Plan is null);
            foreach (var (score, margin) in new[] { (.7, .3), (.94, .02) })
                check($"{width}x{height}: weak icon ({score}/{margin}) waits", Decide(frame with { Visuals = [new("homebase-icon", icon, score, margin)] }).Plan is null);
            check($"{width}x{height}: offscreen icon rejected", Decide(frame with { Visuals = [new("homebase-icon", icon with { X = width + 10 }, .95, .2)] }).Plan is null);
            check($"{width}x{height}: conflicting SELECT labels wait", Decide(frame with { Regions = [.. frame.Regions, new("SELECT", select with { X = 200 })] }, true).Plan is null);
            check($"{width}x{height}: missing SELECT waits", Decide(frame with { Regions = frame.Regions.Where(r => r.Normalized != "SELECT").ToArray() }, true).Plan is null);
            check($"{width}x{height}: wrong zone blocks detected targets", NavigationPlanner.Decide(frame, recognizer.Recognize(frame), profile with { Zone = HomebaseZone.Stonewood }, true).RequiresAttention);
            check($"{width}x{height}: shield below ten blocks launch", Decide(frame with { Regions = [.. frame.Regions, new("SHIELD POWER", new(100, 250, 200, 25)), new("9/10", new(100, 290, 80, 25))] }, true).RequiresAttention);
        }
        check("Detected map targets are revalidated before input", !MenuGeometry.UsesFixedTarget(NavigationStep.SelectHomebase)
            && !MenuGeometry.UsesFixedTarget(NavigationStep.OpenHomebase));
    }
}
