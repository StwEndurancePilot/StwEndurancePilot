namespace EndurancePilot.Core;

public sealed class ProcessExitObserver
{
    private bool seenGame;
    private int absentReads;
    private DateTimeOffset absentSince, lastObservation;

    public void Reset()
    {
        seenGame = false;
        absentReads = 0;
        lastObservation = default;
    }

    public bool Observe(GameProcessSnapshot snapshot)
    {
        if (snapshot.CapturedAt <= lastObservation)
        {
            return false;
        }

        if (snapshot.CapturedAt - lastObservation > TimeSpan.FromSeconds(15))
        {
            absentReads = 0;
        }

        lastObservation = snapshot.CapturedAt;
        if (snapshot.GamePresent)
        {
            seenGame = true;
            absentReads = 0;
            return false;
        }
        if (!seenGame || snapshot.BootstrapperPresent)
        {
            absentReads = 0;
            return false;
        }
        if (absentReads++ == 0)
        {
            absentSince = snapshot.CapturedAt;
        }

        if (absentReads < 2 || snapshot.CapturedAt - absentSince < TimeSpan.FromSeconds(5))
        {
            return false;
        }

        seenGame = false;
        absentReads = 0;
        return true;
    }
}
