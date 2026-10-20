namespace EndurancePilot.App;

internal static class RewardAssets
{
    internal static readonly IReadOnlyDictionary<string, string> Files =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["People XP"] = "survivor_xp",
            ["Survivor XP"] = "survivor_xp",
            ["Hero XP"] = "hero_xp",
            ["Schematic XP"] = "schematic_xp",
            ["Season XP"] = "season_xp",
            ["Battle Pass XP"] = "season_xp",
            ["Gold"] = "gold",
            ["V-Bucks"] = "v-bucks_voucher",
            ["X-Ray Tickets"] = "x-ray_tickets",
            ["Legendary PERK-UP!"] = "legendary_perk-up",
            ["Epic PERK-UP!"] = "epic_perk-up",
            ["Rare PERK-UP!"] = "rare_perk-up",
            ["Uncommon PERK-UP!"] = "uncommon_perk-up",
            ["RE-PERK!"] = "re-perk",
            ["FIRE-UP!"] = "fire-up",
            ["AMP-UP!"] = "amp-up",
            ["FROST-UP!"] = "frost-up",
            ["Pure Drop of Rain"] = "pure_drop_of_rain",
            ["Lightning in a Bottle"] = "lightning_in_a_bottle",
            ["Eye of the Storm"] = "eye_of_the_storm",
            ["Storm Shard"] = "storm_shard",
            ["Training Manual"] = "training_manual",
            ["Trap Designs"] = "trap_designs",
            ["Weapon Designs"] = "weapon_designs",
            ["Road Trip Tickets"] = "road_trip_tickets",
            ["Mini Reward Llama"] = "mini_reward_llama"
        };

    internal static byte[]? Read(string name)
    {
        if (!Files.TryGetValue(name, out var file))
        {
            return null;
        }

        try
        {
            return File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "assets", "reward-" + file + ".png"));
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
