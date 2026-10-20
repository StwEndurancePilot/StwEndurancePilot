using EndurancePilot.Core;

namespace EndurancePilot.App;

internal static class RewardEarningsChecks
{
    internal static IReadOnlyList<StartupCheck> Run(string output)
    {
        var checks = new List<StartupCheck>();
        void Check(string name, bool pass) => checks.Add(new("Earnings: " + name, pass, pass ? "Passed (offline)" : "Failed"));
        string path = Path.Combine(output, "earnings-check-" + Guid.NewGuid() + ".json");
        var ledger = new RewardEarnings(path);
        var first = Guid.NewGuid();
        MissionReward[] items = [new("Gold", 420, ""), new("People XP", 3150, ""), new("Unreadable", null, "")];
        Check("Partial carousel does not count", !ledger.Record(first, items, false) && ledger.Lifetime.Count == 0);
        Check("Complete receipt counts", ledger.Record(first, items, true) && ledger.Session.Single(x => x.Name == "Gold").Quantity == 420);
        Check("Repeated polling does not double count", !ledger.Record(first, items, true) && ledger.Lifetime.Single(x => x.Name == "Gold").Quantity == 420);
        Check("Unknown quantities are not invented", ledger.Lifetime.All(x => x.Name != "Unreadable"));
        ledger.Record(Guid.NewGuid(), [new("gold", 80, "")], true);
        Check("Runs accumulate by reward name", ledger.Session.Single(x => x.Name == "Gold").Quantity == 500);
        var restored = new RewardEarnings(path);
        Check("Lifetime and receipt deduplication survive restart",
            restored.Lifetime.Single(x => x.Name == "Gold").Quantity == 500 && !restored.Record(first, items, true));
        ledger.StartSession();
        Check("New session resets only session totals", ledger.Session.Count == 0 && ledger.Lifetime.Single(x => x.Name == "Gold").Quantity == 500);
        ledger.Record(Guid.NewGuid(), [new("Gold", 10, "")], true);
        Check("New session adds to lifetime", ledger.Session.Single().Quantity == 10 && ledger.Lifetime.Single(x => x.Name == "Gold").Quantity == 510);
        File.WriteAllText(path, "broken");
        var corrupt = new RewardEarnings(path);
        Check("Unreadable history is preserved", corrupt.Warning is not null && !corrupt.Record(Guid.NewGuid(), items, true) && File.ReadAllText(path) == "broken");
        return checks;
    }
}
