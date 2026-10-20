using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using EndurancePilot.Core;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using System.Runtime.InteropServices;

namespace EndurancePilot.App;

public sealed class OcrService
{
    private readonly OcrEngine engine = OcrEngine.TryCreateFromLanguage(new Language("en-US"))
        ?? throw new InvalidOperationException("Windows English OCR is unavailable. Install the English language OCR component.");
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<Observation> ReadAsync(Bitmap source, CancellationToken cancellation = default, bool enhance = true, bool requireExitControls = false)
    {
        await gate.WaitAsync(cancellation);
        try
        {
            var capturedAt = DateTimeOffset.UtcNow;
            var regions = new List<TextRegion>();

            double overviewScale = Math.Min(1, Math.Min(1920d / source.Width, 1080d / source.Height));
            await ReadCrop(source, new Rectangle(0, 0, source.Width, source.Height), overviewScale, "overview", regions, cancellation);
            var overview = new Observation(capturedAt, source.Width, source.Height, regions);
            if (overview.Has(MissionRewardReader.RevealHeading) && RewardCardDetector.Find(source) is { } card)
            {
                Rectangle Crop(double x, double y, double width, double height) => Rectangle.Intersect(
                    new Rectangle(0, 0, source.Width, source.Height),
                    new Rectangle((int)x, (int)y, Math.Max(1, (int)width), Math.Max(1, (int)height)));
                var b = card.Bounds;
                var title = Crop(0, b.Y - b.Height * .3, b.X - b.Width * .1, b.Height * .2);
                if (title.Width > 0 && title.Height > 0)
                {
                    await ReadCrop(source, title, 2, "reward-title", regions, cancellation);

                    regions.AddRange(regions.Where(r => r.Pass == "overview").Select(r => r with { Pass = "reward-count" }).ToArray());
                    await ReadCrop(source, new Rectangle(0, 0, source.Width, source.Height), overviewScale * .85,
                        "reward-count-check", regions, cancellation);
                    var reveal = new Observation(capturedAt, source.Width, source.Height, regions, [card]);
                    if (MissionRewardReader.ReadCard(reveal) is null)
                    {

                        regions.RemoveAll(r => r.Pass is "reward-count" or "reward-count-check");
                        var detail = Crop(b.X - b.Width * .1, b.Y - b.Height * .1, b.Width * 1.2, b.Height * 1.2);
                        await ReadCrop(source, detail, 2, "reward-count", regions, cancellation);
                        await ReadCrop(source, detail, 1.5, "reward-count-check", regions, cancellation);
                        reveal = new(capturedAt, source.Width, source.Height, regions, [card]);
                    }
                    return reveal;
                }
            }
            if (HasCompleteMenuEvidence(overview, requireExitControls))
            {
                return overview;
            }

            if (overviewScale < .9)
            {
                const int tile = 1400, overlap = 180;
                for (int y = 0; y < source.Height; y += tile - overlap)
                {
                    for (int x = 0; x < source.Width; x += tile - overlap)
                    {
                        var bounds = new Rectangle(x, y, Math.Min(tile, source.Width - x), Math.Min(tile, source.Height - y));
                        if (bounds.Width < overlap || bounds.Height < overlap)
                        {
                            continue;
                        }

                        await ReadCrop(source, bounds, 1, $"tile-{x}-{y}", regions, cancellation);
                    }
                }
            }

            var preliminary = new Observation(capturedAt, source.Width, source.Height, regions);
            if (HasCompleteMenuEvidence(preliminary, requireExitControls))
            {
                return preliminary;
            }

            if (new ScreenRecognizer().Recognize(preliminary).Kind == ScreenKind.ModeDiscovery)
            {

                var lobby = AutomationController.UniqueTarget(preliminary.Find(@"^CURRENT LOBBY$").ToArray());
                if (lobby is { } label)
                {
                    var crop = Rectangle.Intersect(new(0, 0, source.Width, source.Height),
                        Rectangle.FromLTRB((int)Math.Max(0, label.X - label.Height), (int)label.Bottom,
                            (int)(label.X + label.Height * 13), (int)(label.Bottom + label.Height * 9)));
                    if (crop.Width > 0 && crop.Height > 0)
                    {
                        await ReadCrop(source, crop, Math.Min(3, 1400d / Math.Max(crop.Width, crop.Height)),
                            "discovery-tile", regions, cancellation);
                    }
                }
                return new(capturedAt, source.Width, source.Height, regions);
            }
            if (new ScreenRecognizer().Recognize(preliminary).Kind == ScreenKind.HomebaseChoice)
            {
                await ReadChoiceControls(source, preliminary, regions, cancellation);
                preliminary = new(capturedAt, source.Width, source.Height, regions);
                if (HasCompleteMenuEvidence(preliminary, requireExitControls))
                {
                    return preliminary;
                }
            }
            if (new ScreenRecognizer().Recognize(preliminary).Kind == ScreenKind.Unknown)
            {

                foreach (var anchor in preliminary.Find(@"^(STORM|ACTIVATE|DEFEND)$")
                                    .Where(r => r.Bounds.Height < 22).Take(6).ToArray())
                {
                    double h = Math.Max(6, anchor.Bounds.Height);
                    var bounds = Rectangle.Intersect(new Rectangle(0, 0, source.Width, source.Height),
                        Rectangle.FromLTRB((int)(anchor.Bounds.X - h * 24), (int)(anchor.Bounds.Y - h * 4),
                            (int)(anchor.Bounds.Right + h * 40), (int)(anchor.Bounds.Bottom + h * 5)));
                    if (bounds.Width > 0 && bounds.Height > 0)
                    {
                        await ReadCrop(source, bounds, Math.Min(4, (double)OcrEngine.MaxImageDimension / Math.Max(bounds.Width, bounds.Height)),
                            "objective-anchor", regions, cancellation);
                    }
                }

                foreach (var timer in preliminary.Find(@"^\d{1,2}:\d{2}$").Where(r => r.Bounds.Height < 24).Take(6).ToArray())
                {
                    double h = Math.Max(8, timer.Bounds.Height);
                    var bounds = Rectangle.Intersect(new Rectangle(0, 0, source.Width, source.Height),
                        new Rectangle((int)(timer.Bounds.CenterX - h * 12), (int)(timer.Bounds.Y - h * 2), (int)(h * 24), (int)(h * 4)));
                    if (bounds.Width > 0 && bounds.Height > 0)
                    {
                        await ReadCrop(source, bounds, 4, "timer-anchor", regions, cancellation);
                    }
                }

                var anchors = preliminary.Find(@"^(WAVE|ENDURANCE:?|DEFEND|QUEST|ACCESS)$").Select(r => r.Bounds).Take(8).ToArray();
                var crops = new List<Box>();
                foreach (var anchor in anchors)
                {
                    double h = Math.Max(8, anchor.Height);
                    var box = new Box(Math.Max(0, anchor.X - h * 16), Math.Max(0, anchor.Y - h), h * 34, h * 3);
                    if (crops.Any(c => c.IntersectionOverUnion(box) > .5))
                    {
                        continue;
                    }

                    crops.Add(box);
                    var bounds = Rectangle.Intersect(new Rectangle(0, 0, source.Width, source.Height),
                        new Rectangle((int)box.X, (int)box.Y, (int)box.Width, (int)box.Height));
                    await ReadCrop(source, bounds, Math.Min(h < 14 ? 4 : 2, (double)OcrEngine.MaxImageDimension / Math.Max(bounds.Width, bounds.Height)),
                        "text-anchor-" + crops.Count, regions, cancellation);
                }
                preliminary = new(capturedAt, source.Width, source.Height, regions);
            }
            if (source.Width < 1600 || new ScreenRecognizer().Recognize(preliminary).Kind is ScreenKind.Unknown or ScreenKind.HomebasePreparing or ScreenKind.Backpack or ScreenKind.RejoinDialog)
            {
                const int width = 800, height = 500, overlap = 120;
                for (int y = 0; y < source.Height; y += height - overlap)
                {
                    for (int x = 0; x < source.Width; x += width - overlap)
                    {
                        var bounds = new Rectangle(x, y, Math.Min(width, source.Width - x), Math.Min(height, source.Height - y));
                        if (bounds.Width < overlap || bounds.Height < overlap)
                        {
                            continue;
                        }

                        await ReadCrop(source, bounds, 2, $"detail-{x}-{y}", regions, cancellation);
                    }
                }
            }
            var final = new Observation(capturedAt, source.Width, source.Height, regions);
            if (new ScreenRecognizer().Recognize(final).Kind == ScreenKind.HomebasePreparing)
            {

                var details = AutomationController.UniqueTarget(final.Find(@"^MISSION DETAILS$").ToArray());
                var abandon = AutomationController.UniqueTarget(final.Find(@"^ABANDON$").ToArray());
                if (details is { } left && abandon is { } right)
                {
                    double h = Math.Max(left.Height, right.Height);
                    if (Math.Abs(left.CenterY - right.CenterY) < h * 2 && left.CenterX < right.CenterX)
                    {
                        var crop = Rectangle.Intersect(new(0, 0, source.Width, source.Height),
                            Rectangle.FromLTRB((int)(left.X - h * 4), (int)(Math.Min(left.Y, right.Y) - h * 12),
                                (int)(right.Right + h * 4), (int)Math.Max(left.Bottom, right.Bottom)));
                        if (crop.Width > 0 && crop.Height > 0)
                        {
                            double scale = Math.Min(3, (double)OcrEngine.MaxImageDimension / Math.Max(crop.Width, crop.Height));
                            await ReadCrop(source, crop, scale, "loadout-controls", regions, cancellation);
                            final = new(capturedAt, source.Width, source.Height, regions);
                        }
                    }
                }
            }
            if (enhance && NeedsEnhancement(final, requireExitControls))
            {

                await ReadCrop(source, new(0, 0, source.Width, source.Height), overviewScale,
                                    "contrast-overview", regions, cancellation, TextTreatment.LocalContrast);
                final = new(capturedAt, source.Width, source.Height, regions);
                if (NeedsEnhancement(final, requireExitControls))
                {
                    foreach (var crop in TextEnhancement.Crops(final))
                    {
                        double scale = Math.Min(Math.Clamp(120d / crop.Height, 2, 6),
                            (double)OcrEngine.MaxImageDimension / Math.Max(crop.Width, crop.Height));
                        await ReadCrop(source, crop, scale, "contrast-crop", regions, cancellation, TextTreatment.LocalContrast);
                        final = new(capturedAt, source.Width, source.Height, regions);
                        if (!NeedsEnhancement(final, requireExitControls))
                        {
                            break;
                        }
                    }
                }
            }
            return final;
        }
        finally { gate.Release(); }
    }

    private static bool HasCompleteMenuEvidence(Observation frame, bool requireExitControls = false)
    {

        var kind = new ScreenRecognizer().Recognize(frame).Kind;
        return kind switch
        {
            ScreenKind.DiscoverBrowser or ScreenKind.DiscoverResults => DiscoveryNavigation.SearchField(frame) is not null,
            ScreenKind.SaveTheWorldDetails => AutomationController.UniqueTarget(frame.Find("^SELECT$").ToArray()) is not null,
            ScreenKind.WorldMap => NavigationPlanner.SelectedZone(frame) is not null && frame.Has(@"^SELECT$"),
            ScreenKind.MissionMap => NavigationPlanner.HasSelectedHomebase(frame)
                && NavigationPlanner.SelectedZone(frame) is not null
                && AutomationController.UniqueTarget(frame.Find(@"^SELECT$").ToArray()) is not null,
            ScreenKind.HomebaseChoice => AutomationController.UniqueTarget(HomebaseChoiceControls.OwnLabels(frame)) is not null
                && AutomationController.UniqueTarget(frame.Find(NavigationPlanner.MissionLaunchLabel).ToArray()) is not null,
            ScreenKind.HomebaseReady => AutomationController.UniqueTarget(frame.Find(NavigationPlanner.MissionLaunchLabel).ToArray()) is not null,
            ScreenKind.Homebase => StormShieldNavigation.InactiveObjective(frame) is not null,
            ScreenKind.StormShieldConsole => StormShieldNavigation.IsConsole(frame) && (!requireExitControls || TargetWaveExit.LeaveControl(frame) is not null),
            ScreenKind.LeaveConfirmation => AutomationController.UniqueTarget(new ScreenRecognizer().Recognize(frame).Targets) is not null,
            ScreenKind.RejoinDialog => AutomationController.UniqueTarget(RejoinDialog.Recognize(frame)!.Targets) is not null,
            ScreenKind.ErrorDialog or ScreenKind.NeedsAttention => AutomationController.UniqueTarget(new ScreenRecognizer().Recognize(frame).Targets) is not null,
            ScreenKind.SleepMode => AutomationController.UniqueTarget(new ScreenRecognizer().Recognize(frame).Targets) is not null,
            ScreenKind.FeedbackSurvey or ScreenKind.NewUnlock => AutomationController.UniqueTarget(new ScreenRecognizer().Recognize(frame).Targets) is not null,
            ScreenKind.Loading or ScreenKind.Matchmaking => true,
            _ => false
        };
    }

    private static bool NeedsEnhancement(Observation frame, bool requireExitControls = false)
    {
        var screen = new ScreenRecognizer().Recognize(frame);
        return screen.Kind is ScreenKind.Unknown or ScreenKind.HomebasePreparing or ScreenKind.Backpack
            || requireExitControls && screen.Kind == ScreenKind.StormShieldConsole && TargetWaveExit.LeaveControl(frame) is null
            || screen.Kind == ScreenKind.HomebaseChoice && !HasCompleteMenuEvidence(frame)
            || screen.Kind is ScreenKind.RejoinDialog or ScreenKind.ErrorDialog or ScreenKind.NeedsAttention or ScreenKind.SleepMode
                && AutomationController.UniqueTarget(screen.Targets) is null;
    }

    internal async Task ReadChoiceControls(Bitmap source, Observation frame, List<TextRegion> regions,
        CancellationToken cancellation = default)
    {

        var own = AutomationController.UniqueTarget(HomebaseChoiceControls.OwnLabels(frame));
        if (own is null || AutomationController.UniqueTarget(frame.Find(NavigationPlanner.MissionLaunchLabel).ToArray()) is not null)
        {
            return;
        }

        double scale = Math.Clamp(48 / own.Value.Height, 2, 4);
        int tile = Math.Max(160, (int)(1400 / scale));
        int stride = Math.Max(80, tile - 80);
        for (int y = Math.Clamp((int)own.Value.Bottom, 0, source.Height - 1); y < source.Height; y += stride)
        {
            for (int x = 0; x < source.Width; x += stride)
            {
                var crop = new Rectangle(x, y, Math.Min(tile, source.Width - x), Math.Min(tile, source.Height - y));
                if (crop.Width < 40 || crop.Height < 20)
                {
                    continue;
                }

                await ReadCrop(source, crop, scale, "choice-controls", regions, cancellation);
            }
        }
    }

    internal async Task ReadCrop(Bitmap source, Rectangle crop, double scale, string pass,
        List<TextRegion> regions, CancellationToken cancellation, TextTreatment treatment = TextTreatment.Original)
    {
        cancellation.ThrowIfCancellationRequested();
        int width = Math.Max(1, (int)Math.Round(crop.Width * scale));
        int height = Math.Max(1, (int)Math.Round(crop.Height * scale));
        if (width > OcrEngine.MaxImageDimension || height > OcrEngine.MaxImageDimension)
        {
            throw new InvalidOperationException("OCR crop exceeds Windows OCR image limits.");
        }

        using var scaled = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(scaled))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;

            if (pass.StartsWith("text-anchor-") || treatment != TextTreatment.Original)
            {
                graphics.PixelOffsetMode = PixelOffsetMode.Half;
            }

            graphics.DrawImage(source, new Rectangle(0, 0, width, height), crop, GraphicsUnit.Pixel);
        }
        SoftwareBitmap bitmap;
        if (treatment == TextTreatment.Original)
        {

            var pixels = new byte[checked(width * height * 4)];
            var data = scaled.LockBits(new(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int row = 0; row < height; row++)
                {
                    Marshal.Copy(data.Scan0 + row * data.Stride, pixels, row * width * 4, width * 4);
                }
            }
            finally { scaled.UnlockBits(data); }
            using var writer = new DataWriter();
            writer.WriteBytes(pixels);
            bitmap = SoftwareBitmap.CreateCopyFromBuffer(writer.DetachBuffer(), BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);
        }
        else
        {
            var encoded = TextEnhancement.Encode(scaled, treatment);
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(encoded);
                await writer.StoreAsync();
                writer.DetachStream();
            }
            stream.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        }
        OcrResult result;
        using (bitmap)
        {
            result = await engine.RecognizeAsync(bitmap);
        }

        cancellation.ThrowIfCancellationRequested();
        Box Map(double x, double y, double w, double h) => OcrGeometry.Map(new(x, y, w, h), width, height,
            result.TextAngle ?? 0, new(crop.X, crop.Y, crop.Width, crop.Height));
        foreach (var line in result.Lines)
        {
            if (line.Words.Count == 0)
            {
                continue;
            }

            double left = line.Words.Min(w => w.BoundingRect.X), top = line.Words.Min(w => w.BoundingRect.Y);
            double right = line.Words.Max(w => w.BoundingRect.Right), bottom = line.Words.Max(w => w.BoundingRect.Bottom);
            var region = new TextRegion(line.Text, Map(left, top, right - left, bottom - top), pass);
            if (pass.StartsWith("reward-", StringComparison.Ordinal)
                || !regions.Any(r => r.Normalized == region.Normalized && r.Bounds.IntersectionOverUnion(region.Bounds) > .35))
            {
                regions.Add(region);
            }

            if (line.Words.Count > 1)
            {
                foreach (var word in line.Words)
                {
                    var box = word.BoundingRect;
                    var token = new TextRegion(word.Text, Map(box.X, box.Y, box.Width, box.Height), pass + "-word");
                    if (!regions.Any(r => r.Normalized == token.Normalized && r.Bounds.IntersectionOverUnion(token.Bounds) > .35))
                    {
                        regions.Add(token);
                    }
                }
            }
        }
    }

    public static Bitmap Annotate(Bitmap source, Observation observation)
    {
        var output = new Bitmap(source);
        using var graphics = Graphics.FromImage(output);
        using var pen = new Pen(Color.FromArgb(210, 77, 225, 181), Math.Max(1, source.Width / 1200f));
        foreach (var region in observation.Regions.Where(r => !r.Pass.EndsWith("-word")))
        {
            graphics.DrawRectangle(pen, (float)region.Bounds.X, (float)region.Bounds.Y, (float)region.Bounds.Width, (float)region.Bounds.Height);
        }

        using var visualPen = new Pen(Color.Gold, Math.Max(2, source.Width / 1000f));
        foreach (var region in observation.Visuals ?? [])
        {
            graphics.DrawRectangle(visualPen, (float)region.Bounds.X, (float)region.Bounds.Y, (float)region.Bounds.Width, (float)region.Bounds.Height);
        }

        return output;
    }
}
