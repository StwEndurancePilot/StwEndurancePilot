using System.Globalization;
using System.Text.RegularExpressions;

namespace EndurancePilot.Core;

public sealed record MissionReward(string Name, long? Quantity, string Icon)
{
    public string QuantityLabel => Quantity is { } count ? $"× {count:N0}" : "Quantity unreadable";
}

public static class MissionRewardReader
{
    public const string RevealHeading = @"^(MISSION REWARDS|[YV]OU RECEIVED|QUEST COMPLETE)$";
    private const string Number = @"(?:\d{1,3}(?:,\d{3})+|\d+)";
    private static readonly (string Name, string Icon)[] Catalog =
    [
        ("People XP", "★"), ("Season XP", "★"), ("Battle Pass XP", "★"), ("Commander XP", "★"),
        ("Hero XP", "★"), ("Survivor XP", "★"), ("Schematic XP", "★"),
        ("Gold", "●"), ("V-Bucks", "◎"), ("X-Ray Tickets", "▣"),
        ("Legendary PERK-UP!", "◆"), ("Epic PERK-UP!", "◆"),
        ("Rare PERK-UP!", "◆"), ("Uncommon PERK-UP!", "◆"),
        ("RE-PERK!", "↻"), ("FIRE-UP!", "♨"), ("AMP-UP!", "ϟ"), ("FROST-UP!", "❄"),
        ("Pure Drop of Rain", "♦"), ("Lightning in a Bottle", "ϟ"),
        ("Eye of the Storm", "◉"), ("Storm Shard", "◆"),
        ("Training Manual", "▤"), ("Trap Designs", "▤"), ("Weapon Designs", "▤"),
        ("Road Trip Tickets", "▣"), ("Event Tickets", "▣")
    ];

    public static IReadOnlyList<MissionReward> Read(Observation frame, ScreenEvidence screen)
    {
        if (screen.Kind is not (ScreenKind.Rewards or ScreenKind.RewardClaim))
        {
            return [];
        }

        if (ReadCard(frame) is { } card)
        {
            return [card];
        }

        var lines = frame.Regions.Where(r => !r.Pass.EndsWith("-word", StringComparison.Ordinal)).ToArray();
        var result = new List<MissionReward>();
        foreach (var (name, icon) in Catalog)
        {
            var label = TextRegion.Normalize(name);
            var amounts = new HashSet<long>();
            bool found = false;
            foreach (var line in lines)
            {

                var text = Regex.Replace(line.Text.ToUpperInvariant(), @"[-!]", " ").Trim();
                text = Regex.Replace(text, @"\s+", " ");
                var namePattern = Regex.Escape(label);
                var match = Regex.Match(text, $@"^(?:[X×]?\s*(?<before>{Number})\s+)?{namePattern}(?:\s+[X×]?\s*(?<after>{Number}))?$");
                if (!match.Success)
                {
                    continue;
                }

                found = true;
                var before = match.Groups["before"].Value;
                var after = match.Groups["after"].Value;
                if (before.Length > 0 || after.Length > 0)
                {
                    Add(before);
                    Add(after);
                    continue;
                }

                foreach (var count in lines.Where(r =>
                                    Math.Abs(r.Bounds.CenterX - line.Bounds.CenterX) < Math.Max(line.Bounds.Width / 2, frame.Width * .025)
                                    && (r.Bounds.Y >= line.Bounds.Bottom && r.Bounds.Y - line.Bounds.Bottom < frame.Height * .065
                                        || line.Bounds.Y >= r.Bounds.Bottom && line.Bounds.Y - r.Bounds.Bottom < frame.Height * .065)))
                {
                    var quantity = Regex.Match(count.Text.Trim(), $@"^[xX×]?\s*(?<count>{Number})$");
                    if (quantity.Success)
                    {
                        Add(quantity.Groups["count"].Value);
                    }
                }
            }
            if (found)
            {
                result.Add(new(name, amounts.Count == 1 ? amounts.Single() : null, icon));
            }

            void Add(string raw)
            {
                if (long.TryParse(raw.Replace(",", ""), NumberStyles.None, CultureInfo.InvariantCulture, out var amount) && amount > 0)
                {
                    amounts.Add(amount);
                }
            }
        }

        var extras = new List<MissionReward>();
        foreach (var line in lines)
        {
            var match = Regex.Match(line.Text.Trim(),
                $@"^(?<name>[A-Za-z][A-Za-z '!‐-]{{2,60}}?)\s+[xX×]\s*(?<count>{Number})$");
            if (!match.Success)
            {
                continue;
            }

            var name = match.Groups["name"].Value.Trim();
            if (Catalog.Any(c => TextRegion.Normalize(c.Name) == TextRegion.Normalize(name)))
            {
                continue;
            }

            if (long.TryParse(match.Groups["count"].Value.Replace(",", ""), out var count) && count > 0)
            {
                extras.Add(new(name, count, "◇"));
            }
        }
        result.AddRange(extras.GroupBy(r => TextRegion.Normalize(r.Name)).Select(g =>
            new MissionReward(g.First().Name, g.Select(r => r.Quantity).Distinct().Count() == 1 ? g.First().Quantity : null, "◇")));
        return result;
    }

    public static MissionReward? ReadCard(Observation frame)
    {
        if (!frame.Has(RevealHeading) || frame.FindVisual("reward-card").Count() != 1)
        {
            return null;
        }

        var names = frame.Regions.Where(r => r.Pass == "reward-title")
            .Select(r => r.Text.Trim()).Where(s => Regex.IsMatch(s, @"^[A-Za-z][A-Za-z0-9 '\-!]{2,60}$"))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (names.Length != 1)
        {
            return null;
        }

        long? Count(string pass)
        {
            var card = frame.FindVisual("reward-card").Single().Bounds;
            var values = frame.Regions.Where(r => r.Pass == pass && r.Bounds.CenterX > card.X + card.Width * .5
                && r.Bounds.CenterY > card.Y + card.Height * .7 && r.Bounds.Right <= card.Right && r.Bounds.Bottom <= card.Bottom).Select(r => r.Text.Trim())
                .Where(s => Regex.IsMatch(s, $@"^(?:[xX×]\s*)?{Number}$")).Distinct().ToArray();
            if (values.Length != 1 || !Regex.IsMatch(values[0], $@"^(?:[xX×]\s*)?{Number}$"))
            {
                return null;
            }

            return long.TryParse(Regex.Replace(values[0], @"[xX×,\s]", ""), out var n) && n > 0 ? n : null;
        }
        var quantity = Count("reward-count");
        if (quantity is null || quantity != Count("reward-count-check"))
        {
            return null;
        }

        var normalizedName = TextRegion.Normalize(names[0]);
        var known = Catalog.FirstOrDefault(c => TextRegion.Normalize(c.Name) == normalizedName
            || c.Name.EndsWith(" XP", StringComparison.Ordinal) && TextRegion.Normalize(c.Name[..^1]) == normalizedName);
        return new(known.Name ?? names[0], quantity, known.Icon ?? "◇");
    }

    public static string Signature(Observation frame, ScreenEvidence screen) =>
        string.Join("|", Read(frame, screen).Select(r => $"{r.Name}:{r.Quantity}"));

    public static string PageContext(Observation frame, ScreenEvidence screen) =>
        string.Join(",", NavigationPlanner.MissionRewardControls(frame).Select(r => r.Normalized).Distinct().Order())
        + "|" + Signature(frame, screen);

    public static bool HasPageChanged(string? previous, Observation frame, ScreenEvidence screen)
    {
        if (previous is null || !NavigationPlanner.MissionRewardControls(frame).Any())
        {
            return false;
        }

        var next = PageContext(frame, screen);
        if (next == previous)
        {
            return false;
        }

        return previous.Split('|')[0] != next.Split('|')[0] || Read(frame, screen).Count > 0;
    }
}

public sealed class MissionRewardTracker
{
    public Guid ReceiptId { get; private set; } = Guid.NewGuid();
    public bool IsComplete
    {
        get; private set;
    }
    public DateTimeOffset LastSeenAt
    {
        get; private set;
    }
    public IReadOnlyList<MissionReward> Items { get; private set; } = [];
    public bool Visible
    {
        get; private set;
    }
    public string Status { get; private set; } = "";
    public bool CanCollect
    {
        get; private set;
    }
    private string? revealSignature;
    private DateTimeOffset revealStableSince;
    private readonly List<MissionReward> previousPages = [];
    private string? candidate, currentPage, currentItems;
    private DateTimeOffset lastFrame, candidateAt;
    private bool dispatched;
    private Guid? runId;
    private readonly Dictionary<string, MissionReward> scrolling = new(StringComparer.OrdinalIgnoreCase);
    private string? lastScrollKey;
    private int menuFrames;
    private ScreenKind lastMenu;

    public void Reset()
    {
        Items = [];
        previousPages.Clear();
        Visible = false;
        Status = "";
        scrolling.Clear();
        lastScrollKey = null;
        ReceiptId = Guid.NewGuid();
        IsComplete = false;
        menuFrames = 0;
        LastSeenAt = default;
        candidate = currentPage = currentItems = null;
        lastFrame = candidateAt = default;
        dispatched = false;
        CanCollect = false;
        revealSignature = null;
        revealStableSince = default;
    }

    public void Dispatched(ClickAction action)
    {
        if (action.Step is NavigationStep.CollectMissionRewards or NavigationStep.ClaimReward)
        {
            dispatched = true;
        }
    }

    public void Observe(Observation frame, ScreenEvidence screen, RunRecord? run)
    {
        if (run is not null && run.Id != runId)
        {
            Reset();
            runId = run.Id;
        }
        if (frame.CapturedAt <= lastFrame)
        {
            return;
        }

        var gap = frame.CapturedAt - lastFrame;
        lastFrame = frame.CapturedAt;
        CanCollect = false;
        if (screen.Kind is not (ScreenKind.Rewards or ScreenKind.RewardClaim))
        {
            candidate = null;
            revealSignature = null;
            bool menu = screen.Kind is ScreenKind.SaveTheWorldHub or ScreenKind.SaveTheWorldLobby or ScreenKind.WorldMap
                or ScreenKind.MissionMap or ScreenKind.ModeDiscovery or ScreenKind.ModeSelection || DiscoveryNavigation.IsOutOfMission(screen.Kind);
            var menuGap = screen.Kind == ScreenKind.SaveTheWorldHub && screen.AllowsInput
                ? NavigationTiming.HealthProbeMaximumGap : TimeSpan.FromSeconds(10);
            menuFrames = menu && Visible ? lastMenu == screen.Kind && gap < menuGap ? menuFrames + 1 : 1 : 0;
            lastMenu = screen.Kind;
            if (menuFrames >= 2)
            {
                IsComplete = true;
            }

            if (Visible)
            {
                Status = Items.Count > 0
                ? "Rewards read from the game · unreadable items may be missing."
                : "Reward details could not be read.";
            }

            return;
        }
        menuFrames = 0;
        LastSeenAt = frame.CapturedAt;
        Visible = true;
        var visibleItems = MissionRewardReader.Read(frame, screen);
        var signature = MissionRewardReader.PageContext(frame, screen);
        if (revealSignature != signature || gap > TimeSpan.FromSeconds(2))
        {
            revealSignature = signature;
            revealStableSince = frame.CapturedAt;
        }
        CanCollect = visibleItems.Count > 0 && visibleItems.All(r => r.Quantity.HasValue)
            && Items.All(r => r.Quantity.HasValue)
            && frame.CapturedAt - revealStableSince >= TimeSpan.FromSeconds(5);
        if (MissionRewardReader.ReadCard(frame) is { } reward)
        {

            if (scrolling.Count == 0)
            {
                foreach (var existing in Items)
                {
                    scrolling.TryAdd($"{existing.Name}:{existing.Quantity}", existing);
                }
            }

            if (lastScrollKey is not null && scrolling.TryGetValue(lastScrollKey, out var prior) && string.Equals(prior.Name, reward.Name, StringComparison.OrdinalIgnoreCase))
            {
                reward = reward with
                {
                    Quantity = Math.Max(prior.Quantity ?? 0, reward.Quantity!.Value)
                };
                scrolling.Remove(lastScrollKey);
            }
            scrolling.Remove($"{reward.Name}:");
            lastScrollKey = $"{reward.Name}:{reward.Quantity}";
            scrolling.TryAdd(lastScrollKey, reward);
            Items = scrolling.Values.GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase).Select(g => new MissionReward(g.Key,
                g.All(r => r.Quantity.HasValue) ? g.Sum(r => r.Quantity!.Value) : null, g.First().Icon)).ToArray();
            Status = "Reading scrolling rewards…";
            return;
        }
        if (Items.Count == 0)
        {
            Status = "Opening rewards · waiting for readable item details…";
        }

        var page = MissionRewardReader.PageContext(frame, screen);
        if (candidate != page || gap > TimeSpan.FromSeconds(10))
        {
            candidate = page;
            candidateAt = frame.CapturedAt;
            return;
        }
        if (frame.CapturedAt - candidateAt < TimeSpan.FromMilliseconds(500))
        {
            return;
        }

        var items = MissionRewardReader.Read(frame, screen);
        if (items.Count == 0)
        {
            return;
        }

        if (!dispatched && scrolling.Count == 0)
        {

            Items = Items.Concat(items).GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                            .Select(g => new MissionReward(g.First().Name, g.Any(r => r.Quantity.HasValue)
                                ? g.Max(r => r.Quantity) : null, g.First().Icon)).ToArray();
            Status = CanCollect ? "Rewards stable; ready to collect."
                : "Reading rewards; waiting for readable quantities and the reveal to finish.";
            currentPage = page;
            currentItems = MissionRewardReader.Signature(frame, screen);
            return;
        }
        if (scrolling.Count > 0)
        {
            foreach (var item in items)
            {
                scrolling.TryAdd($"{item.Name}:{item.Quantity}", item);
            }

            Items = scrolling.Values.GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase).Select(g => new MissionReward(g.Key,
                g.All(r => r.Quantity.HasValue) ? g.Sum(r => r.Quantity!.Value) : null, g.First().Icon)).ToArray();
            return;
        }
        var itemSignature = MissionRewardReader.Signature(frame, screen);
        if (currentPage != page && dispatched)
        {

            if (currentItems != itemSignature)
            {
                previousPages.Clear();
                previousPages.AddRange(Items);
            }
            dispatched = false;
        }
        currentPage = page;
        currentItems = itemSignature;
        Items = previousPages.Concat(items).GroupBy(r => r.Name).Select(g =>
            new MissionReward(g.Key, g.All(r => r.Quantity.HasValue) ? g.Sum(r => r.Quantity!.Value) : null, g.First().Icon)).ToArray();
        Status = "Rewards read from the game · unreadable items may be missing.";
    }
}
