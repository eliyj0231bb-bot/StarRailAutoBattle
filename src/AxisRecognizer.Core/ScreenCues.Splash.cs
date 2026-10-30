using OpenCvSharp;

namespace AxisRecognizer;

/// <summary>
/// An ultimate's full-screen splash (R56): the ultimate's name at the left middle with a cyan bar under it.
/// Bar is the cyan bar in pixels; Length is its length relative to game height.
/// </summary>
public sealed record UltSplash(Rect Bar, double Length);

public sealed partial class CueReader
{
    /// <summary>
    /// Where the splash's name bar can be, relative to game height (16:9). Measured on 铁墓 0:19.4–0:19.9: the bar slides in
    /// from the right and settles at x 0.31–0.62, y 0.78, about 0.016 high.
    /// </summary>
    public static readonly RelRect SplashNameArea = new(0.12, 0.70, 0.88, 0.14);

    /// <summary>A splash bar is at least this long (relative to game height; the settled bar is about 0.31).</summary>
    public const double SplashBarMin = 0.15;

    /// <summary>…and at most this long (blue backdrops gave runs of 0.34–0.72; real bars 0.28–0.31 on all three samples).</summary>
    public const double SplashBarMax = 0.33;

    /// <summary>
    /// A splash shows for about 0.4 s; it counts once the bar is seen in this many frames in a row (at 15 per second).
    /// Backdrops that look like a bar lasted 1–2 frames on the three samples, splashes 3–8.
    /// </summary>
    public const int SplashMinFrames = 3;

    /// <summary>
    /// The splash's cyan name bar, or null. The bar is a band of cyan rows a few pixels high; its length is the longest
    /// run of columns that are cyan in most of the band's rows (the dark runes printed on it leave small gaps).
    /// </summary>
    public static UltSplash? ReadUltSplash(Mat frame, Rect content)
    {
        var h = content.Height;
        var area = Geometry.Clamp(SplashNameArea.ToPixels(content), frame.Width, frame.Height);
        using var crop = new Mat(frame, area);
        using var hsv = new Mat();
        Cv2.CvtColor(crop, hsv, ColorConversionCodes.BGR2HSV);
        using var mask = new Mat();
        Cv2.InRange(hsv, new Scalar(92, 150, 170), new Scalar(110, 255, 255), mask);

        // The band: rows with the most cyan, and their neighbours with at least half as much.
        var rows = new int[mask.Rows];
        for (var y = 0; y < mask.Rows; y++)
        {
            using var row = mask.Row(y);
            rows[y] = Cv2.CountNonZero(row);
        }

        var peak = Array.IndexOf(rows, rows.Max());
        if (rows[peak] < SplashBarMin * h * 0.5)
        {
            return null;
        }

        var (top, bottom) = (peak, peak);
        while (top > 0 && rows[top - 1] >= rows[peak] / 2)
        {
            top--;
        }

        while (bottom < mask.Rows - 1 && rows[bottom + 1] >= rows[peak] / 2)
        {
            bottom++;
        }

        var thickness = bottom - top + 1;
        if (thickness < h * 0.006 || thickness > h * 0.035)
        {
            return null;
        }

        // Longest run of bar columns, bridging gaps up to 0.02 of game height.
        using var band = new Mat(mask, new Rect(0, top, mask.Cols, thickness));
        using var columns = new Mat();
        Cv2.Reduce(band, columns, ReduceDimension.Row, ReduceTypes.Avg, MatType.CV_32F);
        var maxGap = (int)Math.Round(h * 0.02);
        int bestStart = 0, bestEnd = -1, start = -1, lastBar = -1;
        for (var x = 0; x < columns.Cols; x++)
        {
            if (columns.At<float>(0, x) < 255 * 0.5)
            {
                continue;
            }

            if (start < 0 || x - lastBar > maxGap)
            {
                start = x;
            }

            lastBar = x;
            if (x - start > bestEnd - bestStart)
            {
                (bestStart, bestEnd) = (start, x);
            }
        }

        var length = (bestEnd - bestStart + 1) / (double)h;
        if (length is < SplashBarMin or > SplashBarMax)
        {
            return null;
        }

        // A bar, not a blue backdrop: just above and below it is not cyan (铁墓 0:41.9, 1:02.9: sky and water).
        var near = Math.Max(2, (int)Math.Round(h * 0.004));
        var far = Math.Max(near + 2, (int)Math.Round(h * 0.012));
        double CyanShare(int y0, int y1)
        {
            y0 = Math.Clamp(y0, 0, mask.Rows);
            y1 = Math.Clamp(y1, 0, mask.Rows);
            if (y1 <= y0)
            {
                return 0;
            }

            using var strip = new Mat(mask, new Rect(bestStart, y0, bestEnd - bestStart + 1, y1 - y0));
            return Cv2.CountNonZero(strip) / (double)(strip.Rows * strip.Cols);
        }

        if (CyanShare(top - far, top - near) > SplashEdgeMax || CyanShare(bottom + 1 + near, bottom + 1 + far) > SplashEdgeMax)
        {
            return null;
        }

        return new UltSplash(new Rect(area.X + bestStart, area.Y + top, bestEnd - bestStart + 1, thickness), length);
    }

    /// <summary>Most cyan allowed in the strips just above and below a splash bar.</summary>
    public const double SplashEdgeMax = 0.25;

    /// <summary>The bar has slid into place (the name is fully shown) from this length on.</summary>
    public const double SplashSettledMin = 0.27;

    /// <summary>A splash's name matches a named sample at this score (normalized correlation) or more…</summary>
    public const double SplashNameScore = 0.6;

    /// <summary>…and beats every other unit's samples by this much.</summary>
    public const double SplashNameMargin = 0.1;

    private const int SplashNameW = 296, SplashNameH = 72;

    /// <summary>Share of a name sample trimmed on each side, so the name can shift a little against it.</summary>
    private const double SplashTemplateTrim = 0.08;

    /// <summary>Gray, normalized name crops of the user-named splash samples (R57), by unit id.</summary>
    private readonly List<(string Label, (Mat Gray, Mat Text) Template)> _splashNames = [];

    /// <summary>The ultimate's name above a splash bar: from just left of the bar, 0.37 of game height wide.</summary>
    public static Rect SplashNameRect(UltSplash splash, Rect content, int frameWidth, int frameHeight)
    {
        var h = content.Height;
        return Geometry.Clamp(new Rect(splash.Bar.X - (int)Math.Round(h * 0.03), splash.Bar.Y - (int)Math.Round(h * 0.07),
            (int)Math.Round(h * 0.37), (int)Math.Round(h * 0.09)), frameWidth, frameHeight);
    }

    /// <summary>
    /// A name crop in the two forms compared, at the fixed compare size: gray, and a text mask (the name is white, so
    /// bright unsaturated pixels, slightly blurred). Each alone split one name into two groups: gray when the art
    /// behind it or a video overlay differed (铁墓 1:23.1 subtitle, 绘世 2:41.4 heart), the mask on bright art (亚婆离).
    /// </summary>
    private static (Mat Gray, Mat Text) SplashNameForms(Mat crop)
    {
        using var hsv = new Mat();
        Cv2.CvtColor(crop, hsv, ColorConversionCodes.BGR2HSV);
        using var gray = new Mat();
        Cv2.CvtColor(crop, gray, ColorConversionCodes.BGR2GRAY);
        using var text = new Mat();
        Cv2.InRange(hsv, new Scalar(0, 0, 200), new Scalar(180, 50, 255), text);
        var size = new Size(SplashNameW, SplashNameH);
        var grayOut = new Mat();
        Cv2.Resize(gray, grayOut, size, 0, 0, InterpolationFlags.Area);
        using var textSmall = new Mat();
        Cv2.Resize(text, textSmall, size, 0, 0, InterpolationFlags.Area);
        var textOut = new Mat();
        Cv2.GaussianBlur(textSmall, textOut, new Size(5, 5), 1.2);
        return (grayOut, textOut);
    }

    private static (Mat Gray, Mat Text) SplashTemplate(Mat crop)
    {
        var (gray, text) = SplashNameForms(crop);
        using (gray)
        using (text)
        {
            var (dx, dy) = ((int)(SplashNameW * SplashTemplateTrim), (int)(SplashNameH * SplashTemplateTrim));
            var inner = new Rect(dx, dy, SplashNameW - 2 * dx, SplashNameH - 2 * dy);
            return (new Mat(gray, inner).Clone(), new Mat(text, inner).Clone());
        }
    }

    /// <summary>How well two name crops match: the better of the gray and text-mask correlations (the first trimmed so the second can shift).</summary>
    public static double SplashNameMatch(Mat sampleCrop, Mat crop)
    {
        var template = SplashTemplate(sampleCrop);
        var forms = SplashNameForms(crop);
        try
        {
            return MatchScore(forms, template);
        }
        finally
        {
            template.Gray.Dispose();
            template.Text.Dispose();
            forms.Gray.Dispose();
            forms.Text.Dispose();
        }
    }

    private static double MatchScore((Mat Gray, Mat Text) forms, (Mat Gray, Mat Text) template)
    {
        static double One(Mat image, Mat template)
        {
            using var result = new Mat();
            Cv2.MatchTemplate(image, template, result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out _, out double max);
            return max;
        }

        return Math.Max(One(forms.Gray, template.Gray), One(forms.Text, template.Text));
    }

    /// <summary>
    /// Whose ultimate a settled splash is, by its name against the user-named samples (R57); null when it matches none
    /// clearly (the ultimate still counts, without a unit).
    /// </summary>
    public (string UnitId, double Score)? NameSplash(Mat frame, Rect content, UltSplash splash)
    {
        if (_splashNames.Count == 0 || splash.Length < SplashSettledMin)
        {
            return null;
        }

        using var crop = new Mat(frame, SplashNameRect(splash, content, frame.Width, frame.Height));
        var forms = SplashNameForms(crop);
        using var gray = forms.Gray;
        using var text = forms.Text;
        var best = _splashNames.GroupBy(s => s.Label)
            .Select(g => (Label: g.Key, Score: g.Max(s => MatchScore(forms, s.Template))))
            .OrderByDescending(s => s.Score).ToList();
        var second = best.Count > 1 ? best[1].Score : double.NegativeInfinity;
        return best[0].Score >= SplashNameScore && best[0].Score - second >= SplashNameMargin ? (best[0].Label, best[0].Score) : null;
    }
}
