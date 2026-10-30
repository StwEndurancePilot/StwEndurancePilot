using System.Globalization;
using System.Text.RegularExpressions;

namespace EndurancePilot.Core;

public sealed record GameLogWave(int Wave, DateTimeOffset StartedAt);

// Read-only presentation evidence. Log reports never authorize game input,
// complete an attempt, or manufacture a remaining-wave countdown.
public sealed class GameLogProgress(DateTimeOffset processStartedAt)
{
    public GameLogWave? Latest { get; private set; }
    private DateTimeOffset lastEvent;
    private static readonly Regex Wave = new(@"^LogHealthSnapshot: =+ Snapshot: Start of Wave (\d{1,2}) \(FortGameStateOutpost, Difficulty \d+(?:\.\d+)?\) =+$", RegexOptions.CultureInvariant);

    public void Observe(string line, DateTimeOffset now)
    {
        if (line.Length > 4096 || line.Length < 27 || line[0] != '[') return;
        bool wave = line.Contains("LogHealthSnapshot: ", StringComparison.Ordinal);
        bool travel = line.Contains("LogLoad: LoadMap: ", StringComparison.Ordinal)
            || line.Contains("LogWorld: BeginTearingDown for ", StringComparison.Ordinal);
        if (!wave && !travel) return;
        if (!DateTimeOffset.TryParseExact(line.AsSpan(1, 23), "yyyy.MM.dd-HH.mm.ss:fff", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time)
            || time < processStartedAt || time > now.AddSeconds(2) || time < lastEvent) return;
        int prefixEnd = line.IndexOf(']', 25);
        if (prefixEnd < 0) return;
        string message = line[(prefixEnd + 1)..].Trim();
        if (message.StartsWith("LogLoad: LoadMap: ", StringComparison.Ordinal)
            || message.StartsWith("LogWorld: BeginTearingDown for ", StringComparison.Ordinal))
        {
            Latest = null;
            lastEvent = time;
            return;
        }
        var match = Wave.Match(message);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out int number) || number is < 1 or > 30) return;
        if (Latest is { } previous && (time <= previous.StartedAt || number <= previous.Wave && number != 1)) return;
        Latest = new(number, time);
        lastEvent = time;
    }
}
