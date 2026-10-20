using EndurancePilot.Core;

internal static class CrashRecoveryScenarios
{
    public static void Run(Action<string, bool> check, DateTimeOffset epoch)
    {
        var hang = new CrashDialogEvidence(100, 101, 1234, "FortniteClient-Win64-Shipping", "Application Hang Detected",
            "The application has hung and will now close. We apologize for the inconvenience.", true);
        var recovery = new CrashRecovery();
        CrashSnapshot Frame(double time, params CrashDialogEvidence[] dialogs) => new(epoch.AddSeconds(time), true, false, dialogs);
        CrashSnapshot Absent(double time) => new(epoch.AddSeconds(time), false, false, []);
        check("Exact Fortnite hang dialog is recognized independently of screenshots", CrashRecovery.IsKnownCrash(hang));
        check("Wrapped native hang text is recognized", CrashRecovery.IsKnownCrash(hang with
        {
            Message = hang.Message.Replace(" and ", "\r\nand ")
        }));
        check("Exact Fortnite crash dialog is recognized", CrashRecovery.IsKnownCrash(hang with
        {
            Title = "Application Crash Detected",
            Message = hang.Message.Replace("hung", "crashed")
        }));
        foreach (var invalid in new[] { hang with { ProcessName = "SomeOtherGame" }, hang with { Title = "Error" },
            hang with { Message = "The application has hung" }, hang with { Message = hang.Message + " Click to upload logs." },
            hang with { ButtonEnabled = false }, hang with { WindowId = 0 }, hang with { ButtonId = 0 }, hang with { ProcessId = 0 } })
        {
            check("Unverified crash identity/control is rejected", !CrashRecovery.IsKnownCrash(invalid));
        }

        check("Stopped crash recovery cannot dismiss", recovery.Observe(Frame(0, hang)) is null);
        recovery.Start();
        check("Crash dismissal requires two observations", recovery.Observe(Frame(0, hang)) is null);
        check("Repeated timestamp cannot confirm crash", recovery.Observe(Frame(0, hang)) is null);
        check("Rapid duplicate observation cannot confirm crash", recovery.Observe(Frame(.1, hang)) is null);
        var request = recovery.Observe(Frame(.6, hang));
        check("Stable native crash produces a dismissal request", request?.Dialog == hang);
        recovery.Dispatched(request!, epoch.AddSeconds(.6));
        check("A dismissed dialog does not imply process exit", recovery.Observe(Frame(1)) is null && recovery.Status == CrashRecoveryStatus.AwaitingExit);
        check("A dismissed dialog cannot be clicked again", recovery.Observe(Frame(2, hang)) is null);
        recovery.Observe(Absent(3) with
        {
            BootstrapperPresent = true
        });
        check("Bootstrapper presence blocks recovery completion", recovery.Status == CrashRecoveryStatus.AwaitingExit);
        recovery.Observe(Absent(4));
        check("Observed process exit completes one crash recovery", recovery.ConfirmedExits == 1 && recovery.Status == CrashRecoveryStatus.Monitoring);
        recovery.Observe(Absent(5));
        check("One crash exit cannot be counted twice", recovery.ConfirmedExits == 1);
        for (int i = 1; i <= 2; i++)
        {
            double t = 10 * i;
            recovery.Observe(Frame(t, hang));
            request = recovery.Observe(Frame(t + 1, hang));
            recovery.Dispatched(request!, epoch.AddSeconds(t + 1));
            recovery.Observe(Absent(t + 2));
        }
        recovery.Observe(Frame(30, hang));
        check("Fourth crash in thirty minutes pauses recovery", recovery.Observe(Frame(31, hang)) is null && recovery.Status == CrashRecoveryStatus.NeedsAttention);
        recovery.Start();
        recovery.Observe(Frame(40, hang));
        check("Two crash dialogs cannot authorize dismissal", recovery.Observe(Frame(41, hang, hang with { WindowId = 200 })) is null
            && recovery.Status == CrashRecoveryStatus.NeedsAttention);
        recovery.Start();
        recovery.Observe(Frame(50, hang));
        check("Long observation gap requires reconfirmation", recovery.Observe(Frame(60, hang)) is null);
        request = recovery.Observe(Frame(61, hang));
        recovery.Dispatched(request!, epoch.AddSeconds(61));
        recovery.Observe(Frame(91, hang));
        check("Game refusing to exit pauses without a kill or duplicate launch", recovery.Status == CrashRecoveryStatus.NeedsAttention);
        recovery.Start();
        recovery.Observe(Frame(100, hang));
        request = recovery.Observe(Frame(101, hang));
        recovery.Stop();
        bool staleRejected = false;
        try
        {
            recovery.Dispatched(request!, epoch.AddSeconds(101));
        }
        catch (InvalidOperationException) { staleRejected = true; }
        check("Stop invalidates queued crash dismissal", staleRejected);
        recovery.Start();
        recovery.Observe(Frame(110, hang));
        request = recovery.Observe(Frame(111, hang));
        staleRejected = false;
        try
        {
            recovery.Dispatched(request!, epoch.AddSeconds(117));
        }
        catch (InvalidOperationException) { staleRejected = true; }
        check("Expired crash dismissal cannot dispatch", staleRejected);

        recovery.Start();
        for (int i = 0; i < 3; i++)
        {
            double t = 120 + i * 6;
            check("Rejected native dismissal requires fresh evidence " + i, recovery.Observe(Frame(t, hang)) is null);
            request = recovery.Observe(Frame(t + 1, hang));
            check("Stable crash controls allow bounded reacquisition " + i, request is not null);
            recovery.Rejected(request!, epoch.AddSeconds(t + 1), "Dialog focus unavailable");
            check("Rejected native dismissal cannot immediately retry " + i, recovery.Observe(Frame(t + 2, hang)) is null);
        }
        check("Repeated native dismissal failures pause instead of looping", recovery.Status == CrashRecoveryStatus.NeedsAttention);

        var launcher = new LaunchSupervisor();
        launcher.Start();
        for (int i = 0; i < 3; i++)
        {
            int t = i * 20;
            launcher.Observe(new(epoch.AddSeconds(t), false, false, false));
            var launch = launcher.Observe(new(epoch.AddSeconds(t + 5), false, false, false));
            check("Crash recovery still launches only after confirmed absence " + i, launch is not null);
            launcher.Dispatched(launch!, epoch.AddSeconds(t + 5));
            launcher.Observe(new(epoch.AddSeconds(t + 7), true, false, true));
            launcher.RecoverAfterCrash();
        }
        launcher.Observe(new(epoch.AddSeconds(60), false, false, false));
        check("Crash recovery preserves the three-launch budget", launcher.Observe(new(epoch.AddSeconds(65), false, false, false)) is null
            && launcher.Status == LaunchStatus.NeedsAttention);
        launcher.Stop();
        launcher.RecoverAfterCrash();
        check("Crash recovery cannot reactivate a stopped launcher", launcher.Status == LaunchStatus.Stopped);
    }
}
