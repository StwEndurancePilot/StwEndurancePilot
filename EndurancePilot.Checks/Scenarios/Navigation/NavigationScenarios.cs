using EndurancePilot.Core;

internal static class NavigationScenarios
{
    public static void Run(Action<string, bool> check, DateTimeOffset epoch)
    {
        var recognizer = new ScreenRecognizer();
        var profile = new RunnerProfile(HomebaseZone.TwinePeaks, AfkBaseConfirmed: true);
        var superUltrawide = new Observation(epoch, 5120, 1400, []);
        var liveMap = MenuGeometry.Target(NavigationStep.OpenMap, superUltrawide);
        var liveArrow = MenuGeometry.Target(NavigationStep.ChangeZone, superUltrawide);
        var liveSelect = MenuGeometry.Target(NavigationStep.EnterZone, superUltrawide);
        check("5120x1400 Map tab matches live Fortnite position", liveMap.CenterX is > 2020 and < 2140 && liveMap.CenterY is > 115 and < 165);
        check("5120x1400 zone arrow matches live Fortnite position", liveArrow.CenterX is > 185 and < 230 && liveArrow.CenterY is > 315 and < 370);
        check("5120x1400 Select matches live Fortnite position", liveSelect.CenterX is > 4690 and < 4830 && liveSelect.CenterY is > 1160 and < 1240);
        foreach (var (width, height) in new[] { (1920, 1080), (2560, 1440), (3440, 1440), (3840, 1080), (5120, 1400), (5120, 1440) })
        {
            var layout = new Observation(epoch, width, height, []);
            var map = MenuGeometry.Target(NavigationStep.OpenMap, layout);
            var arrow = MenuGeometry.Target(NavigationStep.ChangeZone, layout);
            var select = MenuGeometry.Target(NavigationStep.EnterZone, layout);
            double uiWidth = Math.Min(width, height * 16d / 9);
            check($"Menu anchors at {width}x{height}",
                Math.Abs(map.CenterX - (width / 2d - .1875 * uiWidth)) < .01
                && Math.Abs(arrow.CenterX - .082 * uiWidth) < .01
                && Math.Abs(select.CenterX - (width - .15 * uiWidth)) < .01);
        }
        check("Pointer parks beyond the Select letters", NavigationPlanner.PointerRestingPoint(new(800, 700, 200, 30), 1920, 1080) is { X: > 1000 });
        check("Pointer parks left when the anchor touches right edge", NavigationPlanner.PointerRestingPoint(new(1800, 700, 120, 30), 1920, 1080) is { X: < 1800 });
        check("Pointer cannot park outside a fully occupied frame", NavigationPlanner.PointerRestingPoint(new(0, 0, 1920, 1080), 1920, 1080) is null);
        var hub = new Observation(epoch, 1920, 1080, [], [
            new("hub-quest-board",new(1085,425,420,300),.95,.25),
            new("hub-xp-door",new(1590,300,185,430),.95,.25)]);
        check("Corroborated post-reward hub opens quest menu", NavigationPlanner.Decide(hub, recognizer.Recognize(hub), profile).Plan?.Step == NavigationStep.OpenQuestMenu);
        check("Hub board alone cannot establish lobby", recognizer.Recognize(hub with
        {
            Visuals = [hub.Visuals![0]]
        }).Kind == ScreenKind.Unknown);
        check("Ambiguous hub landmark cannot establish lobby", recognizer.Recognize(hub with
        {
            Visuals = [hub.Visuals![0], hub.Visuals[1] with { Margin = .01 }]
        }).Kind == ScreenKind.Unknown);
        check("Unrelated landmark positions cannot establish lobby", recognizer.Recognize(hub with
        {
            Visuals = [hub.Visuals![0], hub.Visuals[1] with { Bounds = new(10, 10, 185, 430) }]
        }).Kind == ScreenKind.Unknown);
        Observation Frame(int time, params TextRegion[] regions) => new(epoch.AddSeconds(time), 2560, 1440, regions);
        TextRegion Label(string text, double x, double y, double w = 200, double h = 30) => new(text, new(x, y, w, h));
        var expansionMenu = new Observation(epoch, 1600, 900,
        [
            Label("Storm Shield", 492, 25, 125, 16),
            Label("Loadout", 166, 25, 80, 16),
            Label("Objectives", 271, 25, 103, 21),
            Label("Quests", 399, 26, 69, 19),
            Label("EXPAND STORM SHIELD", 369, 544, 213, 20),
            Label("DAILY WARGAMES", 716, 410, 168, 20),
            Label("CUSTOM WARGAMES", 703, 635, 194, 20),
            Label("STORAGE", 1083, 319, 84, 20),
            Label("Storm Shield Level 10", 1097, 173, 228, 18)
        ]);
        var expansionEvidence = recognizer.Recognize(expansionMenu);
        var expansionDecision = NavigationPlanner.Decide(expansionMenu, expansionEvidence, profile);
        check("Expand Storm Shield is recognized as the console, not the backpack",
            expansionEvidence.Kind == ScreenKind.StormShieldConsole);
        check("Expansion menu requires attention instead of launching a different mission",
            expansionDecision.RequiresAttention && expansionDecision.Plan is null
            && expansionDecision.Detail.Contains("Endurance is unavailable", StringComparison.Ordinal));
        var assistanceSetup = Frame(0, Label("STORM SHIELD", 300, 130), Label("START ENDURANCE", 700, 700));
        var uncheckedSetup = assistanceSetup with { Visuals = [new("assistance-unchecked", new(1127, 824, 57, 56), .95, .2)] };
        var checkedSetup = assistanceSetup with { Visuals = [new("assistance-checked", new(1127, 824, 57, 56), .95, .2)] };
        var setupEvidence = recognizer.Recognize(assistanceSetup);
        check("Wait for assistance off starts when unchecked", NavigationPlanner.Decide(uncheckedSetup, setupEvidence, profile).Plan?.Step == NavigationStep.StartEndurance);
        check("Wait for assistance off clears a checked box", NavigationPlanner.Decide(checkedSetup, setupEvidence, profile).Plan?.Step == NavigationStep.DisableAssistance);
        var assistedProfile = profile with { WaitForAssistance = true };
        check("Wait for assistance on checks an unchecked box", NavigationPlanner.Decide(uncheckedSetup, setupEvidence, assistedProfile).Plan?.Step == NavigationStep.EnableAssistance);
        check("Wait for assistance on starts when checked", NavigationPlanner.Decide(checkedSetup, setupEvidence, assistedProfile).Plan?.Step == NavigationStep.StartEndurance);
        var garbledLoadout = Frame(0, Label("NERPLOADOUT", 76, 449), Label("NERYLOADOUT", 77, 449),
            Label("TWIN PEAKS STORM SIUELD", 162, 245), Label("NERO ABILITIES", 121, 663), Label("EDIT", 330, 855),
            Label("Mission Details", 1841, 1383), Label("Abandon", 2162, 1383),
            Label("MAP", 750, 134), Label("COMMAND", 888, 134), Label("ARMORY", 1116, 134), Label("1:30", 2381, 1184, 126, 64));
        check("Captured garbled loadout remains HomebasePreparing", recognizer.Recognize(garbledLoadout).Kind == ScreenKind.HomebasePreparing);
        check("Garbled loadout does not click shared Map navigation", NavigationPlanner.Decide(garbledLoadout,
            recognizer.Recognize(garbledLoadout), profile).Plan is null);
        var readyLoadout = garbledLoadout with
        {
            Regions = garbledLoadout.Regions.Append(Label("LAUNCH", 2050, 1180)).ToArray()
        };
        check("Corroborated garbled loadout can launch when control appears", NavigationPlanner.Decide(readyLoadout,
            recognizer.Recognize(readyLoadout), profile).Plan?.Step == NavigationStep.LaunchLoadedHomebase);
        foreach (string missing in new[] { "NERO ABILITIES", "EDIT", "Mission Details", "Abandon" })
        {
            var incomplete = garbledLoadout with
            {
                Regions = garbledLoadout.Regions.Where(r => r.Text != missing).ToArray()
            };
            check("Loadout fallback requires all corroborating labels: " + missing,
                recognizer.Recognize(incomplete).Kind is not (ScreenKind.HomebasePreparing or ScreenKind.HomebaseReady));
        }
        var mode = Frame(0, Label("SAVE THE WORLD", 100, 900, 450), Label("PLAY", 100, 70), Label("PLAY", 270, 1100),
            Label("COMPETE", 800, 70), Label("CAREER", 1000, 70));
        var decision = NavigationPlanner.Decide(mode, recognizer.Recognize(mode), profile);
        check("Mode entry selects the Play button below its title", decision.Plan?.Step == NavigationStep.EnterSaveTheWorld && decision.Plan.Target.Y == 1100);
        check("An unspecified homebase cannot start navigation", NavigationPlanner.Decide(mode, recognizer.Recognize(mode), new()).RequiresAttention);
        var wrapped = Frame(0, Label("SAVE THE", 158, 697, 181, 22), Label("WORLD", 184, 732, 131, 22),
            Label("PLAY", 94, 58, 58, 16), Label("PLAV", 204, 858, 89, 24),
            Label("COMPETE", 655, 58, 106, 16), Label("CAREER", 797, 58, 88, 16));
        check("Wrapped STW title permits OCR PLAV only beneath its title", NavigationPlanner.Decide(wrapped,
            recognizer.Recognize(wrapped), profile).Plan is { Step: NavigationStep.EnterSaveTheWorld, Target.Y: 858 });
        var topOnly = wrapped with
        {
            Regions = wrapped.Regions.Where(r => r.Text != "PLAV").ToArray()
        };
        check("Wrapped title never selects top navigation Play", NavigationPlanner.Decide(topOnly, recognizer.Recognize(topOnly), profile).Plan is null);
        var unrelated = wrapped with
        {
            Regions = wrapped.Regions.Select(r => r.Text == "WORLD" ? r with { Bounds = new(800, 732, 131, 22) } : r).ToArray()
        };
        check("Separate WORLD text cannot complete a mode title", recognizer.Recognize(unrelated).Kind == ScreenKind.Unknown);
        var splitSave = mode with
        {
            Regions = mode.Regions.Select(r => r.Text == "SAVE THE WORLD" ? r with { Text = "SA VE THE WORLD" } : r).ToArray()
        };
        check("Split SAVE letters retain spatially checked mode entry", NavigationPlanner.Decide(splitSave, recognizer.Recognize(splitSave), profile).Plan?.Step == NavigationStep.EnterSaveTheWorld);
        var world = Frame(0, Label("MAP", 500, 100), Label("COMMAND", 700, 100), Label("ARMORY", 900, 100),
            Label("Twine Peaks", 200, 300, 400, 60), Label("Recommended Power", 200, 600), Label("SELECT", 2000, 1200));
        var playOthers = Frame(0, Label("MAP", 500, 100), Label("COMMAND", 700, 100), Label("ARMORY", 900, 100),
            Label("PLAV WITH OTHERS", 100, 170), Label("REWARDS", 50, 650), Label("ZOOM", 945, 930));
        check("Mission map tolerates observed PLAV heading with map corroboration", recognizer.Recognize(playOthers).Kind == ScreenKind.MissionMap);
        check("Play with others heading alone cannot identify mission map", recognizer.Recognize(Frame(0, Label("PLAV WITH OTHERS", 100, 170))).Kind == ScreenKind.Unknown);
        check("Selected configured zone permits entry", NavigationPlanner.Decide(world, recognizer.Recognize(world), profile).Plan?.Step == NavigationStep.EnterZone);
        var orderedZones = new[] { HomebaseZone.Stonewood, HomebaseZone.Plankerton, HomebaseZone.CannyValley, HomebaseZone.TwinePeaks };
        Observation ZoneFrame(HomebaseZone zone, double seconds) => world with
        {
            CapturedAt = epoch.AddSeconds(seconds),
            Regions = world.Regions.Select(r => r.Text == "Twine Peaks" ? r with { Text = NavigationPlanner.ZoneName(zone) } : r).ToArray(),
            Visuals = [new("next-zone", new(150, 300, 25, 50), .98, .4), new("previous-zone", new(100, 300, 25, 50), .98, .4)]
        };
        var fast = new AutomationController { ErrorRecoveryOnly = false, Profile = profile };
        fast.Start();
        for (int i = 0; i < 3; i++)
        {
            fast.Start();
            var current = ZoneFrame(orderedZones[i], i * 2.4);
            var next = orderedZones[i + 1];
            var plan = NavigationPlanner.Decide(current, recognizer.Recognize(current), profile).Plan;
            check("Forward chevron expects adjacent zone " + next, plan is { Step: NavigationStep.ChangeZone }
                && plan.Target == MenuGeometry.Target(NavigationStep.ChangeZone, current) && plan.Context == next.ToString());
            check("New zone target needs stable evidence " + next, fast.Observe(current, recognizer.Recognize(current)) is null);
            var stable = current with
            {
                CapturedAt = current.CapturedAt.AddMilliseconds(600)
            };
            var click = fast.Observe(stable, recognizer.Recognize(stable));
            check("Fast zone sequence dispatches at 600ms " + next, click is { Step: NavigationStep.ChangeZone } && click.Context == next.ToString());
            if (click is null)
            {
                continue;
            }

            fast.Dispatched(click, stable.CapturedAt);
            check("Old zone cannot confirm " + next, !NavigationPlanner.IsSatisfied(click, current, recognizer.Recognize(current)));
            var wrong = ZoneFrame(orderedZones[(i + 2) % 4], i * 2.4 + .9);
            check("Unexpected zone cannot confirm " + next, !NavigationPlanner.IsSatisfied(click, wrong, recognizer.Recognize(wrong)));
            var arrived = ZoneFrame(next, i * 2.4 + 1.2);
            fast.Observe(arrived, recognizer.Recognize(arrived));
            check("One destination frame cannot confirm " + next, fast.Pending is not null);
            arrived = arrived with
            {
                CapturedAt = arrived.CapturedAt.AddMilliseconds(600)
            };
            fast.Observe(arrived, recognizer.Recognize(arrived));
            check("Two destination frames confirm " + next, fast.Pending is null && fast.Status == ControllerStatus.Observing);
            var reverse = NavigationPlanner.Decide(arrived, recognizer.Recognize(arrived), profile with
            {
                Zone = HomebaseZone.Stonewood
            }).Plan;
            check("Reverse uses adjacent previous zone " + next, reverse is { Step: NavigationStep.ChangeZone }
                && reverse.Target == MenuGeometry.Target(NavigationStep.ChangeZone, arrived, false) && reverse.Context == orderedZones[i].ToString());
        }
        check("Destination has no extra chevron", NavigationPlanner.NextZoneToward(HomebaseZone.TwinePeaks, HomebaseZone.TwinePeaks) is null);
        check("Unspecified zone has no route", NavigationPlanner.NextZoneToward(HomebaseZone.Unspecified, HomebaseZone.TwinePeaks) is null);
        var locked = world with
        {
            Regions = world.Regions.Append(Label("Requires completing quest: Blasting Off", 200, 800, 700)).ToArray()
        };
        decision = NavigationPlanner.Decide(locked, recognizer.Recognize(locked), profile);
        check("Locked zone reports its requirement without clicking Select", decision.RequiresAttention && decision.Plan is null);
        var selected = Frame(0, Label("MAP", 500, 100), Label("COMMAND", 700, 100), Label("ARMORY", 900, 100),
            Label("HOMEBASE STORM SHIELD", 100, 200, 500), Label("Twine Peaks", 100, 300), Label("SELECT", 2000, 1200));
        var selectionClick = new ClickAction(Guid.NewGuid(), "Select homebase", ScreenKind.MissionMap, new(1000, 500, 40, 40),
            2560, 1440, epoch, Step: NavigationStep.SelectHomebase);
        check("Homebase selection confirms a changed heading within the same screen kind", NavigationPlanner.IsSatisfied(selectionClick, selected, recognizer.Recognize(selected)));
        var radar = selected with
        {
            Regions = [..selected.Regions.Select(r => r.Text == "HOMEBASE STORM SHIELD"
            ? r with { Text = "BUILD THE RADAR GRID" } : r),Label("REWARDS",50,500),Label("ZOOM",945,930)]
        };
        check("Selecting another green mission cannot confirm homebase selection", !NavigationPlanner.IsSatisfied(selectionClick,
            radar, new(ScreenKind.MissionMap, [], [])));
        var titleOnlyZone = Frame(0, Label("MAP", 500, 100), Label("COMMAND", 700, 100), Label("ARMORY", 900, 100),
            Label("TWINE PEAKS STORM SHIELD", 100, 170), Label("Twine PeakSv", 100, 230), Label("REWARDS", 50, 500), Label("ZOOM", 945, 930));
        check("Homebase map recognised when Select is covered", recognizer.Recognize(titleOnlyZone).Kind == ScreenKind.MissionMap);
        var missingControl = NavigationPlanner.Decide(titleOnlyZone, recognizer.Recognize(titleOnlyZone), profile);
        check("Full homebase heading uses the fixed icon target even when Select is covered",
            missingControl.Plan?.Step == NavigationStep.SelectHomebase && !missingControl.RequiresAttention);
        var visibleControl = titleOnlyZone with
        {
            Regions = titleOnlyZone.Regions.Append(Label("SELECT", 1500, 870)).ToArray()
        };
        check("Confirmed homebase click enables visible Select", NavigationPlanner.Decide(visibleControl, recognizer.Recognize(visibleControl), profile, homebaseClickConfirmed: true).Plan?.Step == NavigationStep.OpenHomebase);
        check("Homebase title still requires the icon click before Select",
            NavigationPlanner.Decide(visibleControl, recognizer.Recognize(visibleControl), profile).Plan?.Step == NavigationStep.SelectHomebase);
        var hovered = visibleControl with
        {
            Visuals = [new("homebase-icon", new(1000, 500, 60, 60), .98, .4)]
        };
        var noisyHeading = hovered with
        {
            Regions = hovered.Regions.Select(r => r.Normalized == "TWINE PEAKS STORM SHIELD"
            ? r with
            {
                Text = "TWINE PEAKS STORM SIUELD"
            } : r).ToArray()
        };
        check("SIUELD OCR heading still identifies the selected homebase", NavigationPlanner.HasSelectedHomebase(noisyHeading));
        check("Noisy shield heading enables Select after a confirmed click", NavigationPlanner.Decide(noisyHeading, recognizer.Recognize(noisyHeading), profile, true).Plan?.Step == NavigationStep.OpenHomebase);
        check("Noisy heading does not bypass the actual homebase click", NavigationPlanner.Decide(noisyHeading, recognizer.Recognize(noisyHeading), profile).Plan?.Step == NavigationStep.SelectHomebase);
        check("Noisy heading cannot enter a different configured zone", NavigationPlanner.Decide(noisyHeading, recognizer.Recognize(noisyHeading), profile with
        {
            Zone = HomebaseZone.Stonewood
        }, true).RequiresAttention);
        foreach (var invalidTitle in new[] { "TWINE PEAKS STORM FIELD", "TWINE PEAKS STORM SHIELD 2", "TWINE PEAKS STORM SHIELD ASSISTANCE", "BUILD THE RADAR GRID" })
        {
            var unrelatedHeading = noisyHeading with
            {
                Regions = [Label(invalidTitle, 100, 170)]
            };

            if (invalidTitle.Contains("SHIELD ", StringComparison.Ordinal))
            {
                unrelatedHeading = unrelatedHeading with
                {
                    Regions = [Label(invalidTitle.Replace("SHIELD", "SIUELD"), 100, 170)]
                };
            }

            check("Approximate homebase heading rejects unrelated or extended text: " + invalidTitle, !NavigationPlanner.HasSelectedHomebase(unrelatedHeading));
        }
        var clickSequence = new AutomationController { ErrorRecoveryOnly = false, Profile = profile };
        clickSequence.Start();
        ClickAction? ObserveHomebase(double seconds, Observation? frame = null)
        {
            var current = (frame ?? hovered) with
            {
                CapturedAt = epoch.AddSeconds(seconds)
            };
            return clickSequence.Observe(current, recognizer.Recognize(current));
        }
        ObserveHomebase(0);
        var triangle = ObserveHomebase(1)!;
        check("Hovered homebase still requires an actual triangle click", triangle?.Step == NavigationStep.SelectHomebase);
        var parking = NavigationPlanner.PostClickPointerDestination(hovered, triangle!);
        check("Homebase click moves pointer to the detected Select label", parking?.CenterX == 1600);
        var noSelect = hovered with
        {
            Regions = hovered.Regions.Where(r => r.Normalized != "SELECT").ToArray()
        };
        check("Missing Select clears hover onto the detected MAP tab", NavigationPlanner.PostClickPointerDestination(noSelect, triangle!)?.CenterY == 115);
        var noParking = noSelect with
        {
            Regions = noSelect.Regions.Where(r => r.Normalized != "MAP").ToArray()
        };
        check("Missing Select and MAP never invent a pointer destination", NavigationPlanner.PostClickPointerDestination(noParking, triangle!) is null);
        var visualSelect = noSelect with
        {
            Visuals = [new("select-control", new(1500, 870, 200, 30), .99, .3)]
        };
        check("Verified visual Select supports pointer clearing", NavigationPlanner.PostClickPointerDestination(visualSelect, triangle!) is not null);
        var twoSelects = hovered with
        {
            Regions = hovered.Regions.Append(Label("SELECT", 500, 870)).ToArray()
        };
        check("Ambiguous Select labels block pointer clearing", NavigationPlanner.PostClickPointerDestination(twoSelects, triangle!) is null);
        check("Resized frame cannot reuse a pointer destination", NavigationPlanner.PostClickPointerDestination(hovered with { Width = 1920 }, triangle!) is null);
        check("Other actions cannot request homebase pointer clearing", NavigationPlanner.PostClickPointerDestination(hovered, triangle! with { Step = NavigationStep.ChangeZone }) is null);
        var zoneClick = triangle! with
        {
            Step = NavigationStep.ChangeZone,
            SourceScreen = ScreenKind.WorldMap
        };
        check("Zone click parks on the observed MAP tab", NavigationPlanner.PostClickPointerDestination(hovered, zoneClick)?.CenterY == 115);
        check("Unreadable MAP parks zone cursor on observed Select", NavigationPlanner.PostClickPointerDestination(
            hovered with
            {
                Regions = hovered.Regions.Where(r => r.Normalized != "MAP").ToArray()
            }, zoneClick)?.CenterX == 1600);
        check("Zone click never invents a parking coordinate", NavigationPlanner.PostClickPointerDestination(noParking, zoneClick) is null);
        check("Resized zone frame invalidates parking", NavigationPlanner.PostClickPointerDestination(hovered with { Width = 1920 }, zoneClick) is null);
        var toggleFrame = hovered with
        {
            Regions = [Label("ENDURANCE", 400, 180, 400), Label("START ENDURANCE", 700, 700, 400)]
        };
        var toggle = triangle! with
        {
            Step = NavigationStep.DisableAssistance,
            SourceScreen = ScreenKind.EnduranceSetup,
            Target = new(600, 600, 30, 30)
        };
        check("Assistance toggle parks on the non-action heading", NavigationPlanner.PostClickPointerDestination(toggleFrame, toggle)?.CenterX == 600);
        check("Missing heading never parks on Start", NavigationPlanner.PostClickPointerDestination(toggleFrame with { Regions = toggleFrame.Regions.Where(r => r.Normalized != "ENDURANCE").ToArray() }, toggle) is null);
        clickSequence.TargetChanged(triangle!, epoch.AddSeconds(1));
        ObserveHomebase(4);
        triangle = ObserveHomebase(5)!;
        check("Rejected triangle click cannot authorize Select on next scan", triangle?.Step == NavigationStep.SelectHomebase && !clickSequence.HomebaseClickConfirmed);
        clickSequence.Dispatched(triangle!, epoch.AddSeconds(5));
        ObserveHomebase(5.5);
        check("One post-click frame cannot authorize Select", !clickSequence.HomebaseClickConfirmed);
        ObserveHomebase(6);
        check("Dispatched triangle plus two stable frames confirms click", clickSequence.HomebaseClickConfirmed);
        ObserveHomebase(6.3);
        var selectClick = ObserveHomebase(6.9);
        check("Next action after triangle confirmation is Select, without a second triangle click", selectClick?.Step == NavigationStep.OpenHomebase);
        var incompleteMap = hovered with
        {
            Regions = hovered.Regions.Where(r => !r.Normalized.Contains("STORM")).ToArray()
        };
        check("Incomplete map frame cannot restart triangle navigation", ObserveHomebase(7.2, incompleteMap) is null && clickSequence.HomebaseClickConfirmed);
        check("Different mission text cannot authorize Select or another triangle click", ObserveHomebase(7.5, radar) is null && clickSequence.HomebaseClickConfirmed);
        var persistentHover = radar with
        {
            Visuals = hovered.Visuals
        };
        check("Persistent changed heading clears stale homebase confirmation", ObserveHomebase(9.3, persistentHover) is null && !clickSequence.HomebaseClickConfirmed);
        check("Persistent hover reacquires the triangle instead of clicking the wrong mission", ObserveHomebase(9.4, persistentHover)?.Step == NavigationStep.SelectHomebase);
        clickSequence.Start();
        check("Restart requires fresh triangle click evidence", !clickSequence.HomebaseClickConfirmed);
        ObserveHomebase(0);
        var firstTriangle = ObserveHomebase(.1)!;
        clickSequence.Dispatched(firstTriangle, epoch.AddSeconds(.16));
        check("A transient neighbouring mission hover does not issue another click", ObserveHomebase(.2, radar) is null && clickSequence.Pending == firstTriangle);
        check("One homebase frame after clearing hover waits for confirmation", ObserveHomebase(.3) is null);
        check("Stable homebase after clearing hover immediately chains Select", ObserveHomebase(.4)?.Step == NavigationStep.OpenHomebase);
        clickSequence.Start();
        ObserveHomebase(0);
        firstTriangle = ObserveHomebase(.1)!;
        clickSequence.Dispatched(firstTriangle, epoch.AddSeconds(.16));
        check("Slow positive capture gets a second frame instead of a premature retry", ObserveHomebase(2.4) is null && clickSequence.Pending == firstTriangle);
        check("Delayed positive evidence still chains directly to Select", ObserveHomebase(2.6)?.Step == NavigationStep.OpenHomebase);
        clickSequence.Start();
        ObserveHomebase(0);
        firstTriangle = ObserveHomebase(.1)!;
        clickSequence.Dispatched(firstTriangle, epoch.AddSeconds(.16));
        check("Unchanged mission cannot immediately repeat a triangle click", ObserveHomebase(1, persistentHover) is null && clickSequence.Pending is not null);
        check("Unconfirmed triangle uses a short bounded wait", ObserveHomebase(2.2, persistentHover) is null && clickSequence.Pending is null);
        ObserveHomebase(2.4, persistentHover);
        var triangleRetry = ObserveHomebase(2.5, persistentHover);
        check("Triangle retry reacquires fresh evidence in under three seconds", triangleRetry?.Step == NavigationStep.SelectHomebase);
        clickSequence.Dispatched(triangleRetry!, epoch.AddSeconds(2.56));
        ObserveHomebase(4.6, persistentHover);
        check("Repeated failed selection pauses instead of clicking forever", clickSequence.Status == ControllerStatus.PausedForAttention);
        check("Different zone heading still blocks homebase entry", NavigationPlanner.Decide(visibleControl, recognizer.Recognize(visibleControl), profile with
        {
            Zone = HomebaseZone.Stonewood
        }).RequiresAttention);
        var own = Frame(0, Label("COMMUNITY LOOKOUT", 400, 100), Label("MY STORM SHIELD", 650, 350),
            Label("STORM SHIELD ASSIST", 650, 450), Label("LAUNCH", 1500, 1200));
        check("Own homebase choice is explicit when the checkmark is absent", NavigationPlanner.Decide(own, recognizer.Recognize(own), profile).Plan?.Step == NavigationStep.SelectOwnHomebase);
        var checkedOwn = own with
        {
            Visuals = [new("own-homebase-selected", new(1100, 350, 30, 30), .98, .4)]
        };
        check("Verified own-homebase selection enables Launch", NavigationPlanner.Decide(checkedOwn, recognizer.Recognize(checkedOwn), profile).Plan?.Step == NavigationStep.LaunchHomebase);
        var mergedOwn = own with
        {
            Regions = own.Regions.Where(r => r.Normalized != "MY STORM SHIELD")
            .Concat(new[]{Label("MY STORM SHIELD V Access your own Storm Shield",650,350,700),
                Label("MY",650,350,40),Label("STORM",700,350,90),Label("SHIELD",800,350,90)}).ToArray()
        };
        var mergedPlan = NavigationPlanner.Decide(mergedOwn, recognizer.Recognize(mergedOwn), profile).Plan;
        check("Recognised choice dialog uses the fixed own-shield row", mergedPlan?.Step == NavigationStep.SelectOwnHomebase
            && mergedPlan.Target == MenuGeometry.Target(NavigationStep.SelectOwnHomebase, mergedOwn));
        var mergedSelected = mergedOwn with
        {
            Visuals = checkedOwn.Visuals,
            Regions = mergedOwn.Regions.Select(r => r.Text == "LAUNCH" ? r with { Text = "LAUNCN" } : r).ToArray()
        };
        check("Selected own-homebase row accepts known Launch font variant", NavigationPlanner.Decide(mergedSelected, recognizer.Recognize(mergedSelected), profile).Plan?.Step == NavigationStep.LaunchHomebase);
        var wideExact = mergedOwn with
        {
            Regions = mergedOwn.Regions.Append(Label("MY STORM SHIELD", 650, 350, 500)).ToArray()
        };
        check("Word bounds exclude an OCR-attached checkmark from the visual anchor", AutomationController.UniqueTarget(HomebaseChoiceControls.OwnLabels(wideExact))?.Right == 890);
        var scattered = mergedOwn with
        {
            Regions = mergedOwn.Regions.Select(r => r.Text == "STORM" ? r with { Bounds = r.Bounds with { Y = 500 } } : r).ToArray()
        };
        check("Different rows cannot manufacture a My Storm Shield target", HomebaseChoiceControls.OwnLabels(scattered).Count == 0);
        var repeated = mergedOwn with
        {
            Regions = mergedOwn.Regions.Concat(new[] { Label("MY", 650, 650, 40), Label("STORM", 700, 650, 90), Label("SHIELD", 800, 650, 90) }).ToArray()
        };
        check("Two own-homebase rows remain ambiguous", NavigationPlanner.Decide(repeated, recognizer.Recognize(repeated), profile).Plan is null);
        var responsive = new AutomationController { ErrorRecoveryOnly = false, Profile = profile };
        responsive.Start();
        responsive.Observe(own, recognizer.Recognize(own));
        var selectOwn = responsive.Observe(own with
        {
            CapturedAt = epoch.AddMilliseconds(500)
        }, recognizer.Recognize(own))!;
        responsive.Dispatched(selectOwn, epoch.AddMilliseconds(500));
        responsive.Observe(checkedOwn with
        {
            CapturedAt = epoch.AddMilliseconds(750)
        }, recognizer.Recognize(checkedOwn));
        var chainedLaunch = responsive.Observe(checkedOwn with
        {
            CapturedAt = epoch.AddMilliseconds(1250)
        }, recognizer.Recognize(checkedOwn));
        check("Confirmed My Storm Shield selection clears its pending action", responsive.Pending is null);
        check("Launch reuses two freshly observed destination targets", chainedLaunch?.Step == NavigationStep.LaunchHomebase);
        check("Chained launch belongs to the current frame", chainedLaunch?.ObservedAt == epoch.AddMilliseconds(1250));
        check("Zone-change animation keeps fast polling", NavigationTiming.PollMilliseconds(ScreenKind.Unknown, NavigationStep.ChangeZone) == 50);
        check("Mission entry animation keeps fast polling", NavigationTiming.PollMilliseconds(ScreenKind.Unknown, NavigationStep.EnterZone) == 50);
        check("Map and lobby use fast polling", NavigationTiming.PollMilliseconds(ScreenKind.WorldMap, null) == 50
            && NavigationTiming.PollMilliseconds(ScreenKind.SaveTheWorldLobby, null) == 50);
        check("Gameplay retains slower polling", NavigationTiming.PollMilliseconds(ScreenKind.ActiveEndurance, null) == 2500);
        check("Unknown idle screen retains slower polling", NavigationTiming.PollMilliseconds(ScreenKind.Unknown, null) == 2500);
        check("Error recovery cooldown stays unchanged", NavigationTiming.TransitionCooldown(null) == TimeSpan.FromSeconds(5));
        var ready = Frame(0, Label("TWINE PEAKS STORM SNIELD", 100, 200), Label("NERO LOADOUT", 100, 350),
            Label("MAP", 500, 100), Label("COMMAND", 700, 100), Label("ARMORY", 900, 100), Label("LAUNCH", 2000, 1200));
        check("Mission Launch does not require a readable countdown", recognizer.Recognize(ready).Kind == ScreenKind.HomebaseReady
            && NavigationPlanner.Decide(ready, recognizer.Recognize(ready), profile).Plan?.Step == NavigationStep.LaunchLoadedHomebase);
        var delayed = new AutomationController { ErrorRecoveryOnly = false, Profile = profile };
        delayed.Start();
        var mapWithIcon = selected with
        {
            Visuals = [new("homebase-icon", new(1000, 500, 40, 40), .99, .3)]
        };
        delayed.Observe(mapWithIcon, recognizer.Recognize(mapWithIcon));
        var delayedClick = delayed.Observe(mapWithIcon with
        {
            CapturedAt = epoch.AddMilliseconds(100)
        }, recognizer.Recognize(mapWithIcon))!;
        delayed.Dispatched(delayedClick, epoch.AddMilliseconds(160));
        check("A single ready frame cannot supersede a pending map click", delayed.Observe(ready with { CapturedAt = epoch.AddMilliseconds(250) }, recognizer.Recognize(ready)) is null && delayed.Pending is not null);
        var immediateLaunch = delayed.Observe(ready with
        {
            CapturedAt = epoch.AddMilliseconds(400)
        }, recognizer.Recognize(ready));
        check("Verified ready screen clears stale map action and launches", immediateLaunch?.Step == NavigationStep.LaunchLoadedHomebase && delayed.Pending is null && !delayed.HomebaseClickConfirmed);
        foreach (var step in new[] { NavigationStep.SelectHomebase, NavigationStep.OpenHomebase, NavigationStep.SelectOwnHomebase })
        {
            var pendingAction = selectionClick with
            {
                Step = step
            };
            check("Advanced homebase reconciles " + step, NavigationPlanner.IsSatisfied(pendingAction, ready, recognizer.Recognize(ready)));
            check("Unknown cannot reconcile " + step, !NavigationPlanner.IsSatisfied(pendingAction, ready, ScreenEvidence.Unknown("unreadable")));
            check("Generic loading cannot reconcile " + step, !NavigationPlanner.IsSatisfied(pendingAction, ready, new(ScreenKind.Loading, [], [])));
        }
        var preparing = ready with
        {
            Regions = ready.Regions.Where(r => r.Text != "LAUNCH").ToArray()
        };
        check("Loadout without Launch cannot fall through into map navigation", recognizer.Recognize(preparing).Kind == ScreenKind.HomebasePreparing
            && NavigationPlanner.Decide(preparing, recognizer.Recognize(preparing), profile).Plan is null);
        var instruction = preparing with
        {
            Regions = preparing.Regions.Append(Label("WAIT FOR LAUNCH", 2000, 1000)).ToArray()
        };
        check("Launch instruction is not a Launch control", recognizer.Recognize(instruction).Kind == ScreenKind.HomebasePreparing);
        var ambiguousLaunch = ready with
        {
            Regions = ready.Regions.Append(Label("LAUNCH", 1000, 1000)).ToArray()
        };
        check("Two different Launch controls remain ambiguous", NavigationPlanner.Decide(ambiguousLaunch, recognizer.Recognize(ambiguousLaunch), profile).Plan is null);
        check("Launch alone does not identify a mission", recognizer.Recognize(Frame(0, Label("LAUNCH", 1000, 1000))).Kind == ScreenKind.Unknown);
        var otherMission = ready with
        {
            Regions = ready.Regions.Where(r => !r.Text.Contains("STORM")).Concat(new[] { Label("EDIT", 100, 600), Label("HERO ABILITIES", 100, 450) }).ToArray()
        };
        check("Unreadable or other mission heading waits without launching or reopening Map", recognizer.Recognize(otherMission).Kind == ScreenKind.HomebasePreparing
            && NavigationPlanner.Decide(otherMission, recognizer.Recognize(otherMission), profile).Plan is null);
        var noisyLoadout = otherMission with
        {
            Regions = otherMission.Regions.Select(r => r.Text == "HERO ABILITIES" ? r with { Text = "MERO ABILITIES" } : r).ToArray()
        };
        check("Noisy abilities label keeps loadout eligible for detail OCR", recognizer.Recognize(noisyLoadout).Kind == ScreenKind.HomebasePreparing
            && NavigationPlanner.Decide(noisyLoadout, recognizer.Recognize(noisyLoadout), profile).Plan is null);
        var detailsLoadout = noisyLoadout with
        {
            Regions = noisyLoadout.Regions.Where(r => r.Text != "MERO ABILITIES").Concat(new[] { Label("Mission Details", 100, 650), Label("Abandon", 500, 950) }).ToArray()
        };
        check("Mission details and Abandon corroborate an unreadable loadout heading", recognizer.Recognize(detailsLoadout).Kind == ScreenKind.HomebasePreparing
            && NavigationPlanner.Decide(detailsLoadout, recognizer.Recognize(detailsLoadout), profile).Plan is null);
        var missingDetails = detailsLoadout with
        {
            Regions = detailsLoadout.Regions.Where(r => r.Text != "Abandon").ToArray()
        };
        check("Incomplete loadout fallback cannot authorize Launch", NavigationPlanner.Decide(missingDetails, recognizer.Recognize(missingDetails), profile).Plan?.Step != NavigationStep.LaunchLoadedHomebase);
        var recoveredLoadout = noisyLoadout with
        {
            Regions = noisyLoadout.Regions.Append(Label("TWINE PEAKS STORM SHIELD", 100, 200)).ToArray()
        };
        check("Recovered Storm Shield heading enables loaded-mission Launch", NavigationPlanner.Decide(recoveredLoadout, recognizer.Recognize(recoveredLoadout), profile).Plan?.Step == NavigationStep.LaunchLoadedHomebase);
        var loadSequence = new AutomationController { ErrorRecoveryOnly = false, Profile = profile };
        loadSequence.Start();
        ClickAction? ObserveLoad(Observation source, double seconds)
        {
            var current = source with
            {
                CapturedAt = epoch.AddSeconds(seconds)
            };
            return loadSequence.Observe(current, recognizer.Recognize(current));
        }
        ObserveLoad(checkedOwn, 0);
        var firstLaunch = ObserveLoad(checkedOwn, .6)!;
        loadSequence.Dispatched(firstLaunch, epoch.AddSeconds(.6));
        ObserveLoad(preparing, 1.2);
        ObserveLoad(preparing, 1.8);
        check("Preparing screen confirms first Launch", loadSequence.Pending is null);
        check("Second Launch still needs stable evidence", ObserveLoad(ready, 2.4) is null);
        var secondLaunch = ObserveLoad(ready, 3);
        check("Second Launch is dispatched promptly without five-second cooldown", secondLaunch?.Step == NavigationStep.LaunchLoadedHomebase);
        if (secondLaunch is not null)
        {
            loadSequence.Dispatched(secondLaunch, epoch.AddSeconds(3));
            check("Visible Launch is not clicked repeatedly while awaiting loading", ObserveLoad(ready, 3.6) is null && loadSequence.Pending is not null);
            var loading = Frame(0, Label("LOADING", 1000, 1000));
            ObserveLoad(loading, 4.2);
            ObserveLoad(loading, 4.8);
            check("Loading confirms second Launch", loadSequence.Pending is null);
        }
        check("Low-margin icons cannot authorize navigation", NavigationPlanner.ReliableVisual(own with
        {
            Visuals = [new("homebase-icon", new(1000, 500, 40, 40), .97, .01)]
        }, "homebase-icon") is null);
        var setup = Frame(0, Label("STORM SHIELD", 100, 100), Label("START ENDURANCE", 500, 600));
        check("Endurance start requires agreed in-run readiness", NavigationPlanner.Decide(setup, recognizer.Recognize(setup), profile with
        {
            AfkBaseConfirmed = false
        }).RequiresAttention);

        var controller = new AutomationController { ErrorRecoveryOnly = false, Profile = profile };
        controller.Start();
        controller.Observe(mode, recognizer.Recognize(mode));
        var later = mode with
        {
            CapturedAt = epoch.AddSeconds(1)
        };
        var action = controller.Observe(later, recognizer.Recognize(later))!;
        controller.Dispatched(action, epoch.AddSeconds(1));
        var error = Frame(2, Label("NETWORK CONNECTION LOST", 500, 400), Label("CONTINUE", 600, 800));
        controller.Observe(error, recognizer.Recognize(error));
        check("An error supersedes a pending navigation wait immediately", controller.Status == ControllerStatus.Observing && controller.Pending is null);
        var errorNext = error with
        {
            CapturedAt = epoch.AddSeconds(3)
        };
        check("Recovery still requires a second observation after navigation fails", controller.Observe(errorNext, recognizer.Recognize(errorNext)) is { Step: null });

        controller.Start();
        for (int rejection = 0; rejection < 3; rejection++)
        {
            int time = rejection * 6;
            var first = mode with
            {
                CapturedAt = epoch.AddSeconds(time)
            };
            check("Changed target requires new first observation " + rejection, controller.Observe(first, recognizer.Recognize(first)) is null);
            var second = first with
            {
                CapturedAt = first.CapturedAt.AddSeconds(1)
            };
            var proposal = controller.Observe(second, recognizer.Recognize(second))!;
            check("Changed target reacquires after stable evidence " + rejection, proposal is not null);
            controller.TargetChanged(proposal!, second.CapturedAt);
            check("Rejected click has no pending transition " + rejection, controller.Pending is null);
            if (rejection < 2)
            {
                var early = first with
                {
                    CapturedAt = first.CapturedAt.AddSeconds(2)
                };
                check("Changed target cooldown blocks immediate retry " + rejection, controller.Observe(early, recognizer.Recognize(early)) is null);
            }
        }
        check("Repeated target changes pause after three rejections", controller.Status == ControllerStatus.PausedForAttention);
    }

    public static void RunCaptures(string directory, Action<string, bool> check)
    {
        var json = new System.Text.Json.JsonSerializerOptions();
        json.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        foreach (var (name, step) in new[]
        {
            ("stonewood-missions-native", NavigationStep.SelectHomebase),
            ("homebase-launch-native", NavigationStep.LaunchHomebase),
            ("world-map-stonewood-native", NavigationStep.EnterZone),
            ("mode-stw-selected-native", NavigationStep.EnterSaveTheWorld)
        })
        {
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, name + ".json")));
            var frame = document.RootElement.GetProperty("Observation").DeserializeObservation(json);
            var decision = NavigationPlanner.Decide(frame, new ScreenRecognizer().Recognize(frame), new(HomebaseZone.Stonewood));
            check("Captured navigation step: " + name, decision.Plan?.Step == step);
        }
        using var locked = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "world-map-twine-locked-native.json")));
        var observation = locked.RootElement.GetProperty("Observation").DeserializeObservation(json);
        var rejected = NavigationPlanner.Decide(observation, new ScreenRecognizer().Recognize(observation), new(HomebaseZone.TwinePeaks));
        check("Captured locked Twine Peaks stops entry", rejected.Plan is null && rejected.RequiresAttention);
    }
}
