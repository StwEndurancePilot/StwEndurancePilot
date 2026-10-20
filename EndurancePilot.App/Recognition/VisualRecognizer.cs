using System.Drawing.Imaging;
using EndurancePilot.Core;
using OpenCvSharp;
using CvSize = OpenCvSharp.Size;

namespace EndurancePilot.App;

internal sealed class VisualRecognizer
{
    private readonly string assetDirectory;
    public VisualRecognizer(string? assetDirectory = null) => this.assetDirectory = assetDirectory ?? Path.Combine(AppContext.BaseDirectory, "assets");

    public Observation Enrich(Bitmap image, Observation observation, ScreenKind screen, bool requireHomebaseIcon = true)
    {
        var visuals = observation.Visuals?.ToList() ?? new List<VisualRegion>();
        if (screen is ScreenKind.Unknown or ScreenKind.SaveTheWorldHub)
        {
            visuals.AddRange(HealthBarDetector.Find(image));
        }

        if (screen is ScreenKind.FortniteLobby or ScreenKind.ModeDiscovery)
        {
            var arrow = Match(image, "discover-chevron", false, .45, 2.3, new Box(image.Width * .2, image.Height * .75, image.Width * .6, image.Height * .25));
            if (arrow is not null)
            {
                visuals.Add(arrow);
            }
        }
        if (screen == ScreenKind.FortniteLobby || screen == ScreenKind.ModeSelection && !NavigationPlanner.SaveTheWorldPlayControls(observation).Any())
        {

            double scale = Math.Min(1, Math.Min(1600d / image.Width, 900d / image.Height));
            using var reduced = new Bitmap(Math.Max(1, (int)Math.Round(image.Width * scale)), Math.Max(1, (int)Math.Round(image.Height * scale)));
            using (var graphics = Graphics.FromImage(reduced))
            {
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(image, 0, 0, reduced.Width, reduced.Height);
            }
            var match = Match(reduced, "stw-discovery-tile", false, .3, 2.5,
                new Box(0, reduced.Height * .65, reduced.Width, reduced.Height * .35));
            if (match is not null)
            {
                double sx = (double)reduced.Width / image.Width, sy = (double)reduced.Height / image.Height;
                visuals.Add(match with
                {
                    Bounds = new(match.Bounds.X / sx, match.Bounds.Y / sy, match.Bounds.Width / sx, match.Bounds.Height / sy)
                });
            }
        }
        if (DiscoveryNavigation.IsBrowser(screen))
        {
            if (DiscoveryNavigation.ByEpic(observation) is { } epic && HasLightButtonBackground(image, epic))
            {
                visuals.Add(new("by-epic-selected", epic, 1, 1));
            }

            if (screen == ScreenKind.DiscoverResults && DiscoveryNavigation.HasQuery(observation))
            {
                var results = AutomationController.UniqueTarget(observation.Find("^ISLANDS RESULTS$").ToArray());
                if (results is { } heading)
                {
                    var tile = MatchDiscoveryTile(image, heading);
                    if (tile is not null)
                    {
                        visuals.Add(tile);
                    }
                }
            }
        }
        if (screen == ScreenKind.ModeDiscovery)
        {
            var lobby = AutomationController.UniqueTarget(observation.Find(@"^CURRENT LOBBY$").ToArray());
            if (lobby is { } text)
            {
                var area = new Box(Math.Max(0, text.X - text.Height), text.Bottom,
                    text.Height * 13, text.Height * 10);
                var match = Match(image, "stw-discovery-tile", false,
                    Math.Max(.25, text.Height / 34.67 * .65), text.Height / 34.67 * 1.4, area);
                if (match is not null)
                {
                    visuals.Add(match);
                }
            }
        }
        if (screen == ScreenKind.Unknown)
        {

            var match = Match(image, "quest-log-prompt", false, .4, 2,
                            new Box(0, 0, image.Width, image.Height));
            if (match is not null)
            {
                visuals.Add(match);
            }

            if (match is not { Score: >= .82, Margin: >= .08 })
            {
                var board = Match(image, "hub-quest-board", false, .4, 2, new Box(0, 0, image.Width, image.Height));
                if (board is { Score: >= .82, Margin: >= .08 })
                {
                    visuals.Add(board);
                    double scale = board.Bounds.Width / 420;
                    var door = Match(image, "hub-xp-door", false, scale * .85, scale * 1.15,
                        new Box(board.Bounds.Right, Math.Max(0, board.Bounds.Y - board.Bounds.Height),
                            board.Bounds.Width, board.Bounds.Height * 3));
                    if (door is not null)
                    {
                        visuals.Add(door);
                    }
                }
            }
        }
        if (screen == ScreenKind.EnduranceSetup)
        {
            var label = AutomationController.UniqueTarget(observation.Find(@"^WAIT FOR ASSISTANCE$").ToArray());
            if (label is { } text)
            {
                var area = new Box(text.X - text.Height * 4, text.Y - text.Height * 1.8,
                    text.Height * 4, text.Height * 4);
                var on = Match(image, "assistance-checked", false, Math.Max(.25, text.Height / 21.33 * .75), text.Height / 21.33 * 1.35, area);
                var off = Match(image, "assistance-unchecked", false, Math.Max(.25, text.Height / 21.33 * .75), text.Height / 21.33 * 1.35, area);

                if (on is not null || off is not null)
                {
                    var best = on is not null && (off is null || on.Score > off.Score) ? on : off!;
                    double otherScore = best == on ? off?.Score ?? 0 : on?.Score ?? 0;
                    visuals.Add(best with
                    {
                        Margin = Math.Min(best.Margin, best.Score - otherScore)
                    });
                }
            }
        }
        if ((screen == ScreenKind.WorldMap || screen == ScreenKind.MissionMap && NavigationPlanner.HasSelectedHomebase(observation))
            && !observation.Has(@"^SELECT$"))
        {
            var match = Match(image, "select-control", greenOnly: false, minimumScale: .4, maximumScale: 2,
                new Box(0, 0, image.Width, image.Height));
            if (match is not null)
            {
                visuals.Add(match);
            }
        }
        if (screen == ScreenKind.HomebaseChoice && !observation.Has(NavigationPlanner.MissionLaunchLabel))
        {
            var match = Match(image, "launch-control", greenOnly: false, minimumScale: .4, maximumScale: 2,
                new Box(0, 0, image.Width, image.Height));
            if (match is not null)
            {
                visuals.Add(match);
            }
        }
        if (screen == ScreenKind.WorldMap && NavigationPlanner.SelectedZone(observation) is { } zone)
        {
            var title = observation.Find("^" + NavigationPlanner.ZoneName(zone).ToUpperInvariant() + "$")
                .MaxBy(r => r.Bounds.Height);
            if (title is not null)
            {
                var box = title.Bounds;

                string firstWord = NavigationPlanner.ZoneName(zone).Split(' ')[0].ToUpperInvariant();
                var nameWord = observation.Regions.Where(r => r.Pass.EndsWith("-word", StringComparison.Ordinal)
                    && r.Normalized == firstWord && r.Bounds.CenterX >= box.X && r.Bounds.CenterX <= box.Right
                    && r.Bounds.CenterY >= box.Y && r.Bounds.CenterY <= box.Bottom)
                    .MinBy(r => r.Bounds.X);
                double titleLeft = nameWord?.Bounds.X ?? box.X;

                var area = new Box(titleLeft - box.Height * 3, box.Y - box.Height * .4, box.Height * 3, box.Height * 1.8);
                foreach (var id in new[] { "next-zone", "previous-zone" })
                {
                    var match = Match(image, id, greenOnly: false, minimumScale: Math.Max(.25, box.Height / 60d * .65),
                        maximumScale: box.Height / 60d * 1.4, area);
                    if (match is not null)
                    {
                        visuals.Add(match);
                    }
                }
            }
        }
        if (screen == ScreenKind.MissionMap && (requireHomebaseIcon || !NavigationPlanner.HasSelectedHomebase(observation)))
        {

            var menuHeights = observation.Find(@"^(MAP|COMMAND|ARMORY|QUESTS|LOCKER)$")
                            .Select(r => r.Bounds.Height).Order().ToArray();
            double menuHeight = menuHeights.Length == 0 ? 0 : menuHeights[menuHeights.Length / 2];
            double minimumScale = menuHeight > 0 ? Math.Max(.25, menuHeight * 1.4 / 49) : .4;

            var headingGlyphs = NavigationPlanner.HomebaseHeadings(observation)
                            .Select(r => new Box(0, Math.Max(0, r.Bounds.Y - r.Bounds.Height * 1.5), r.Bounds.X,
                                r.Bounds.Height * 4)).ToArray();
            var match = Match(image, "homebase-icon", greenOnly: true, minimumScale: minimumScale,
                maximumScale: Math.Max(3, minimumScale * 5),
                new Box(0, 0, image.Width, image.Height), headingGlyphs);
            if (match is not null)
            {
                visuals.Add(match);
            }
        }
        if (screen == ScreenKind.HomebaseChoice)
        {
            var own = AutomationController.UniqueTarget(HomebaseChoiceControls.OwnLabels(observation));
            if (own is { } label)
            {

                var area = new Box(label.Right + label.Height, label.Y - label.Height,
                    label.Height * 10, label.Height * 3);
                var match = Match(image, "own-homebase-selected", greenOnly: false,
                    minimumScale: Math.Max(.25, label.Height / 23d * .65), maximumScale: label.Height / 23d * 1.4, area);
                if (match is not null)
                {
                    visuals.Add(match);
                }
            }
        }
        return observation with
        {
            Visuals = visuals
        };
    }

    internal bool HomebaseTargetStillPresent(Bitmap image, Box target)
    {
        if (!AutomationController.IsValidTarget(target, image.Width, image.Height))
        {
            return false;
        }

        var area = new Box(target.X - target.Width * .35, target.Y - target.Height * .35,
                    target.Width * 1.7, target.Height * 1.7);
        var match = Match(image, "homebase-icon", true, target.Width / 48 * .9, target.Width / 48 * 1.12, area);
        return match is { Score: >= .82, Margin: >= .08 }
            && match.Bounds.IntersectionOverUnion(target) > .5
            && target.CenterX >= match.Bounds.X && target.CenterX <= match.Bounds.Right
            && target.CenterY >= match.Bounds.Y && target.CenterY <= match.Bounds.Bottom;
    }

    private VisualRegion? Match(Bitmap image, string id, bool greenOnly, double minimumScale, double maximumScale, Box area,
        IReadOnlyList<Box>? exclusions = null, int colourChannel = -1)
    {
        if (greenOnly && colourChannel < 0)
        {
            var standard = Match(image, id, true, minimumScale, maximumScale, area, exclusions, 1);
            if (standard is { Score: >= .82, Margin: >= .08 })
            {
                return standard;
            }

            var matches = new[] { 0, 2, 3 }
                            .Select(channel => Match(image, id, true, minimumScale, maximumScale, area, exclusions, channel))
                            .Append(standard).OfType<VisualRegion>().ToArray();
            if (matches.Length == 0)
            {
                return null;
            }

            var reliable = matches.Where(match => match.Score >= .82 && match.Margin >= .08).ToArray();
            var bestColour = (reliable.Length > 0 ? reliable : matches).MaxBy(match => match.Score)!;
            double competitor = reliable.Where(match => match.Bounds.IntersectionOverUnion(bestColour.Bounds) < .25
                && !MostlyContained(match.Bounds, bestColour.Bounds)).Select(match => match.Score).DefaultIfEmpty(0).Max();
            return bestColour with
            {
                Margin = Math.Min(bestColour.Margin, bestColour.Score - competitor)
            };
        }
        string path = Path.Combine(assetDirectory, (id == "previous-zone" ? "next-zone" : id) + ".png");
        if (!File.Exists(path))
        {
            return null;
        }

        int left = Math.Clamp((int)Math.Floor(area.X), 0, image.Width - 1);
        int top = Math.Clamp((int)Math.Floor(area.Y), 0, image.Height - 1);
        int right = Math.Clamp((int)Math.Ceiling(area.Right), left + 1, image.Width);
        int bottom = Math.Clamp((int)Math.Ceiling(area.Bottom), top + 1, image.Height);
        using var crop = image.Clone(new Rectangle(left, top, right - left, bottom - top), PixelFormat.Format32bppArgb);
        using var sourceColor = new Mat();
        var pixels = crop.LockBits(new Rectangle(System.Drawing.Point.Empty, crop.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            if (pixels.Stride > 0)
            {
                using var bgra = Mat.FromPixelData(crop.Height, crop.Width, MatType.CV_8UC4, pixels.Scan0, pixels.Stride);
                Cv2.CvtColor(bgra, sourceColor, ColorConversionCodes.BGRA2BGR);
            }
            else
            {
                using var bgra = new Mat(crop.Height, crop.Width, MatType.CV_8UC4);
                var row = new byte[crop.Width * 4];
                for (int y = 0; y < crop.Height; y++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(pixels.Scan0 + y * pixels.Stride, row, 0, row.Length);
                    System.Runtime.InteropServices.Marshal.Copy(row, 0, bgra.Ptr(y), row.Length);
                }
                Cv2.CvtColor(bgra, sourceColor, ColorConversionCodes.BGRA2BGR);
            }
        }
        finally { crop.UnlockBits(pixels); }
        using var templateColor = Cv2.ImRead(path, ImreadModes.Color);
        if (templateColor.Empty())
        {
            return null;
        }

        if (id == "previous-zone")
        {
            Cv2.Flip(templateColor, templateColor, FlipMode.Y);
        }

        using var source = Feature(sourceColor, greenOnly, colourChannel);
        using var template = Feature(templateColor, greenOnly, colourChannel == 3 ? 3 : 1);
        var candidates = new List<(Box Bounds, double Score)>();
        var searchAreas = id == "homebase-icon" && source.Width > 400 && source.Height > 400
            ? GreenIconAreas(source, template.Width * minimumScale)
            : new[] { new Rect(0, 0, source.Width, source.Height) };
        foreach (var searchArea in searchAreas)
        {
            using var region = new Mat(source, searchArea);

            for (double scale = minimumScale; scale <= maximumScale * 1.001; scale *= id is "stw-discovery-tile" or "stw-search-tile" or "discover-chevron" ? 1.02 : id == "homebase-icon" ? 1.04 : 1.08)
            {
                int width = Math.Max(8, (int)Math.Round(template.Width * scale));
                int height = Math.Max(8, (int)Math.Round(template.Height * scale));
                if (width > region.Width || height > region.Height)
                {
                    continue;
                }

                using var resized = new Mat();
                Cv2.Resize(template, resized, new CvSize(width, height), 0, 0, InterpolationFlags.Linear);
                using var scores = new Mat();
                Cv2.MatchTemplate(region, resized, scores, TemplateMatchModes.CCoeffNormed);
                for (int peak = 0; peak < 2; peak++)
                {
                    Cv2.MinMaxLoc(scores, out _, out double score, out _, out var location);
                    if (!double.IsFinite(score) || score < .55)
                    {
                        break;
                    }

                    candidates.Add((new Box(left + searchArea.X + location.X, top + searchArea.Y + location.Y, width, height), score));
                    int x = Math.Max(0, location.X - width / 2), y = Math.Max(0, location.Y - height / 2);
                    Cv2.Rectangle(scores, new Rect(x, y, Math.Min(scores.Width - x, width), Math.Min(scores.Height - y, height)), Scalar.All(-1), -1);
                }
            }
        }
        if (exclusions is not null)
        {
            candidates.RemoveAll(c => exclusions.Any(ex => c.Bounds.CenterX >= ex.X && c.Bounds.CenterX <= ex.Right
                && c.Bounds.CenterY >= ex.Y && c.Bounds.CenterY <= ex.Bottom));
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        var best = candidates.MaxBy(c => c.Score);

        double runnerUp = candidates.Where(c => c.Bounds.IntersectionOverUnion(best.Bounds) < .2
                    && !(id == "homebase-icon" && MostlyContained(c.Bounds, best.Bounds)))
                    .Select(c => c.Score).DefaultIfEmpty(0).Max();
        return new(id, best.Bounds, best.Score, best.Score - runnerUp);
    }

    private static Rect[] GreenIconAreas(Mat green, double minimumWidth)
    {

        using var mask = new Mat();
        Cv2.Threshold(green, mask, 40, 255, ThresholdTypes.Binary);
        Cv2.FindContours(mask, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        var areas = new List<Rect>();
        foreach (var contour in contours)
        {
            var bounds = Cv2.BoundingRect(contour);
            if (bounds.Width < minimumWidth * .65 || bounds.Height < minimumWidth * .35
                || bounds.Width / (double)bounds.Height is < .4 or > 2.5
                || Cv2.ContourArea(contour) < bounds.Width * bounds.Height * .15)
            {
                continue;
            }

            int pad = (int)Math.Ceiling(bounds.Width * .75);
            int x = Math.Max(0, bounds.X - pad), y = Math.Max(0, bounds.Y - pad);
            int right = Math.Min(green.Width, bounds.Right + pad), bottom = Math.Min(green.Height, bounds.Bottom + pad);
            areas.Add(new Rect(x, y, right - x, bottom - y));
        }
        return areas.ToArray();
    }

    private static bool MostlyContained(Box a, Box b)
    {
        double intersection = Math.Max(0, Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X))
            * Math.Max(0, Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y));
        return intersection >= .9 * Math.Min(a.Width * a.Height, b.Width * b.Height);
    }

    private static Mat Feature(Mat color, bool greenOnly, int colourChannel = 1)
    {
        var output = new Mat();
        if (!greenOnly)
        {
            Cv2.CvtColor(color, output, ColorConversionCodes.BGR2GRAY);
            return output;
        }
        var channels = Cv2.Split(color);
        try
        {
            if (colourChannel != 3)
            {
                using var other = new Mat();
                Cv2.Max(channels[(colourChannel + 1) % 3], channels[(colourChannel + 2) % 3], other);
                Cv2.Subtract(channels[colourChannel], other, output);
                Cv2.Normalize(output, output, 0, 255, NormTypes.MinMax);
                return output;
            }

            using var maximum = new Mat();
            using var minimum = new Mat();
            Cv2.Max(channels[0], channels[1], maximum);
            Cv2.Max(maximum, channels[2], maximum);
            Cv2.Min(channels[0], channels[1], minimum);
            Cv2.Min(minimum, channels[2], minimum);
            Cv2.Subtract(maximum, minimum, output);
            Cv2.Normalize(output, output, 0, 255, NormTypes.MinMax);
            return output;
        }
        finally
        {
            foreach (var channel in channels)
            {
                channel.Dispose();
            }
        }
    }

    private static bool HasLightButtonBackground(Bitmap image, Box label)
    {
        int bright = 0, total = 0;
        foreach (double x in new[] { label.X - label.Height * .45, label.Right + label.Height * .45 })
        {
            foreach (double y in new[] { label.Y, label.CenterY, label.Bottom })
            {
                if (x < 0 || y < 0 || x >= image.Width || y >= image.Height)
                {
                    return false;
                }

                var p = image.GetPixel((int)x, (int)y);
                total++;
                if (p.R > 170 && p.G > 170 && p.B > 170)
                {
                    bright++;
                }
            }
        }

        return bright == total;
    }

    private VisualRegion? MatchDiscoveryTile(Bitmap image, Box heading)
    {

        double scale = Math.Min(1, Math.Min(1600d / image.Width, 900d / image.Height));
        using var reduced = new Bitmap(Math.Max(1, (int)Math.Round(image.Width * scale)), Math.Max(1, (int)Math.Round(image.Height * scale)));
        using (var graphics = Graphics.FromImage(reduced))
        {
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(image, 0, 0, reduced.Width, reduced.Height);
        }
        double sx = (double)reduced.Width / image.Width, sy = (double)reduced.Height / image.Height;
        double expected = heading.Height * sy / 20;
        var match = Match(reduced, "stw-search-tile", false, Math.Max(.2, expected * .75), expected * 1.4,
            new Box(0, heading.Bottom * sy, reduced.Width, reduced.Height - heading.Bottom * sy));
        return match is null ? null : match with
        {
            Bounds = new(match.Bounds.X / sx, match.Bounds.Y / sy, match.Bounds.Width / sx, match.Bounds.Height / sy)
        };
    }

    public static void PrepareAssets(string captures, string output)
    {
        Directory.CreateDirectory(output);

        Crop("stonewood-missions-native", "homebase-icon", new Rectangle(1620, 207, 48, 49));
        Crop("homebase-launch-native", "own-homebase-selected", new Rectangle(1093, 367, 33, 35));
        Crop("world-map-stonewood-native", "next-zone", new Rectangle(188, 317, 42, 51));
        Crop("world-map-stonewood-native", "select-control", new Rectangle(2088, 1177, 140, 47));
        Crop("homebase-launch-native", "launch-control", new Rectangle(1498, 1212, 115, 26));
        Crop("twine-endurance-setup-assistance-native", "assistance-checked", new Rectangle(1127, 824, 57, 56));
        Crop("twine-endurance-setup-solo-native", "assistance-unchecked", new Rectangle(1127, 824, 57, 56));
        Crop("mode-discovery-stw-native", "stw-discovery-tile", new Rectangle(185, 1175, 185, 104));
        Crop("stw-hub-quest-prompt-native", "quest-log-prompt", new Rectangle(1705, 670, 285, 65));
        Crop("live-post-reward-hub-1920x1080", "hub-quest-board", new Rectangle(1085, 425, 420, 300));
        Crop("live-post-reward-hub-1920x1080", "hub-xp-door", new Rectangle(1590, 300, 185, 430));
        void Crop(string capture, string name, Rectangle bounds)
        {
            using var source = new Bitmap(Path.Combine(captures, capture + ".png"));
            using var crop = source.Clone(bounds, PixelFormat.Format32bppArgb);
            crop.Save(Path.Combine(output, name + ".png"), ImageFormat.Png);
        }
    }
}
