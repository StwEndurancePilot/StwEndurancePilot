using EndurancePilot.Core;
using System.Text.Json;

namespace EndurancePilot.App;

internal static class LiveSleepCheck
{
    // Explicit diagnostic: dismiss only a verified sleep prompt, then verify it left.
    internal static async Task Run(string output)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var token = timeout.Token;
        var game = GameWindow.Find() ?? throw new InvalidOperationException("Fortnite window unavailable.");
        var ocr = new OcrService();
        var recognizer = new ScreenRecognizer();
        using var capture = new BackgroundGameCapture(game);
        using var background = await capture.CaptureAsync(token) ?? throw new InvalidOperationException("No frame.");
        var evidence = recognizer.Recognize(await ocr.ReadAsync(background, token));
        if (evidence.Kind != ScreenKind.SleepMode || AutomationController.UniqueTarget(evidence.Targets) is null)
            throw new InvalidOperationException("No unambiguous sleep prompt. No input sent.");
        if (!await GameInput.FocusForAutomationAsync(game, token)) throw new InvalidOperationException("Windows blocked focus.");
        var controller = new AutomationController();
        controller.Start();
        bool sent = false, dismissed = false;
        for (int attempt = 0; attempt < 12; attempt++)
        {
            await Task.Delay(200, token);
            game = GameWindow.Find() ?? throw new InvalidOperationException("Window lost.");
            using var image = game.Capture();
            var observation = await ocr.ReadAsync(image, token);
            evidence = recognizer.Recognize(observation);
            if (evidence.Kind != ScreenKind.SleepMode)
            {
                dismissed = evidence.Kind != ScreenKind.Unknown;
                if (dismissed) break;
                continue;
            }
            var action = controller.Observe(observation, evidence);
            if (!sent && action?.Step == NavigationStep.KeepPlaying)
            {
                sent = await GameInput.DispatchAsync(game, image, action, ocr, () => !token.IsCancellationRequested, token);
                if (sent) controller.Dispatched(action, DateTimeOffset.UtcNow);
            }
        }
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { Sent = sent, Dismissed = dismissed, Screen = evidence.Kind.ToString() }));
        if (!dismissed) throw new InvalidOperationException("Sleep dismissal was not confirmed.");
    }
}
