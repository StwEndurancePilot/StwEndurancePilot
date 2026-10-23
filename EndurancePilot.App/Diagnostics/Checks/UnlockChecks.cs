using System.Drawing.Imaging;
using EndurancePilot.Core;

namespace EndurancePilot.App;

internal static class UnlockChecks
{
    internal static async Task Run(string capture)
    {
        using var original = new Bitmap(capture);
        var ocr = new OcrService();
        var frame = await ocr.ReadAsync(original);
        var screen = new ScreenRecognizer().Recognize(frame);
        var plan = NavigationPlanner.Decide(frame, screen, new()).Plan;
        if (plan?.Step != NavigationStep.ContinueUnlock)
        {
            throw new InvalidOperationException("The supplied image does not show a recognized unlock announcement.");
        }

        var action = new ClickAction(Guid.NewGuid(), plan.Description, screen.Kind, plan.Target,
            frame.Width, frame.Height, frame.CapturedAt, Step: plan.Step);
        if (!GameInput.UnlockTargetStillPresent(frame, action)
            || GameInput.UnlockTargetStillPresent(frame with { Regions = frame.Regions.Where(r => r.Normalized != "NEW").ToArray() }, action)
            || GameInput.UnlockTargetStillPresent(frame, action with { Target = new Box(0, 0, 100, 100) }))
        {
            throw new InvalidOperationException("Unlock target verification accepted a wrong screen or target.");
        }

        using var recolored = new Bitmap(original.Width, original.Height);
        using (var attributes = new ImageAttributes())
        {
            var matrix = new ColorMatrix { Matrix00 = 1, Matrix11 = .6f, Matrix22 = 1, Matrix33 = 1, Matrix44 = 1 };
            attributes.SetColorMatrix(matrix);
            using var graphics = Graphics.FromImage(recolored);
            graphics.DrawImage(original, new Rectangle(0, 0, recolored.Width, recolored.Height),
                0, 0, original.Width, original.Height, GraphicsUnit.Pixel, attributes);
        }
        var changed = await ocr.ReadAsync(recolored, enhance: false);
        bool reread = GameInput.UnlockTargetStillPresent(changed, action);
        bool pixelsMatch = GameInput.TargetStillMatches(original, recolored, action.Target);
        if (!reread || pixelsMatch)
        {
            throw new InvalidOperationException($"A readable unlock with changed button pixels was rejected. Reread={reread}; pixelsMatch={pixelsMatch}.");
        }
    }
}
