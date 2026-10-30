using System.Text.RegularExpressions;

namespace EndurancePilot.Core;

public sealed record WaveHudReading(int Wave, bool Starting, TimeSpan Remaining);

public static class WaveHud
{
    public static WaveHudReading? Read(Observation frame)
    {
        var countdown = WaveReader.ReadCountdown(frame);
        bool starting = countdown.Count > 0;
        var labels = starting ? countdown : WaveReader.Read(frame);
        var waves = labels.Select(r => r.Wave).Distinct().ToArray();
        if (waves.Length != 1) return null;
        var times = new HashSet<int>();
        foreach (var label in labels)
        {
            if (label.Bounds.CenterY > frame.Height * .45) continue;
            double h = Math.Max(8, label.Bounds.Height);
            foreach (var region in frame.Regions)
            {
                var match = Regex.Match(region.Normalized, @"^(\d{1,2})\s*:\s*([0-5]\d)(?:$|\s+)");
                if (!match.Success) continue;
                bool inline = Regex.IsMatch(region.Normalized, @"\bWAVE\s+" + waves[0] + @"\b")
                    && !Regex.IsMatch(region.Normalized, @"\b(STARTS|INCOMING)\b")
                    && region.Bounds.X <= label.Bounds.CenterX && region.Bounds.Right >= label.Bounds.CenterX
                    && Math.Abs(region.Bounds.CenterY - label.Bounds.CenterY) <= h;
                if (!inline && match.Value.Length != region.Normalized.Length) continue;
                bool below = region.Bounds.Y >= label.Bounds.Bottom - h * .25
                    && region.Bounds.Y - label.Bounds.Bottom <= h * 3
                    && region.Bounds.CenterX >= label.Bounds.X - h * 3
                    && region.Bounds.CenterX <= label.Bounds.Right + h;
                if (!inline && !below) continue;
                int seconds = int.Parse(match.Groups[1].Value) * 60 + int.Parse(match.Groups[2].Value);
                // Endurance HUD clocks are minutes, not the session elapsed time.
                if (seconds <= 20 * 60) times.Add(seconds);
            }
        }
        return times.Count == 1 ? new(waves[0], starting, TimeSpan.FromSeconds(times.Single())) : null;
    }
}
