namespace EndurancePilot.Core;

public enum WalkKey
{
    A, W, S, D
}
public sealed record WalkStep(WalkKey Key, int Milliseconds);
public sealed record WalkRoute(IReadOnlyList<WalkStep> Steps)
{
    public static WalkRoute TwineInitial => new([new(WalkKey.A, 4000), new(WalkKey.W, 4000)]);
    public void Validate()
    {
        if (Steps is null || Steps.Count is < 1 or > 12 || Steps.Any(s => s is null || !Enum.IsDefined(s.Key)
            || s.Milliseconds is < 50 or > 10000) || Steps.Sum(s => (long)s.Milliseconds) > 30000)
        {
            throw new InvalidOperationException("Walking route must contain 1–12 WASD steps, each 50–10,000 ms, with a total of at most 30 seconds.");
        }
    }
}
