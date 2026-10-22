using EndurancePilot.Core;

int passed = 0;
void Check(string name, bool condition)
{
    if (!condition)
    {
        throw new Exception("FAIL: " + name);
    }

    passed++;
    Console.WriteLine("PASS: " + name);
}
var epoch = DateTimeOffset.Parse("2026-10-16T12:00:00Z");
Observation Frame(int second, params string[] text) => new(epoch.AddSeconds(second), 1920, 1080,
    text.Select((t, i) => new TextRegion(t, new Box(100, 50 + i * 70, 300, 45))).ToArray());
var recognizer = new ScreenRecognizer();
HestiaEntryScenarios.Run(Check, epoch);
LoadedEntryScenarios.Run(Check, epoch);
AnnouncementScenarios.Run(Check, epoch);
var tracker = new RunTracker();
RunRecord? Observe(int second, params string[] text)
{
    var frame = Frame(second, text);
    return tracker.Observe(frame, recognizer.Recognize(frame));
}
Check("Generic Continue is unknown", recognizer.Recognize(Frame(0, "CONTINUE")).Kind == ScreenKind.Unknown);
Check("Discovery accepts observed PLAV OCR", recognizer.Recognize(Frame(0, "CURRENT LOBBY", "SA VE THE WORLD", "PLAV", "SHOP")).Kind == ScreenKind.ModeDiscovery);
Check("PLAV alone cannot identify discovery", recognizer.Recognize(Frame(0, "PLAV", "SHOP")).Kind == ScreenKind.Unknown);
Check("Isolated victory text is insufficient", recognizer.Recognize(Frame(0, "VICTORY")).Kind == ScreenKind.Unknown);
Check("STW lobby needs corroborated navigation", recognizer.Recognize(Frame(0, "MAP", "COMMAND", "ARMORY")).Kind == ScreenKind.SaveTheWorldLobby);
Check("Mission map survives temporarily unreadable mission heading", recognizer.Recognize(Frame(0, "MAP", "COMMAND", "ARMORY", "REWARDS", "ZOOM")).Kind == ScreenKind.MissionMap);
Check("Rewards alone do not establish mission map", recognizer.Recognize(Frame(0, "MAP", "COMMAND", "ARMORY", "REWARDS")).Kind == ScreenKind.SaveTheWorldLobby);
Check("Homebase recognises the observed split Shield OCR", recognizer.Recognize(Frame(0, "Storm Shield", "O Activate Storm S ieldto Defend")).Kind == ScreenKind.Homebase);
Check("Storm Shield heading alone cannot trigger walking", recognizer.Recognize(Frame(0, "Storm Shield")).Kind != ScreenKind.Homebase);
Check("Wave 31 is invalid", WaveReader.Read(Frame(0, "Endurance: Wave 31")).Count == 0);
Check("Countdown cannot be a live wave reading", WaveReader.Read(Frame(0, "Wave 30 starts in:", "Endurance Defense: Wave 30 starts in:")).Count == 0);
Check("Observed countdown survives a partially obscured Endurance word", recognizer.Recognize(Frame(0, "nce Defense: Wave 2 starts in", "0:18", "E ACCESS STORM SHIELD MENU")).Kind == ScreenKind.EnduranceCountdown);
Check("Countdown needs a corroborating timer", recognizer.Recognize(Frame(0, "Endurance Defense: Wave 2 starts in:")).Kind == ScreenKind.Unknown);
Check("Timed objective line gives a live wave", WaveReader.Read(Frame(0, "[ 2:40 ] Endurance: Wave 2")).Single().Wave == 2);
var countdownTracker = new RunTracker();
foreach (int second in new[] { 1, 3 })
{
    var countdown = Frame(second, "Endurance Defense: Wave 30 starts in:", "0:18");
    countdownTracker.Observe(countdown, recognizer.Recognize(countdown));
}
Check("Countdown never establishes wave 30", countdownTracker.Current is null);
Observe(1, "Endurance: Wave 29");
Check("One frame cannot establish wave", tracker.Current is null);
Observe(3, "Endurance: Wave 29");
Check("Time-separated wave reads establish run", tracker.ConfirmedWave == 29);
Observe(4, "Endurance: Wave 30");
Observe(6, "Endurance: Wave 30");
Check("Wave 30 appearing is not completion", tracker.Current?.Outcome == RunOutcome.InProgress);
Observe(7, "VICTORY", "TEAM SCORE", "CONTINUE");
Check("Single result frame is insufficient", tracker.Current?.Outcome == RunOutcome.InProgress);
var completed = Observe(9, "VICTORY", "TEAM SCORE", "CONTINUE");
Check("Wave 30 followed by stable victory completes", completed?.Outcome == RunOutcome.Completed30);
Check("Result does not count twice", Observe(12, "VICTORY", "TEAM SCORE", "CONTINUE") is null);
Observe(15, "Endurance: Wave 1");
Observe(17, "Endurance: Wave 1");
Check("New run after terminal result resets wave", tracker.ConfirmedWave == 1 && tracker.Current?.Id != completed?.Id);
Observe(20, "Endurance: Wave 30");
Observe(22, "Endurance: Wave 30");
Check("A two-read large OCR jump is held", tracker.ConfirmedWave == 1);
Observe(24, "Endurance: Wave 2");
Observe(26, "Endurance: Wave 2");
Check("Valid progression recovers after bad read", tracker.ConfirmedWave == 2);
Observe(28, "Endurance: Wave 3", "Wave 8");
Observe(30, "Endurance: Wave 3", "Wave 8");
Check("Conflicting HUD indicators are rejected", tracker.ConfirmedWave == 2);
Observe(32, "VICTORY", "TEAM SCORE");
var unverified = Observe(34, "VICTORY", "TEAM SCORE");
Check("Victory without wave 30 is unverified", unverified?.Outcome == RunOutcome.UnverifiedVictory);
Observe(40, "Endurance: Wave 12");
Observe(42, "Endurance: Wave 12");
Check("Attaching mid-run is supported", tracker.ConfirmedWave == 12);
Observe(45, "DEFEAT", "CONTINUE");
Check("Defeat is recorded separately", Observe(47, "DEFEAT", "CONTINUE")?.Outcome == RunOutcome.Failed);
var sameBox = new Box(500, 100, 300, 40);
var duplicated = new Observation(epoch, 2560, 1440, [new("Wave 10", sameBox, "overview"), new("Wave 10", sameBox, "tile")]);
Check("Repeated OCR passes are one source", WaveReader.Read(duplicated).Count == 1);
var conflicting = duplicated with { Regions = [new("Wave 10", sameBox), new("Wave 18", sameBox)] };
Check("Conflicting OCR at the same position remains a disagreement", WaveReader.Read(conflicting).Select(r => r.Wave).Distinct().Count() == 2);
Check("Historical max wave cannot establish live endurance", recognizer.Recognize(Frame(0, "Endurance Max Wave 30 / 30", "MAP", "COMMAND", "ARMORY", "TWINE PEAKS STORM SHIELD", "SELECT")).Kind == ScreenKind.MissionMap);
var interruptedRead = new RunTracker();
var wave = Frame(0, "Endurance: Wave 1");
interruptedRead.Observe(wave, recognizer.Recognize(wave));
var lateWave = wave with { CapturedAt = epoch.AddSeconds(30) };
interruptedRead.Observe(lateWave, recognizer.Recognize(lateWave));
Check("Long observation gaps reset wave confirmation", interruptedRead.Current is null);
interruptedRead.Observe(lateWave with { CapturedAt = epoch.AddSeconds(31) }, recognizer.Recognize(lateWave));
Check("Wave reacquires after a fresh pair of observations", interruptedRead.ConfirmedWave == 1);
Check("An unknown error is not assumed recoverable", recognizer.Recognize(Frame(0, "THERE WAS AN ERROR", "CONTINUE")).Kind == ScreenKind.NeedsAttention);
Check("Network evidence enables bounded recovery", recognizer.Recognize(Frame(0, "NETWORK CONNECTION LOST", "CONTINUE")).Kind == ScreenKind.ErrorDialog);
Check("Start Endurance alone cannot authorize an action", recognizer.Recognize(Frame(0, "START ENDURANCE")).Kind == ScreenKind.Unknown);
var setup = Frame(0, "STORM SHIELD", "START ENDURANCE", "Wait for Assistance");
Check("Start waits for positive unchecked evidence", NavigationPlanner.Decide(setup, recognizer.Recognize(setup), new(HomebaseZone.TwinePeaks, true)).Plan is null);
var setupOn = setup with { Visuals = [new("assistance-checked", sameBox, .95, .2)] };
Check("Checked assistance is disabled before start", NavigationPlanner.Decide(setupOn, recognizer.Recognize(setupOn), new(HomebaseZone.TwinePeaks, true)).Plan?.Step == NavigationStep.DisableAssistance);
var setupOff = setup with { Visuals = [new("assistance-unchecked", sameBox, .95, .2)] };
Check("Verified unchecked assistance permits start", NavigationPlanner.Decide(setupOff, recognizer.Recognize(setupOff), new(HomebaseZone.TwinePeaks, true)).Plan?.Step == NavigationStep.StartEndurance);
var setupAmbiguous = setup with { Visuals = [.. setupOn.Visuals!, .. setupOff.Visuals!] };
Check("Conflicting checkbox states block start", NavigationPlanner.Decide(setupAmbiguous, recognizer.Recognize(setupAmbiguous), new(HomebaseZone.TwinePeaks, true)).Plan is null);

var controller = new AutomationController();
ClickAction? Feed(int second, params string[] text)
{
    var frame = Frame(second, text);
    return controller.Observe(frame, recognizer.Recognize(frame));
}
ClickAction PlanError(int second, bool installation = false)
{
    string body = installation ? "VERIFY OR REINSTALL" : "NETWORK CONNECTION LOST";
    Feed(second, body, "CONTINUE");
    return Feed(second + 1, body, "CONTINUE") ?? throw new Exception("Expected stable error action");
}
controller.Start();
Check("One error frame does not click", Feed(1, "NETWORK CONNECTION LOST", "CONTINUE") is null);
var action = Feed(2, "NETWORK CONNECTION LOST", "CONTINUE")!;
Check("Stable error finds a local text target", action is not null && action.Target.CenterX == 250);
controller.Dispatched(action!, epoch.AddSeconds(2));
Feed(3, "CONTINUE");
Check("Unknown screen cannot prove dismissal", controller.ConfirmedDismissals == 0 && controller.Status == ControllerStatus.AwaitingTransition);
Feed(4, "MAP", "COMMAND", "ARMORY");
Check("Single destination frame cannot prove dismissal", controller.ConfirmedDismissals == 0);
Feed(5, "MAP", "COMMAND", "ARMORY");
Check("Known stable destination proves dismissal", controller.ConfirmedDismissals == 1 && controller.Status == ControllerStatus.Observing);
Check("Recovery cooldown prevents immediate input", Feed(6, "NETWORK CONNECTION LOST", "CONTINUE") is null);

controller.Start();
action = PlanError(10, installation: true);
controller.Dispatched(action, epoch.AddSeconds(11));
Feed(12, "MAP", "COMMAND", "ARMORY");
Feed(13, "MAP", "COMMAND", "ARMORY");
Check("Installation error pauses after verified dismissal", controller.Status == ControllerStatus.PausedForAttention);
Check("Paused controller cannot act", Feed(15, "NETWORK CONNECTION LOST", "CONTINUE") is null);

controller.Start();
action = PlanError(20);
controller.Dispatched(action, epoch.AddSeconds(21));
Feed(33, "NETWORK CONNECTION LOST", "CONTINUE");
Check("Failed click requires a new target acquisition", controller.Status == ControllerStatus.Observing && Feed(34, "NETWORK CONNECTION LOST", "CONTINUE") is null);
action = PlanError(36);
controller.Dispatched(action, epoch.AddSeconds(37));
Feed(50, "NETWORK CONNECTION LOST", "CONTINUE");
Check("Two failed click attempts pause", controller.Status == ControllerStatus.PausedForAttention);

controller.Start();
action = PlanError(60);
Feed(62, "CONTINUE");
bool rejected = false;
try { controller.Dispatched(action, epoch.AddSeconds(62)); } catch (InvalidOperationException) { rejected = true; }
Check("A newer observation invalidates an old action", rejected);
action = PlanError(64);
rejected = false;
try { controller.Dispatched(action, epoch.AddSeconds(71)); } catch (InvalidOperationException) { rejected = true; }
Check("Old action expires even without another observation", rejected);
controller.Start();
Feed(80, "NETWORK CONNECTION LOST", "CONTINUE", "OK");
Check("Two separate error buttons are ambiguous", Feed(81, "NETWORK CONNECTION LOST", "CONTINUE", "OK") is null);
Check("Invalid target geometry is rejected", !AutomationController.IsValidTarget(new Box(double.NaN, 0, 10, 10), 100, 100)
    && !AutomationController.IsValidTarget(new Box(90, 0, 20, 10), 100, 100)
    && !AutomationController.IsValidTarget(new Box(0, 0, 0, 10), 100, 100));
controller.Start();
for (int i = 0; i < 3; i++)
{
    int time = 100 + i * 20;
    action = PlanError(time);
    controller.Dispatched(action, epoch.AddSeconds(time + 1));
    Feed(time + 2, "MAP", "COMMAND", "ARMORY");
    Feed(time + 3, "MAP", "COMMAND", "ARMORY");
}
Check("Repeated connection failures stop the recovery loop", controller.Status == ControllerStatus.PausedForAttention);

NavigationScenarios.Run(Check, epoch);
QuestMenuEntryScenarios.Run(Check, epoch);
DiscoveryScenarios.Run(Check, epoch);
InteractionScenarios.Run(Check, epoch);
FuzzyScenarios.Run(Check, epoch);
RejoinScenarios.Run(Check, epoch);
ErrorDialogScenarios.Run(Check, epoch);
SleepScenarios.Run(Check, epoch);
RapidNavigationScenarios.Run(Check, epoch);
SessionScenarios.Run(Check, epoch);
RunRestartScenarios.Run(Check, epoch);
RunnerSessionScenarios.Run(Check, epoch);
TargetWaveScenarios.Run(Check, epoch);
LaunchScenarios.Run(Check, epoch);
CrashRecoveryScenarios.Run(Check, epoch);
ProcessExitScenarios.Run(Check, epoch);
RewardScenarios.Run(Check, epoch);
MissionRewardScenarios.Run(Check, epoch);
WalkRoute.TwineInitial.Validate();
Check("Initial Twine route matches the requested timed holds", WalkRoute.TwineInitial.Steps.SequenceEqual(new[] { new WalkStep(WalkKey.A, 4000), new WalkStep(WalkKey.W, 4000) }));
foreach (var invalidRoute in new[] { new WalkRoute([]), new WalkRoute([new((WalkKey)99,1000)]),
    new WalkRoute([new(WalkKey.W,0)]), new WalkRoute([new(WalkKey.W,10001)]),
    new WalkRoute([new(WalkKey.W,10000),new(WalkKey.A,10000),new(WalkKey.S,10000),new(WalkKey.D,10000)]) })
{
    bool invalidRejected = false;
    try
    {
        invalidRoute.Validate();
    }
    catch (InvalidOperationException) { invalidRejected = true; }
    Check("Invalid or excessive movement route is rejected", invalidRejected);
}
if (args.Length == 2 && args[0] == "--navigation")
{
    NavigationScenarios.RunCaptures(args[1], Check);
}
else if (args.Length == 2 && args[0] == "--target-exit-captures")
{
    TargetWaveScenarios.RunCaptures(args[1], Check);
}
else if (args.Length == 2)
{
    var options = new System.Text.Json.JsonSerializerOptions();
    options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    var expectations = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, ScreenKind>>(File.ReadAllText(args[0]), options)!;
    foreach (var (name, expected) in expectations)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(args[1], name + ".json")));
        var captured = doc.RootElement.GetProperty("Observation").DeserializeObservation(options);
        Check("Real capture: " + name, recognizer.Recognize(captured).Kind == expected);
        if (name.StartsWith("installation-error"))
        {
            controller.Start();
            controller.Observe(captured, recognizer.Recognize(captured));
            var later = captured with
            {
                CapturedAt = captured.CapturedAt.AddSeconds(1)
            };
            var dismiss = controller.Observe(later, recognizer.Recognize(later));
            Check("Detected dismissal target: " + name, dismiss is { PauseAfterDismissal: true }
                && AutomationController.IsValidTarget(dismiss.Target, captured.Width, captured.Height));
        }
    }
}
Console.WriteLine($"{passed} checks passed.");

static class JsonExtensions
{
    public static Observation DeserializeObservation(this System.Text.Json.JsonElement element, System.Text.Json.JsonSerializerOptions options)
        => System.Text.Json.JsonSerializer.Deserialize<Observation>(element.GetRawText(), options)!;
}
