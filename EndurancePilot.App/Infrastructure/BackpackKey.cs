using EndurancePilot.Core;

namespace EndurancePilot.App;

internal static class BackpackKey
{
    internal const string Default = "Tab";
    internal static readonly IReadOnlyList<string> Choices =
        new[] { "Tab" }.Concat(Enumerable.Range('A', 26).Select(c => ((char)c).ToString()))
            .Concat(Enumerable.Range(0, 10).Select(n => n.ToString())).ToArray();

    internal static ushort Resolve(string choice)
    {
        if (!Choices.Contains(choice, StringComparer.Ordinal))
            throw new InvalidDataException("Choose one of the listed backpack keys.");
        return choice == "Tab" ? (ushort)0x09 : (ushort)choice[0];
    }

    // Keep the quest-menu Tab shortcut independent of the gameplay inventory binding.
    internal static ushort ResolveInput(NavigationKey key, NavigationStep? step, string backpackKey) => key switch
    {
        NavigationKey.Tab when step is NavigationStep.OpenBackpack or NavigationStep.OpenTargetExitBackpack => Resolve(backpackKey),
        NavigationKey.Tab => 0x09,
        NavigationKey.C => 0x43,
        NavigationKey.Space => 0x20,
        NavigationKey.E => 0x45,
        NavigationKey.Escape => 0x1B,
        _ => throw new InvalidOperationException("Unsupported key")
    };
}
