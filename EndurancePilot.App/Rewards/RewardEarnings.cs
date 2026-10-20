using System.Text.Json;
using EndurancePilot.Core;

namespace EndurancePilot.App;

internal sealed class RewardEarnings
{
    internal sealed record Saved(Dictionary<string, long> Totals, HashSet<Guid> Receipts);
    private readonly string path;
    private Saved saved = new(new(StringComparer.OrdinalIgnoreCase), []);
    private readonly Dictionary<string, long> session = new(StringComparer.OrdinalIgnoreCase);
    internal string? Warning
    {
        get; private set;
    }
    private bool readable = true;
    internal RewardEarnings(string? path = null)
    {
        this.path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EndurancePilot", "earnings.json");
        try
        {
            if (File.Exists(this.path))
            {
                var data = JsonSerializer.Deserialize<Saved>(File.ReadAllText(this.path))
                    ?? throw new InvalidDataException("Empty earnings file.");
                if (data.Totals is null || data.Receipts is null || data.Totals.Any(x => x.Value < 0))
                {
                    throw new InvalidDataException("Invalid earnings file.");
                }

                saved = new(new(data.Totals, StringComparer.OrdinalIgnoreCase), data.Receipts);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            readable = false;
            Warning = "Lifetime earnings could not be loaded. Existing history has been preserved.";
        }
    }
    internal IReadOnlyList<MissionReward> Session => Rows(session);
    internal IReadOnlyList<MissionReward> Lifetime => Rows(saved.Totals);
    private static MissionReward[] Rows(Dictionary<string, long> values) => values.OrderBy(x => x.Key)
        .Select(x => new MissionReward(x.Key, x.Value, "◇")).ToArray();
    internal void StartSession() => session.Clear();

    internal bool Record(Guid receipt, IReadOnlyList<MissionReward> items, bool complete)
    {
        if (!complete || items.Count == 0 || saved.Receipts.Contains(receipt) || !readable)
        {
            return false;
        }

        var totals = new Dictionary<string, long>(saved.Totals, StringComparer.OrdinalIgnoreCase);
        var current = new Dictionary<string, long>(session, StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var item in items.Where(x => x.Quantity is > 0))
            {
                totals[item.Name] = checked(totals.GetValueOrDefault(item.Name) + item.Quantity!.Value);
                current[item.Name] = checked(current.GetValueOrDefault(item.Name) + item.Quantity!.Value);
            }
            var next = new Saved(totals, new(saved.Receipts) { receipt });
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(next));
            File.Move(path + ".tmp", path, true);
            saved = next;
            session.Clear();
            foreach (var pair in current)
            {
                session.Add(pair.Key, pair.Value);
            }

            Warning = null;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or OverflowException)
        {
            Warning = "Earnings could not be saved. Totals will retry on the next observation.";
            return false;
        }
    }
}
