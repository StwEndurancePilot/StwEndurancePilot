using System.Drawing.Imaging;
using System.Text.Json;
using System.Text.Json.Serialization;
using EndurancePilot.Core;

namespace EndurancePilot.App;

public static class Inspection
{
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task AnalyzeFile(string input, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        using var bitmap = new Bitmap(input);
        var observation = await new OcrService().ReadAsync(bitmap);
        var evidence = new ScreenRecognizer().Recognize(observation);
        observation = new VisualRecognizer().Enrich(bitmap, observation, evidence.Kind);
        evidence = new ScreenRecognizer().Recognize(observation);
        var navigation = NavigationPlanner.Decide(observation, evidence, new(HomebaseZone.Stonewood));
        string stem = Path.GetFileNameWithoutExtension(input);
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, stem + ".json"),
            JsonSerializer.Serialize(new
            {
                Screen = evidence,
                Observation = observation,
                Navigation = navigation
            }, Json));
        using var overlay = OcrService.Annotate(bitmap, observation);
        overlay.Save(Path.Combine(outputDirectory, stem + ".overlay.png"), ImageFormat.Png);
    }
}
