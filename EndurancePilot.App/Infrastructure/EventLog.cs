using System.Text.Json;

namespace EndurancePilot.App;

internal sealed class EventLog
{
    private static readonly JsonSerializerOptions Options = new(Inspection.Json) { WriteIndented = false };
    public string DirectoryPath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EndurancePilot", "logs");
    public void Write(string kind, object data)
    {
        Directory.CreateDirectory(DirectoryPath);
        var now = DateTimeOffset.UtcNow;
        string path = Path.Combine(DirectoryPath, now.ToString("yyyy-MM-dd") + ".jsonl");
        File.AppendAllText(path, JsonSerializer.Serialize(new
        {
            Time = now,
            Kind = kind,
            Data = data
        }, Options) + Environment.NewLine);
    }

    public void SaveObservation(EndurancePilot.Core.Observation observation, EndurancePilot.Core.ScreenEvidence screen)
    {
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(Path.Combine(DirectoryPath, "last-observation.json"),
            JsonSerializer.Serialize(new
            {
                Observation = observation,
                Screen = screen
            }, Inspection.Json));
    }
}
