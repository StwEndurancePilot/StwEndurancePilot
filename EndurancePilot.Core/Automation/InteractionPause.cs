namespace EndurancePilot.Core;

public sealed class InteractionPause
{
    public bool Active
    {
        get; private set;
    }
    private DateTimeOffset? idleSince;
    private (int X, int Y)? pointer;
    public void Begin()
    {
        Active = true;
        idleSince = null;
        pointer = null;
    }
    public void Clear()
    {
        Active = false;
        idleSince = null;
        pointer = null;
    }
    public bool Ready(DateTimeOffset now, bool focused, bool held, int x, int y)
    {
        if (!Active)
        {
            return true;
        }

        bool moved = pointer is { } previous && previous != (x, y);
        pointer = (x, y);
        if (!focused || held || moved)
        {
            idleSince = null;
            return false;
        }
        idleSince ??= now;
        if (now - idleSince < TimeSpan.FromSeconds(2))
        {
            return false;
        }

        Clear();
        return true;
    }
}
