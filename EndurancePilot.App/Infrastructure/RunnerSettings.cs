using System.Text.Json;
using System.Text.Json.Nodes;
using EndurancePilot.Core;

namespace EndurancePilot.App;

internal sealed record RunnerSettings(HomebaseZone Zone = HomebaseZone.TwinePeaks, int AttemptLimit = 1,
    bool LaunchIfMissing = true, bool RunIndefinitely = true,
    bool ManualThemeEnabled = false, bool ManualDarkTheme = true, int TargetWave = 30,
    WebhookSettings? Webhook = null, bool EndRunWhenTargetReached = true, bool WaitForAssistance = false,
    // Legacy values are preserved for compatibility; they never authorize refocusing.
    bool AutoResumeFortnite = false, int AutoResumeIdleSeconds = 10,
    string StartStopHotkey = RunnerHotkey.Default, bool CloseToTray = true, bool StartWithWindows = false, bool AutoImportPenny = true)
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
        if (AutoResumeIdleSeconds is < 2 or > 600)
        {
            throw new InvalidDataException("Choose a Fortnite focus delay from 2 to 600 seconds.");
        }
        _ = RunnerHotkey.Resolve(StartStopHotkey);
    }
}

internal sealed class SettingsStore(string? path = null)
{
    public string FilePath { get; } = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EndurancePilot", "settings.json");
    private string BackupPath => FilePath + ".bak";
    private static RunnerSettings Read(string path)
    {
        var settings = JsonSerializer.Deserialize<RunnerSettings>(File.ReadAllText(path), Inspection.Json)
            ?? throw new InvalidDataException("Settings file is empty.");
        settings.Validate();
        return settings;
    }
    private static bool ReadError(Exception error) => error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException;
    public RunnerSettings Load(out string? warning)
    {
        warning = null;
        if (!File.Exists(FilePath) && !File.Exists(BackupPath))
        {
            return new();
        }

        try
        {
            return Read(FilePath);
        }
        catch (Exception error) when (ReadError(error))
        {
            try
            {
                var recovered = Read(BackupPath);
                warning = "Saved settings could not be read. Recovered the previous saved settings from backup.";
                return recovered;
            }
            catch (Exception backupError) when (ReadError(backupError)) { }
            warning = "Saved settings could not be read. Temporary defaults are shown; the original files are preserved and saving is blocked until they can be recovered.";
            return new();
        }
    }
    public void Save(RunnerSettings settings)
    {
        settings.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
        // Preserve fields written by another version, including nested notification options.
        JsonObject? previous = null;
        bool validPrimary = false;
        foreach (var candidate in new[] { FilePath, BackupPath })
        {
            if (!File.Exists(candidate)) continue;
            try
            {
                _ = Read(candidate);
                previous = JsonNode.Parse(File.ReadAllText(candidate))!.AsObject();
                validPrimary = candidate == FilePath;
                break;
            }
            catch (Exception error) when (ReadError(error)) { }
        }
        if (previous is null && (File.Exists(FilePath) || File.Exists(BackupPath)))
            throw new InvalidDataException("Saved settings could not be read. Existing settings have been preserved; recover settings.json or its .bak file before saving.");
        var next = JsonSerializer.SerializeToNode(settings, Inspection.Json)!.AsObject();
        static void Merge(JsonObject destination, JsonObject values)
        {
            foreach (var entry in values)
            {
                if (entry.Value is JsonObject nested && destination[entry.Key] is JsonObject existing) Merge(existing, nested);
                else destination[entry.Key] = entry.Value?.DeepClone();
            }
        }
        previous ??= new JsonObject();
        Merge(previous, next);
        var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, previous, Inspection.Json);
                stream.Flush(flushToDisk: true);
            }
            if (validPrimary) File.Replace(temporary, FilePath, BackupPath);
            else
            {
                // Keep unreadable input for recovery, without replacing the valid backup with it.
                if (File.Exists(FilePath)) File.Copy(FilePath, FilePath + ".unreadable-" + Guid.NewGuid().ToString("N"));
                File.Move(temporary, FilePath, overwrite: true);
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
