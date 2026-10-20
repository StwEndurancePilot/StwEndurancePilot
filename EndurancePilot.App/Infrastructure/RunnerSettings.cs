using System.Text.Json;
using EndurancePilot.Core;

namespace EndurancePilot.App;

internal sealed record RunnerSettings(HomebaseZone Zone = HomebaseZone.TwinePeaks, int AttemptLimit = 1,
    bool LaunchIfMissing = true, bool RunIndefinitely = true,
    bool ManualThemeEnabled = false, bool ManualDarkTheme = true, int TargetWave = 30,
    WebhookSettings? Webhook = null, bool EndRunWhenTargetReached = true)
{
    public void Validate()
    {
        Webhook?.Validate();
        if (!Enum.IsDefined(Zone) || Zone == HomebaseZone.Unspecified || AttemptLimit is < 1 or > 100)
        {
            throw new InvalidDataException("Choose a valid homebase and an attempt limit from 1 to 100.");
        }

        if (TargetWave is < 1 or > 30)
        {
            throw new InvalidDataException("Choose a target wave from 1 to 30.");
        }
    }
}

internal sealed class SettingsStore(string? path = null)
{
    public string FilePath { get; } = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EndurancePilot", "settings.json");
    public RunnerSettings Load(out string? warning)
    {
        warning = null;
        if (!File.Exists(FilePath))
        {
            return new();
        }

        try
        {
            var settings = JsonSerializer.Deserialize<RunnerSettings>(File.ReadAllText(FilePath), Inspection.Json)
                ?? throw new InvalidDataException("Settings file is empty.");
            settings.Validate();
            return settings;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            warning = "Saved settings could not be read. Defaults loaded: " + error.Message;
            return new();
        }
    }
    public void Save(RunnerSettings settings)
    {
        settings.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Inspection.Json));
        File.Move(temporary, FilePath, overwrite: true);
    }
}
