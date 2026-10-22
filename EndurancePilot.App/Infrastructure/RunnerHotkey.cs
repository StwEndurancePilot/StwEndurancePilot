namespace EndurancePilot.App;

internal static class RunnerHotkey
{
    internal const string Default = "F10";
    internal static readonly IReadOnlyList<string> Choices =
    [
        "F6", "F7", "F8", "F9", "F10", "F11",
        "Ctrl+Alt+F6", "Ctrl+Alt+F7", "Ctrl+Alt+F8",
        "Ctrl+Alt+F9", "Ctrl+Alt+F10", "Ctrl+Alt+F11"
    ];

    internal static (uint Modifiers, uint Key) Resolve(string choice)
    {
        if (!Choices.Contains(choice, StringComparer.Ordinal))
        {
            throw new InvalidDataException("Choose one of the listed start/stop shortcuts.");
        }

        bool modified = choice.StartsWith("Ctrl+Alt+", StringComparison.Ordinal);
        string keyName = modified ? choice[9..] : choice;
        uint key = 0x6Fu + uint.Parse(keyName[1..]); // F6 through F11: VK_F1 is 0x70.
        return (0x4000u | (modified ? 0x0003u : 0u), key);
    }
}
