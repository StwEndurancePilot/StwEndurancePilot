using System.Globalization;
using System.Text.RegularExpressions;

namespace EndurancePilot.Core;

public sealed record GameLogWave(int Wave, DateTimeOffset StartedAt);
public sealed record GameLogPlaytime(bool Sleeping, DateTimeOffset ChangedAt, bool Warning = false, DateTimeOffset? EpisodeStartedAt = null)
{
    public bool NeedsKeepPlaying => Sleeping || Warning;
    public DateTimeOffset WakeEventAt => EpisodeStartedAt ?? ChangedAt;
}

// Read-only presentation evidence. Log reports never authorize game input,
// complete an attempt, or manufacture a remaining-wave countdown.
public sealed class GameLogProgress(DateTimeOffset processStartedAt)
{
    public GameLogWave? Latest { get; private set; }
    public GameLogPlaytime? Playtime { get; private set; }
    public DateTimeOffset? ResultAvailableAt { get; private set; }
    private DateTimeOffset lastEvent;
    private static readonly Regex Wave = new(@"^LogHealthSnapshot: =+ Snapshot: Start of Wave (\d{1,2}) \(FortGameStateOutpost, Difficulty \d+(?:\.\d+)?\) =+$", RegexOptions.CultureInvariant);

    public void Observe(string line, DateTimeOffset now)
    {
        if (line.Length > 4096 || line.Length < 27 || line[0] != '[') return;
        bool wave = line.Contains("LogHealthSnapshot: ", StringComparison.Ordinal);
        bool travel = line.Contains("LogLoad: LoadMap: ", StringComparison.Ordinal)
            || line.Contains("LogWorld: BeginTearingDown for ", StringComparison.Ordinal);
        bool playtime = line.Contains("LogFortPlaytimeManager: ", StringComparison.Ordinal);
        bool result = line.Contains("LogFortMovie: ", StringComparison.Ordinal);
        if (!wave && !travel && !playtime && !result) return;
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
            ResultAvailableAt = null;
            lastEvent = time;
            return;
        }
        var sleep = Regex.Match(message, @"^LogFortPlaytimeManager: \[[a-fA-F0-9]+\] Changing Playtime State from \w+ to (Sleep|Unblocked|Idle)$", RegexOptions.CultureInvariant);
        if (sleep.Success)
        {
            string state = sleep.Groups[1].Value;
            var episode = state != "Unblocked" && Playtime is { NeedsKeepPlaying: true } priorEpisode ? priorEpisode.WakeEventAt : time;
            Playtime = new(state == "Sleep", time, state == "Idle", episode);
            lastEvent = time;
            return;
        }
        // This is an invitation to inspect the result screen, not proof of a win.
        if (message == "LogFortMovie: UFortMovieWidget::SetMediaSource(FMS_OutpostVictory)  SourceOpenSuccess 1"
            && Latest is { } reported && time >= reported.StartedAt && time - reported.StartedAt < TimeSpan.FromMinutes(20))
        {
            ResultAvailableAt ??= time;
            lastEvent = time;
            return;
        }
        var match = Wave.Match(message);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out int number) || number is < 1 or > 30) return;
        if (Latest is { } previous && (time <= previous.StartedAt || number <= previous.Wave && number != 1)) return;
        Latest = new(number, time);
        ResultAvailableAt = null;
        lastEvent = time;
    }
}
