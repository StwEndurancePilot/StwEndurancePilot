namespace EndurancePilot.App;

static class Program
{

    [STAThread]
    static void Main(string[] args)
    {

        ApplicationConfiguration.Initialize();
        CompactNativeLibrary.Initialize();
        if (args.Length == 4 && args[0] == "--verify-end-game")
        {
            try
            {
                EndGameSequenceChecks.Run(args[1], args[2], args[3]).GetAwaiter().GetResult();
            }
            catch (Exception error)
            {
                Directory.CreateDirectory(args[3]);
                File.WriteAllText(Path.Combine(args[3], "error.txt"), error.ToString());
                Environment.ExitCode = 1;
            }
            return;
        }
        if (args.Length == 3 && args[0] == "--preview-reward-receipts")
        {
            RewardReceiptChecks.Preview(args[1], args[2]).GetAwaiter().GetResult();
            return;
        }
        if (args.Length == 3 && args[0] == "--verify-accessibility")
        {
            AccessibilityChecks.Run(args[1], args[2]).GetAwaiter().GetResult();
            return;
        }
        if (args.Length == 3 && args[0] == "--verify-health")
        {
            HealthBarChecks.Run(args[1], args[2]);
            return;
        }
        if (args.Length == 3 && args[0] == "--verify-map-select")
        {
            try
            {
                RouteChecks.Run(args[1], args[2], selectedOnly: true).GetAwaiter().GetResult();
            }
            catch (Exception error)
            {
                Directory.CreateDirectory(args[2]);
                File.WriteAllText(Path.Combine(args[2], "error.txt"), error.ToString());
                Environment.ExitCode = 1;
            }
            return;
        }
        if (args.Length == 3 && args[0] == "--verify-discover")
        {
            DiscoveryChecks.Run(args[1], args[2]).GetAwaiter().GetResult();
            return;
        }
        if (args.Length == 3 && args[0] == "--verify-scroll-rewards")
        {
            try
            {
                ScrollRewardChecks.Run(args[1], args[2]).GetAwaiter().GetResult();
            }
            catch (Exception error)
            {
                Directory.CreateDirectory(args[2]);
                File.WriteAllText(Path.Combine(args[2], "error.txt"), error.ToString());
                Environment.ExitCode = 1;
            }
            return;
        }
        if (args.Length == 2 && args[0] == "--preview-settings")
        {
            SettingsPreview.Render(args[1]);
            return;
        }
        if (args.Length == 2 && args[0] == "--self-check")
        {
            Directory.CreateDirectory(args[1]);
            var checks = StartupChecks.Run().Concat(SettingsChecks.Run(args[1])).Concat(RewardEarningsChecks.Run(args[1])).Concat(RewardReceiptChecks.Run(args[1])).Concat(WebhookChecks.RunAsync().GetAwaiter().GetResult())
                .Concat(UpdateChecks.RunAsync().GetAwaiter().GetResult())
                .Concat(KeyPressChecks.RunAsync().GetAwaiter().GetResult()).Concat(WaveNotificationChecks.Run()).ToArray();
            File.WriteAllText(Path.Combine(args[1], "startup-checks.json"), System.Text.Json.JsonSerializer.Serialize(checks, Inspection.Json));
            Environment.ExitCode = checks.All(c => c.Passed) ? 0 : 1;
            return;
        }
        if (args.Length == 3 && args[0] is "--analyze" or "--verify-sleep" or "--verify-enhancement" or "--probe-enhancement" or "--verify-rejoin" or "--verify-recovery" or "--prepare-anchors" or "--verify-navigation" or "--verify-rewards" or "--verify-waves" or "--verify-console" or "--verify-route" or "--verify-display" or "--verify-backpack" or "--probe-hud" or "--probe-smalltext" or "--probe-objective" or "--benchmark-navigation" or "--probe-padded-hud")
        {
            try
            {
                if (args[0] == "--analyze")
                {
                    Inspection.AnalyzeFile(args[1], args[2]).GetAwaiter().GetResult();
                }
                else if (args[0] == "--verify-sleep")
                {
                    SleepChecks.Run(args[1], args[2]).GetAwaiter().GetResult();
                }
                else if (args[0] == "--verify-enhancement")
                {
                    EnhancementChecks.Run(args[1], args[2]).GetAwaiter().GetResult();
                }
                else if (args[0] == "--probe-enhancement")
                {
                    EnhancementProbe.Run(args[1], args[2]).GetAwaiter().GetResult();
                }
                else if (args[0] == "--verify-rejoin")
                {
                    RejoinChecks.Run(args[1], args[2]).GetAwaiter().GetResult();
                }
                else if (args[0] == "--benchmark-navigation")
                {
                    NavigationPerformanceChecks.Run(args[1], args[2]).GetAwaiter().GetResult();
                }
                else if (args[0] == "--probe-objective")
                {
                    ObjectiveProbe.Run(args[1], args[2]).GetAwaiter().GetResult();
                }
                else if (args[0] == "--probe-smalltext")
                {
                    SmallTextDiagnostics.Run(args[1], args[2]).GetAwaiter().GetResult();
                }
                else if (args[0] == "--probe-hud")
                {
                    HudDiagnostics.Run(args[1], args[2]).GetAwaiter().GetResult();
                }
                else if (args[0] == "--probe-padded-hud")
                {
                    PaddedHudProbe.Run(args[1], args[2]).GetAwaiter().GetResult();
                }
                else if (args[0] == "--prepare-anchors")
                {
                    VisualRecognizer.PrepareAssets(args[1], args[2]);
                }
                else if (args[0] == "--verify-navigation")
                {
                    NavigationChecks.Run(args[1], args[2]).GetAwaiter().GetResult();
                }
                else if (args[0] == "--verify-rewards")
                {
                    RewardChecks.Run(args[1], args[2]).GetAwaiter().GetResult();
                }
                else if (args[0] == "--verify-waves")
                {
                    WaveChecks.Run(args[1], args[2]).GetAwaiter().GetResult();
                }
                else if (args[0] == "--verify-console")
                {
                    ConsoleChecks.Run(args[1], args[2]).GetAwaiter().GetResult();
                }
                else if (args[0] == "--verify-route")
                {
                    RouteChecks.Run(args[1], args[2]).GetAwaiter().GetResult();
                }
                else if (args[0] == "--verify-display")
                {
                    DisplayChecks.Run(args[1], args[2]).GetAwaiter().GetResult();
                }
                else if (args[0] == "--verify-backpack")
                {
                    BackpackChecks.Run(args[1], args[2]).GetAwaiter().GetResult();
                }
                else
                {
                    RecoveryChecks.Run(args[1], args[2]).GetAwaiter().GetResult();
                }
            }
            catch (Exception error)
            {
                Directory.CreateDirectory(args[2]);
                File.WriteAllText(Path.Combine(args[2], "error.txt"), error.ToString());
                Environment.ExitCode = 1;
            }
            return;
        }
        using var instance = new Mutex(true, @"Local\EndurancePilot.Runner", out bool firstInstance);
        if (!firstInstance)
        {
            MessageBox.Show("Endurance Pilot is already open. Use its existing window to start or stop the runner.", "Endurance Pilot", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            var app = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnMainWindowClose };
            app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary());
            app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
            app.Run(new PilotWindow());
        }
        catch (Exception error)
        {
            MessageBox.Show("Endurance Pilot could not start:\n\n" + error.Message, "Endurance Pilot", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Environment.ExitCode = 1;
        }
        finally { instance.ReleaseMutex(); }
    }
}
