using EndurancePilot.Core;

internal static class InteractionScenarios
{
    public static void Run(Action<string, bool> check, DateTimeOffset epoch)
    {
        var recognizer = new ScreenRecognizer();
        var profile = new RunnerProfile(HomebaseZone.TwinePeaks, true);
        Observation Frame(int time, params TextRegion[] text) => new(epoch.AddSeconds(time), 2560, 1440, text);
        TextRegion Label(string text, int x, int y, int w = 400, int h = 30) => new(text, new(x, y, w, h));
        var homebase = Frame(0, Label("Storm Shield", 20, 180), Label("Activate Storm Shield to Defend", 20, 220),
            Label("ACCESS STORM SHIELD MENU", 1300, 800), Label("E", 1240, 800, 25));
        var plan = NavigationPlanner.Decide(homebase, recognizer.Recognize(homebase), profile).Plan;
        check("Inactive homebase opens backpack without walking", plan is { Step: NavigationStep.OpenBackpack, Key: NavigationKey.Tab });
        var noBinding = homebase with
        {
            Regions = homebase.Regions.Where(r => r.Normalized != "E").ToArray()
        };
        check("Backpack route does not need a nearby E prompt", NavigationPlanner.Decide(noBinding, recognizer.Recognize(noBinding), profile).Plan is { Step: NavigationStep.OpenBackpack });
        var tinyObjective = Frame(0, Label("Storm Shield", 900, 100, 65, 9), Label("O Activate Storm Shield to Defend", 900, 115, 185, 10));
        check("Small relocated objective opens backpack without fixed HUD coordinates", NavigationPlanner.Decide(tinyObjective,
            recognizer.Recognize(tinyObjective), profile).Plan is { Step: NavigationStep.OpenBackpack, Key: NavigationKey.Tab });
        var mergedObjective = Frame(0, Label("Storm Shield O Activate Storm Shield to Defend", 900, 100, 200, 25));
        check("Merged header and activation objective identify inactive homebase", recognizer.Recognize(mergedObjective).Kind == ScreenKind.Homebase);
        var wrappedObjective = Frame(0, Label("Storm Shield", 900, 100, 65, 9),
            Label("Activate Storm Shield", 900, 115, 120, 10), Label("to Defend", 900, 130, 55, 10));
        check("Adjacent wrapped activation objective is recognised", recognizer.Recognize(wrappedObjective).Kind == ScreenKind.Homebase);
        var distantObjective = tinyObjective with
        {
            Regions = tinyObjective.Regions.Select(r => r.Text.StartsWith("O ")
            ? r with
            {
                Bounds = new(100, 800, 185, 10)
            } : r).ToArray()
        };
        check("Unrelated header and distant objective cannot establish homebase", recognizer.Recognize(distantObjective).Kind != ScreenKind.Homebase);
        var missingHeader = tinyObjective with
        {
            Regions = tinyObjective.Regions.Skip(1).ToArray()
        };
        check("Activation text alone still needs its mission header", recognizer.Recognize(missingHeader).Kind != ScreenKind.Homebase);
        var noisyObjective = tinyObjective with
        {
            Regions = tinyObjective.Regions.Select(r => r.Text.StartsWith("O ")
            ? r with
            {
                Text = "O Activate StomShiebd to Defend"
            } : r).ToArray()
        };
        check("Captured tiny-HUD noun misread retains exact activation and defend evidence", recognizer.Recognize(noisyObjective).Kind == ScreenKind.Homebase);
        var wrongObjective = noisyObjective with
        {
            Regions = noisyObjective.Regions.Select(r => r.Text.StartsWith("O ")
            ? r with
            {
                Text = "O Activate Storm Chest to Defend"
            } : r).ToArray()
        };
        check("Another objective noun is not corrected into Storm Shield", recognizer.Recognize(wrongObjective).Kind != ScreenKind.Homebase);
        var distantBinding = noBinding with
        {
            Regions = [.. noBinding.Regions, Label("E", 500, 300, 25)]
        };
        check("An unrelated E label cannot authorize interaction", InteractionPrompts.StormShield(distantBinding) is null);
        var consoleLabelOnly = Frame(0, homebase.Regions[2], homebase.Regions[3]);
        check("Console prompt without an inactive shield objective cannot act", NavigationPlanner.Decide(consoleLabelOnly, recognizer.Recognize(consoleLabelOnly), profile).Plan is null);
        var active = homebase with
        {
            Regions = [.. homebase.Regions, Label("Endurance: Wave 4", 1000, 100)]
        };
        check("Active wave takes priority over console interaction", recognizer.Recognize(active).Kind == ScreenKind.ActiveEndurance
            && NavigationPlanner.Decide(active, recognizer.Recognize(active), profile).Plan is null);
        var countdown = homebase with
        {
            Regions = [.. homebase.Regions, Label("Endurance Defense: Wave 4 starts in:", 900, 100), Label("0:20", 1200, 140)]
        };
        check("Countdown takes priority over console interaction", recognizer.Recognize(countdown).Kind == ScreenKind.EnduranceCountdown
            && NavigationPlanner.Decide(countdown, recognizer.Recognize(countdown), profile).Plan is null);
        var controller = new AutomationController { Profile = profile, ErrorRecoveryOnly = false };
        controller.Start();
        controller.Observe(homebase, recognizer.Recognize(homebase));
        var next = homebase with
        {
            CapturedAt = epoch.AddSeconds(1)
        };
        var action = controller.Observe(next, recognizer.Recognize(next))!;
        check("Backpack dispatch preserves the inactive objective as its visual guard", action is { Key: NavigationKey.Tab } && action.Target == plan!.Target
            && InteractionPrompts.IsAllowedKeyboardAction(action));
        check("Keyboard action cannot be reused on an active-wave screen", !InteractionPrompts.IsAllowedKeyboardAction(action with
        {
            SourceScreen = ScreenKind.ActiveEndurance
        }));
        controller.Dispatched(action, epoch.AddSeconds(1));
        controller.Observe(homebase with
        {
            CapturedAt = epoch.AddSeconds(2)
        }, recognizer.Recognize(homebase));
        check("Unchanged objective cannot confirm backpack opening", controller.Status == ControllerStatus.AwaitingTransition);
        var console = Frame(3, Label("STORM SHIELD ENDURANCE", 500, 800), Label("DAILY WARGAMES", 1100, 600), Label("STORAGE", 1600, 450));
        controller.Observe(console, recognizer.Recognize(console));
        controller.Observe(console with
        {
            CapturedAt = epoch.AddSeconds(4)
        }, recognizer.Recognize(console));
        check("Stable console menu confirms interaction", controller.Status == ControllerStatus.Observing);
        controller.Observe(active with
        {
            CapturedAt = epoch.AddSeconds(10)
        }, recognizer.Recognize(active));
        controller.Observe(homebase with
        {
            CapturedAt = epoch.AddSeconds(11)
        }, recognizer.Recognize(homebase));
        check("Known active run blocks later misleading homebase prompts", controller.Observe(homebase with { CapturedAt = epoch.AddSeconds(12) }, recognizer.Recognize(homebase)) is null && controller.RunInProgress);
        controller.Observe(console with
        {
            CapturedAt = epoch.AddSeconds(13)
        }, recognizer.Recognize(console));
        check("Opening the console manually during a run does not restart endurance", controller.Observe(console with { CapturedAt = epoch.AddSeconds(14) }, recognizer.Recognize(console)) is null);
        var backpack = Frame(15, Label("Loadout", 200, 30, 100), Label("Objectives", 350, 30, 150), Label("Quests", 550, 30, 100),
            Label("Storm Shield", 700, 30, 180), Label("Loading...", 1500, 600));
        check("Backpack tabs take priority over an item loading label", recognizer.Recognize(backpack).Kind == ScreenKind.Backpack);
        plan = NavigationPlanner.Decide(backpack, recognizer.Recognize(backpack), profile).Plan;
        check("Backpack targets only the Storm Shield tab", plan is { Step: NavigationStep.SelectStormShieldTab, Key: null } && plan.Target.X == 700);
        controller.Observe(backpack, recognizer.Recognize(backpack));
        check("An active run prevents backpack navigation", controller.Observe(backpack with { CapturedAt = epoch.AddSeconds(16) }, recognizer.Recognize(backpack)) is null);
        var misaligned = backpack with
        {
            Regions = backpack.Regions.Select(r => r.Normalized == "QUESTS" ? r with { Bounds = new(550, 400, 100, 30) } : r).ToArray()
        };
        check("Unrelated labels cannot establish the backpack tab row", StormShieldNavigation.BackpackTab(misaligned) is null);
        var wrapped = Frame(17, Label("Storm Shield", 500, 700, 180), Label("Endurance", 525, 750, 130), Label("Daily Wargames", 900, 600), Label("Storage", 1400, 400), Label("Leave with Party", 1400, 800));
        check("Wrapped endurance tile is recognised", recognizer.Recognize(wrapped).Kind == ScreenKind.StormShieldConsole
            && NavigationPlanner.Decide(wrapped, recognizer.Recognize(wrapped), profile).Plan is { Step: NavigationStep.SelectEndurance });
        var separated = wrapped with
        {
            Regions = wrapped.Regions.Select(r => r.Normalized == "ENDURANCE" ? r with { Bounds = new(1000, 750, 130, 30) } : r).ToArray()
        };
        check("Separate Storm Shield and Endurance labels cannot form a tile", StormShieldNavigation.EnduranceTile(separated) is null);
        var missedStorage = wrapped with
        {
            Regions = wrapped.Regions.Where(r => r.Text != "Storage")
            .Append(Label("CUSTOM WARGAMES", 900, 850)).ToArray()
        };
        check("Console remains actionable when Storage OCR is missing", recognizer.Recognize(missedStorage).Kind == ScreenKind.StormShieldConsole
            && NavigationPlanner.Decide(missedStorage, recognizer.Recognize(missedStorage), profile).Plan?.Step == NavigationStep.SelectEndurance);
        var weakConsole = missedStorage with
        {
            Regions = missedStorage.Regions.Where(r => r.Text != "Daily Wargames").ToArray()
        };
        check("One neighbouring menu label cannot authorize endurance selection", recognizer.Recognize(weakConsole).Kind != ScreenKind.StormShieldConsole);
        var overlappingTabs = missedStorage with
        {
            Regions = missedStorage.Regions.Concat(backpack.Regions).ToArray()
        };
        check("Console content takes precedence over visible backpack tabs", recognizer.Recognize(overlappingTabs).Kind == ScreenKind.StormShieldConsole);
        var menuSequence = new AutomationController { Profile = profile, ErrorRecoveryOnly = false };
        menuSequence.Start();
        menuSequence.Observe(backpack, recognizer.Recognize(backpack));
        var tabClick = menuSequence.Observe(backpack with
        {
            CapturedAt = epoch.AddSeconds(16)
        }, recognizer.Recognize(backpack))!;
        menuSequence.Dispatched(tabClick, epoch.AddSeconds(16));
        menuSequence.Observe(overlappingTabs with
        {
            CapturedAt = epoch.AddSeconds(17)
        }, recognizer.Recognize(overlappingTabs));
        menuSequence.Observe(overlappingTabs with
        {
            CapturedAt = epoch.AddSeconds(18)
        }, recognizer.Recognize(overlappingTabs));
        menuSequence.Observe(overlappingTabs with
        {
            CapturedAt = epoch.AddSeconds(24)
        }, recognizer.Recognize(overlappingTabs));
        var tileClick = menuSequence.Observe(overlappingTabs with
        {
            CapturedAt = epoch.AddSeconds(25)
        }, recognizer.Recognize(overlappingTabs));
        check("Backpack tab transition leads to Endurance tile click", tileClick?.Step == NavigationStep.SelectEndurance);
        check("Leave with Party never becomes a default navigation action", NavigationPlanner.Decide(wrapped, recognizer.Recognize(wrapped), profile).Plan?.Step == NavigationStep.SelectEndurance);
        var hub = Frame(20, Label("E QUEST LOG", 1700, 700));
        plan = NavigationPlanner.Decide(hub, recognizer.Recognize(hub), profile).Plan;
        check("Observed quest-log prompt routes through Tab", plan is { Key: NavigationKey.Tab, Step: NavigationStep.OpenQuestMenu });
        controller.Observe(hub, recognizer.Recognize(hub));
        controller.Observe(hub with
        {
            CapturedAt = epoch.AddSeconds(21)
        }, recognizer.Recognize(hub));
        action = controller.Observe(hub with
        {
            CapturedAt = epoch.AddSeconds(22)
        }, recognizer.Recognize(hub))!;
        check("Confirmed lobby clears active-run latch before target reacquisition", !controller.RunInProgress && action is { Key: NavigationKey.Tab });
        check("Tab is permitted only for the verified hub action", InteractionPrompts.IsAllowedKeyboardAction(action)
            && !InteractionPrompts.IsAllowedKeyboardAction(action with
            {
                Step = NavigationStep.StartEndurance
            }));
    }
}
