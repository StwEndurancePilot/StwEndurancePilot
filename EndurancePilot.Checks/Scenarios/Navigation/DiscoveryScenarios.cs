using EndurancePilot.Core;

internal static class DiscoveryScenarios
{
    internal static void Run(Action<string, bool> check, DateTimeOffset epoch)
    {
        var recognizer = new ScreenRecognizer();
        var profile = new RunnerProfile(HomebaseZone.TwinePeaks, true);
        TextRegion Label(string text, double x, double y, double w = 120, double h = 24) => new(text, new(x, y, w, h));
        Observation browser = new(epoch, 1920, 1080, [Label("Search Discover",100,150,280),Label("DISCOVER",950,150),
            Label("FOLLOWING",1130,150),Label("BV EPIC",1330,150),Label("CATEGORIES",1660,150)]);
        NavigationPlan? Plan(Observation f) => NavigationPlanner.Decide(f, recognizer.Recognize(f), profile).Plan;
        check("Discover recognises observed BV EPIC spelling", recognizer.Recognize(browser).Kind == ScreenKind.DiscoverBrowser);
        check("Discover first selects By Epic", Plan(browser)?.Step == NavigationStep.FilterByEpic);
        var selected = browser with
        {
            Visuals = [new("by-epic-selected", new(1330, 150, 120, 24), 1, 1)]
        };
        check("Selected By Epic permits search in detected field", Plan(selected) is { Step: NavigationStep.SearchSaveTheWorld, Target.X: 100, Context: DiscoveryNavigation.Query });
        var results = selected with
        {
            Regions = selected.Regions.Select(r => r.Text == "Search Discover" ? r with { Text = "Q Save The World" } : r).Append(Label("Islands Results", 70, 350, 220)).ToArray()
        };
        check("Search icon is not mistaken for query text", DiscoveryNavigation.HasQuery(results));
        check("Query text alone cannot select a result", Plan(results) is null);
        var tile = new VisualRegion("stw-search-tile", new(70, 405, 312, 137), .98, .3);
        results = results with
        {
            Visuals = [selected.Visuals![0], tile]
        };
        check("Official artwork selects matching result", Plan(results) is { Step: NavigationStep.SelectSaveTheWorld, Target.X: 70 });
        check("Ambiguous official artwork blocks selection", Plan(results with { Visuals = [selected.Visuals![0], tile with { Margin = .02 }] }) is null);
        check("Unselected By Epic is selected before using results", Plan(results with
        {
            Visuals = [tile]
        })?.Step == NavigationStep.FilterByEpic);
        var wrongQuery = results with
        {
            Regions = results.Regions.Select(r => r.Text == "Q Save The World" ? r with { Text = "Battle Royale" } : r).ToArray()
        };
        check("An old search is replaced before choosing a tile", Plan(wrongQuery)?.Step == NavigationStep.SearchSaveTheWorld);
        var noField = selected with
        {
            Regions = selected.Regions.Where(r => r.Text != "Search Discover").ToArray()
        };
        check("Missing search field remains inert", recognizer.Recognize(noField).Kind == ScreenKind.Unknown && Plan(noField) is null);
        var details = new Observation(epoch, 1920, 1080, [Label("Created by Epic",80,55,220),Label("SAVE THE WORLD",80,90,500,40),
            Label("About Island",1500,200),Label("By Epic",1400,420),Label("SELECT",190,900)]);
        check("Epic details must be selected before Play", Plan(details)?.Step == NavigationStep.ConfirmSaveTheWorld);
        check("A different creator cannot be selected", Plan(details with { Regions = details.Regions.Select(r => r.Text == "Created by Epic" ? r with { Text = "Created by Someone" } : r).ToArray() }) is null);
        var lobby = new Observation(epoch, 1920, 1080, [Label("PLAY", 80, 50), Label("SHOP", 200, 50), Label("LOCKER", 300, 50), Label("COMPETE", 690, 50), Label("CAREER", 840, 50), Label("BATTLE ROYALE", 80, 740, 300), Label("PLAY", 180, 900)]);
        check("Different mode is recognised without pressing its Play", recognizer.Recognize(lobby).Kind == ScreenKind.FortniteLobby && Plan(lobby) is null);
        check("Lobby tolerates unreadable Career with four ordered tabs", recognizer.Recognize(lobby with
        {
            Regions = lobby.Regions.Where(r => r.Text != "CAREER").ToArray()
        }).Kind == ScreenKind.FortniteLobby);
        check("Three tabs alone cannot establish lobby", !DiscoveryNavigation.HasLobbyNavigation(lobby with
        {
            Regions = lobby.Regions.Where(r => r.Text is not ("CAREER" or "COMPETE")).ToArray()
        }));
        check("Unaligned labels cannot establish lobby navigation", !DiscoveryNavigation.HasLobbyNavigation(lobby with
        {
            Regions = lobby.Regions.Select(r => r.Text == "LOCKER" ? r with { Bounds = new(300, 500, 100, 30) } : r).ToArray()
        }));
        lobby = lobby with
        {
            Visuals = [new("discover-chevron", new(970, 1040, 45, 28), .99, .2)]
        };
        check("Different mode opens detected bottom chevron", Plan(lobby)?.Step == NavigationStep.OpenDiscover);
        var visibleTile = lobby with
        {
            Visuals = [.. lobby.Visuals!, new("stw-discovery-tile", new(80, 950, 180, 100), .94, .2)]
        };
        check("Visible STW carousel tile takes priority over Discover", Plan(visibleTile)?.Step == NavigationStep.SelectSaveTheWorld);
        var tileText = visibleTile with
        {
            Regions = visibleTile.Regions.Append(Label("SAVE THE WORLD", 80, 950, 180)).ToArray()
        };
        check("Tile text cannot cause entry without a Play button beneath it", Plan(tileText)?.Step == NavigationStep.SelectSaveTheWorld);
        check("Ambiguous carousel artwork falls back to the chevron", Plan(visibleTile with
        {
            Visuals = [lobby.Visuals![0], visibleTile.Visuals![1] with { Margin = .01 }]
        })?.Step == NavigationStep.OpenDiscover);
        var search = Plan(selected)!;
        var action = new ClickAction(Guid.NewGuid(), search.Description, ScreenKind.DiscoverBrowser, search.Target, 1920, 1080, epoch, Step: search.Step, Context: search.Context);
        check("Only fixed Discover search text is allowed", DiscoveryNavigation.IsSearchAction(action) && !DiscoveryNavigation.IsSearchAction(action with
        {
            Context = "other"
        }) && !DiscoveryNavigation.IsSearchAction(action with
        {
            SourceScreen = ScreenKind.ActiveEndurance
        }));
        check("Search completion requires results and the matching query", NavigationPlanner.IsSatisfied(action, results, recognizer.Recognize(results)) && !NavigationPlanner.IsSatisfied(action, wrongQuery, recognizer.Recognize(wrongQuery)) && !NavigationPlanner.IsSatisfied(action, selected, recognizer.Recognize(selected)));
        var controller = new AutomationController { Profile = profile, ErrorRecoveryOnly = false };
        controller.Start();
        check("One browser reading does not dispatch", controller.Observe(browser, recognizer.Recognize(browser)) is null);
        var later = browser with
        {
            CapturedAt = epoch.AddMilliseconds(150)
        };
        var click = controller.Observe(later, recognizer.Recognize(later));
        check("Two stable readings dispatch By Epic", click?.Step == NavigationStep.FilterByEpic);
        controller.Dispatched(click!, later.CapturedAt);
        for (int i = 1; i <= 2; i++)
        {
            var f = selected with
            {
                CapturedAt = epoch.AddMilliseconds(150 + i * 150)
            };
            action = controller.Observe(f, recognizer.Recognize(f))!;
        }
        check("Confirmed filter transitions directly into search", action?.Step == NavigationStep.SearchSaveTheWorld);
        controller.Stop();
        check("Stop blocks Discover typing", controller.Observe(selected with { CapturedAt = epoch.AddSeconds(2) }, recognizer.Recognize(selected)) is null);
        var carousel = lobby with
        {
            Regions = lobby.Regions.Append(Label("Current Lobby", 80, 820, 230)).ToArray()
        };
        var stw = lobby with
        {
            Regions = lobby.Regions.Select(r => r.Text == "BATTLE ROYALE" ? r with { Text = "SAVE THE WORLD" } : r).ToArray()
        };
        var tileClick = new ClickAction(Guid.NewGuid(), "Select STW", ScreenKind.FortniteLobby, new(80, 950, 180, 100), 1920, 1080, epoch, Step: NavigationStep.SelectSaveTheWorld);
        check("Unchanged carousel cannot confirm tile selection", !NavigationPlanner.IsSatisfied(tileClick, tileText, recognizer.Recognize(tileText)));
        check("STW title and its Play button confirm carousel selection", NavigationPlanner.IsSatisfied(tileClick, stw, recognizer.Recognize(stw)));
        check("Selected STW still enters when Career is unreadable", Plan(stw with
        {
            Regions = stw.Regions.Where(r => r.Text != "CAREER").ToArray()
        })?.Step == NavigationStep.EnterSaveTheWorld);
        (Observation Frame, NavigationStep Step)[] route = [(lobby,NavigationStep.OpenDiscover),(carousel,NavigationStep.OpenDiscover),
            (browser,NavigationStep.FilterByEpic),(selected,NavigationStep.SearchSaveTheWorld),(results,NavigationStep.SelectSaveTheWorld),
            (details,NavigationStep.ConfirmSaveTheWorld),(stw,NavigationStep.EnterSaveTheWorld)];
        controller.Start();
        var time = epoch.AddSeconds(10);
        foreach (var leg in route)
        {
            ClickAction? next = null;
            for (int i = 0; i < 20 && next is null; i++)
            {
                time = time.AddMilliseconds(150);
                var frame = leg.Frame with
                {
                    CapturedAt = time
                };
                next = controller.Observe(frame, recognizer.Recognize(frame));
            }
            check("Discovery route dispatches " + leg.Step + " from " + recognizer.Recognize(leg.Frame).Kind, next?.Step == leg.Step);
            controller.Dispatched(next!, time);
        }
        foreach (var kind in new[] { ScreenKind.FortniteLobby, ScreenKind.DiscoverBrowser, ScreenKind.DiscoverResults, ScreenKind.SaveTheWorldDetails })
        {
            var tracker = new RunTracker();
            for (int t = 0; t < 2; t++)
            {
                tracker.Observe(new(epoch.AddSeconds(t), 1920, 1080, [Label("Endurance: Wave 2", 80, 200, 300)]), new(ScreenKind.ActiveEndurance, [], []));
            }

            tracker.Observe(browser with
            {
                CapturedAt = epoch.AddSeconds(2)
            }, new(kind, [], []));
            var outcome = tracker.Observe(browser with
            {
                CapturedAt = epoch.AddSeconds(3)
            }, new(kind, [], []));
            check("Returned " + kind + " interrupts tracked mission", outcome?.Outcome == RunOutcome.Interrupted);
        }
    }
}
