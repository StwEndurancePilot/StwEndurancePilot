namespace EndurancePilot.Core;

public sealed record GameProcessSnapshot(DateTimeOffset CapturedAt, bool GamePresent, bool BootstrapperPresent, bool WindowPresent);
public sealed record LaunchRequest(Guid Id, DateTimeOffset ObservedAt);
public enum LaunchStatus
{
    Stopped, Monitoring, Starting, NeedsAttention
}

public sealed class LaunchSupervisor
{
    public LaunchStatus Status { get; private set; } = LaunchStatus.Stopped;
    public string Detail { get; private set; } = "Launch supervision stopped";
    private readonly Queue<DateTimeOffset> attempts = new();
    private DateTimeOffset lastObservation, absentSince, startupSince;
    private bool observingAbsence;
    private int absenceReads;
    private LaunchRequest? proposed;

    public void Start()
    {
        Status = LaunchStatus.Monitoring;
        Detail = "Checking the game process";
        attempts.Clear();
        lastObservation = default;
        observingAbsence = false;
        proposed = null;
    }

    public void Stop()
    {
        Status = LaunchStatus.Stopped;
        proposed = null;
        Detail = "Launch supervision stopped";
    }
    public void Pause(string reason)
    {
        Status = LaunchStatus.NeedsAttention;
        proposed = null;
        Detail = reason;
    }

    public void RecoverAfterCrash()
    {

        if (Status == LaunchStatus.Stopped)
        {
            return;
        }

        Status = LaunchStatus.Monitoring;
        proposed = null;
        observingAbsence = false;
        Detail = "Crash recovery finished; confirming process absence";
    }

    public LaunchRequest? Observe(GameProcessSnapshot snapshot)
    {
        if (Status is LaunchStatus.Stopped or LaunchStatus.NeedsAttention || snapshot.CapturedAt <= lastObservation)
        {
            return null;
        }

        if (snapshot.CapturedAt - lastObservation > TimeSpan.FromSeconds(15))
        {
            observingAbsence = false;
        }

        lastObservation = snapshot.CapturedAt;
        proposed = null;
        if (snapshot.GamePresent && snapshot.WindowPresent)
        {
            Status = LaunchStatus.Monitoring;
            observingAbsence = false;
            Detail = "Game window available";
            return null;
        }
        if (snapshot.GamePresent || snapshot.BootstrapperPresent || Status == LaunchStatus.Starting)
        {
            observingAbsence = false;
            if (Status != LaunchStatus.Starting)
            {
                Status = LaunchStatus.Starting;
                startupSince = snapshot.CapturedAt;
            }
            if (snapshot.CapturedAt - startupSince >= TimeSpan.FromMinutes(3))
            {
                Pause(snapshot.GamePresent || snapshot.BootstrapperPresent
                    ? "Game startup has not produced a usable window. Check Fortnite or Epic; no second copy was launched."
                    : "Epic launch did not produce a game process within three minutes. Check the launcher before resuming.");
            }
            else
            {
                Detail = "Waiting for game startup; no duplicate launch";
            }

            return null;
        }
        if (!observingAbsence)
        {
            observingAbsence = true;
            absentSince = snapshot.CapturedAt;
            absenceReads = 1;
        }
        else
        {
            absenceReads++;
        }

        if (absenceReads < 2 || snapshot.CapturedAt - absentSince < TimeSpan.FromSeconds(5))
        {
            Detail = "Confirming that the game process is absent";
            return null;
        }
        while (attempts.TryPeek(out var attempt) && snapshot.CapturedAt - attempt > TimeSpan.FromMinutes(30))
        {
            attempts.Dequeue();
        }

        if (attempts.Count >= 3)
        {
            Pause("Three game launches within thirty minutes. Check the repeated exits before resuming.");
            return null;
        }
        Detail = "Game process absent; launch through Epic is ready";
        return proposed = new(Guid.NewGuid(), snapshot.CapturedAt);
    }

    public void Dispatched(LaunchRequest request, DateTimeOffset now)
    {
        if (Status != LaunchStatus.Monitoring || proposed != request || now < request.ObservedAt || now - request.ObservedAt > TimeSpan.FromSeconds(5))
        {
            throw new InvalidOperationException("Launch request is no longer current.");
        }

        attempts.Enqueue(now);
        proposed = null;
        observingAbsence = false;
        startupSince = now;
        Status = LaunchStatus.Starting;
        Detail = "Launch requested through Epic";
    }
}
