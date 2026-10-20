using System.Drawing.Imaging;
using System.Text.Json;
using EndurancePilot.Core;

namespace EndurancePilot.App;

internal static class AccessibilityChecks
{
    internal static async Task Run(string captures, string output)
    {
        Directory.CreateDirectory(output);
        var reports = new List<object>();
        int failures = 0;
        var ocr = new OcrService();
        foreach (var (name, step) in new[] {
            ("reported-play-with-others-map", NavigationStep.SelectHomebase),
            ("live-twine-triangle-stall-1918x1030", NavigationStep.SelectHomebase),
            ("feedback-survey", NavigationStep.SkipFeedback),
            ("new-unlock", NavigationStep.ContinueUnlock) })
        {
            using var original = new Bitmap(Path.Combine(captures, name + ".png"));
            Box? baseline = null;
            foreach (string variant in new[] { "original", "dim", "bright", "hue-rotation" })
            {
                using var bitmap = new Bitmap(original.Width, original.Height);
                float gain = variant == "dim" ? .5f : variant == "bright" ? 1.25f : 1;
                var matrix = new ColorMatrix();
                matrix.Matrix00 = matrix.Matrix11 = matrix.Matrix22 = gain;
                if (variant == "hue-rotation")
                {
                    matrix.Matrix00 = matrix.Matrix11 = matrix.Matrix22 = 0;
                    matrix.Matrix01 = matrix.Matrix12 = matrix.Matrix20 = 1;
                }
                using var attributes = new ImageAttributes();
                attributes.SetColorMatrix(matrix);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.DrawImage(original, new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                        0, 0, original.Width, original.Height, GraphicsUnit.Pixel, attributes);
                }

                var observation = await ocr.ReadAsync(bitmap);
                var screen = new ScreenRecognizer().Recognize(observation);
                observation = new VisualRecognizer().Enrich(bitmap, observation, screen.Kind);
                screen = new ScreenRecognizer().Recognize(observation);
                var plan = NavigationPlanner.Decide(observation, screen, new(HomebaseZone.TwinePeaks, true)).Plan;
                if (variant == "original")
                {
                    baseline = plan?.Target;
                }

                bool passed = plan?.Step == step && baseline is { } target
                    && plan.Target.IntersectionOverUnion(target) > .5
                    && (step != NavigationStep.SelectHomebase
                        || new VisualRecognizer().HomebaseTargetStillPresent(bitmap, plan.Target));
                if (!passed)
                {
                    failures++;
                }

                reports.Add(new
                {
                    name,
                    variant,
                    Passed = passed,
                    Screen = screen.Kind,
                    Plan = plan,
                    observation.Visuals
                });
            }
        }
        File.WriteAllText(Path.Combine(output, "results.json"),
            JsonSerializer.Serialize(new
            {
                Checks = reports.Count,
                Failures = failures,
                Reports = reports
            }, Inspection.Json));
        Environment.ExitCode = failures == 0 ? 0 : 1;
    }
}
