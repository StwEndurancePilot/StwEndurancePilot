namespace EndurancePilot.Core;

public sealed record CrashDialogEvidence(long WindowId, long ButtonId, int ProcessId, string ProcessName,
    string Title, string Message, bool ButtonEnabled);
public sealed record CrashSnapshot(DateTimeOffset CapturedAt, bool GamePresent, bool BootstrapperPresent,
    IReadOnlyList<CrashDialogEvidence> Dialogs);
public sealed record CrashDismissRequest(Guid Id, DateTimeOffset ObservedAt, CrashDialogEvidence Dialog);
public enum CrashRecoveryStatus
{
    Stopped, Monitoring, Confirming, AwaitingExit, NeedsAttention
}

public sealed class CrashRecovery
{
    public CrashRecoveryStatus Status { get; private set; } = CrashRecoveryStatus.Stopped;
    public string Detail { get; private set; } = "Crash recovery stopped";
    public int ConfirmedExits
    {
        get; private set;
    }
    private readonly Queue<DateTimeOffset> dismissals = new();
    private CrashDialogEvidence? candidate;
    private CrashDismissRequest? proposed;
    private DateTimeOffset lastObservation, stableSince, dispatchedAt, nextAttemptAt;
    private int hits, rejectedDismissals;

    public static bool IsKnownCrash(CrashDialogEvidence dialog)
    {
        string message = string.Join(" ", dialog.Message.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return dialog.WindowId != 0 && dialog.ButtonId != 0 && dialog.ProcessId > 0 && dialog.ButtonEnabled
            && dialog.ProcessName.Equals("FortniteClient-Win64-Shipping", StringComparison.OrdinalIgnoreCase)
            && ((dialog.Title == "Application Hang Detected"
                && message == "The application has hung and will now close. We apologize for the inconvenience.")
            || (dialog.Title == "Application Crash Detected"
                && message == "The application has crashed and will now close. We apologize for the inconvenience."));
    }

    public void Start()
    {
        Status = CrashRecoveryStatus.Monitoring;
        Detail = "Watching for Fortnite crash dialogs";
        dismissals.Clear();
        candidate = null;
        proposed = null;
        hits = 0;
        lastObservation = default;
        nextAttemptAt = default;
        rejectedDismissals = 0;
        ConfirmedExits = 0;
    }
    public void Stop()
    {
        Status = CrashRecoveryStatus.Stopped;
        candidate = null;
        proposed = null;
        hits = 0;
    }
    public void Pause(string reason)
    {
        Status = CrashRecoveryStatus.NeedsAttention;
        Detail = reason;
        proposed = null;
    }

    public void InvalidateObservation()
    {
        candidate = null;
        proposed = null;
        hits = 0;
        dispatchedAt = nextAttemptAt = default;
        if (Status is CrashRecoveryStatus.Confirming or CrashRecoveryStatus.AwaitingExit)
        {
            Status = CrashRecoveryStatus.Monitoring;
            Detail = "Waiting for fresh crash evidence";
        }
    }

    public CrashDismissRequest? Observe(CrashSnapshot frame)
    {
        if (Status is CrashRecoveryStatus.Stopped or CrashRecoveryStatus.NeedsAttention || frame.CapturedAt <= lastObservation)
        {
            return null;
        }

        if (frame.CapturedAt - lastObservation > TimeSpan.FromSeconds(6))
        {
            hits = 0;
        }

        lastObservation = frame.CapturedAt;
        proposed = null;
        if (Status == CrashRecoveryStatus.AwaitingExit)
        {
            if (!frame.GamePresent && !frame.BootstrapperPresent)
            {
                ConfirmedExits++;
                Status = CrashRecoveryStatus.Monitoring;
                candidate = null;
                hits = 0;
                Detail = "Crashed Fortnite exited; launch supervision will confirm absence before relaunch";
            }
            else if (frame.CapturedAt - dispatchedAt >= TimeSpan.FromSeconds(30))
            {
                Pause("Crash dialog was dismissed but Fortnite has not exited after 30 seconds. No duplicate game was launched.");
            }

            return null;
        }
        var known = frame.Dialogs.Where(IsKnownCrash).ToArray();
        if (known.Length == 0 || !frame.GamePresent)
        {
            Status = CrashRecoveryStatus.Monitoring;
            candidate = null;
            hits = 0;
            Detail = "Watching for Fortnite crash dialogs";
            return null;
        }
        if (known.Length != 1)
        {
            Pause("More than one Fortnite crash dialog was found; inspect the game before resuming.");
            return null;
        }
        if (frame.CapturedAt < nextAttemptAt)
        {
            Status = CrashRecoveryStatus.Confirming;
            return null;
        }
        if (candidate != known[0] || hits == 0)
        {
            candidate = known[0];
            hits = 1;
            stableSince = frame.CapturedAt;
        }
        else
        {
            hits++;
        }

        Status = CrashRecoveryStatus.Confirming;
        Detail = "Confirming Fortnite's native crash dialog";
        if (hits < 2 || frame.CapturedAt - stableSince < TimeSpan.FromMilliseconds(500))
        {
            return null;
        }

        while (dismissals.TryPeek(out var time) && frame.CapturedAt - time > TimeSpan.FromMinutes(30))
        {
            dismissals.Dequeue();
        }

        if (dismissals.Count >= 3)
        {
            Pause("Three Fortnite crash dismissals within thirty minutes. Repeated crashes require attention.");
            return null;
        }
        return proposed = new(Guid.NewGuid(), frame.CapturedAt, candidate!);
    }

    public void Dispatched(CrashDismissRequest request, DateTimeOffset now)
    {
        if (Status != CrashRecoveryStatus.Confirming || proposed != request || now < request.ObservedAt
            || now - request.ObservedAt > TimeSpan.FromSeconds(5))
        {
            throw new InvalidOperationException("Crash dismissal is no longer current.");
        }

        dismissals.Enqueue(now);
        proposed = null;
        dispatchedAt = now;
        rejectedDismissals = 0;
        Status = CrashRecoveryStatus.AwaitingExit;
        Detail = "Dismissed Fortnite crash dialog; waiting for the game process to exit";
    }

    public void Rejected(CrashDismissRequest request, DateTimeOffset now, string reason)
    {
        if (Status != CrashRecoveryStatus.Confirming || proposed != request)
        {
            throw new InvalidOperationException("Rejected crash dismissal is no longer current.");
        }

        proposed = null;
        candidate = null;
        hits = 0;
        if (++rejectedDismissals >= 3)
        {
            Pause("Crash dialog could not be dismissed after three attempts: " + reason);
            return;
        }
        nextAttemptAt = now.AddSeconds(3);
        Detail = "Crash dismissal was not sent; reacquiring native controls: " + reason;
    }
}
