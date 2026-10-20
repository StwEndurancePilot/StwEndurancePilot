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
    public bool Ready(DateTimeOffset now, bool focused, bool held, int x, int y, bool resumeHub = false)
    {
        if (!Active)
        {
            return true;
        }

        bool moved = pointer is { } previous && previous != (x, y);
        pointer = (x, y);
        if ((!focused && !resumeHub) || held || moved)
        {
            idleSince = null;
            return false;
        }
        idleSince ??= now;
        if (now - idleSince < TimeSpan.FromSeconds(focused ? 2 : 10))
        {
            return false;
        }

        Clear();
        return true;
    }
}
