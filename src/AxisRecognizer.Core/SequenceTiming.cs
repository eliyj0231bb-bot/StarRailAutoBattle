using System.Globalization;
using System.Text.RegularExpressions;

namespace AxisRecognizer;

/// <summary>
/// The "定时=毫秒" of one line of a target flow (R60), read and written in place for the launcher's timing box.
/// Lines are passed without their line break.
/// </summary>
public static partial class SequenceTiming
{
    /// <summary>True for an extra action written as an ultimate ("3-1 长夜月 终结技"): the only lines that take a timing.</summary>
    public static bool CanTime(string line)
    {
        var tokens = Tokens(line);
        return tokens.Length >= 2 && ExtraIndexRegex().IsMatch(tokens[0]) && tokens.Skip(2).Contains("终结技");
    }

    /// <summary>The milliseconds written on the line, or null when there is none or it can't be read.</summary>
    public static int? Delay(string line)
    {
        foreach (var token in Tokens(line))
        {
            var match = DelayRegex().Match(token);
            if (match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var ms))
            {
                return ms;
            }
        }

        return null;
    }

    /// <summary>
    /// The line with its timing set to <paramref name="ms"/> (null removes it). The timing goes at the end of the
    /// step, before any "@ 条件" and "# 注释"; the rest of the line is kept as written.
    /// </summary>
    public static string WithDelay(string line, int? ms)
    {
        var end = line.IndexOfAny(['@', '＠', '#', '＃']);
        var head = end >= 0 ? line[..end] : line;
        var tail = end >= 0 ? line[end..] : string.Empty;
        head = WrittenDelayRegex().Replace(head, string.Empty);
        var step = head.TrimEnd();
        var gap = head[step.Length..];
        if (ms is { } value)
        {
            step += " 定时=" + value.ToString(CultureInfo.InvariantCulture);
        }

        return tail.Length == 0 ? step : step + (gap.Length > 0 ? gap : " ") + tail;
    }

    /// <summary>
    /// The step a timing on line <paramref name="index"/> counts from: the nearest action line above it, as written
    /// (without condition or comment). Null when there is none in the same segment.
    /// </summary>
    public static string? PreviousAction(IReadOnlyList<string> lines, int index)
    {
        for (var i = index - 1; i >= 0; i--)
        {
            var head = Head(lines[i]);
            if (head.Length == 0)
            {
                continue;
            }

            return head.StartsWith("==", StringComparison.Ordinal) ? null : head;
        }

        return null;
    }

    private static string Head(string line)
    {
        var normalized = ExpectedSequence.Normalize(line);
        var end = normalized.IndexOfAny(['@', '#']);
        return (end >= 0 ? normalized[..end] : normalized).Trim();
    }

    private static string[] Tokens(string line) =>
        Head(line).Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

    [GeneratedRegex(@"^\d+-\d+$")]
    private static partial Regex ExtraIndexRegex();

    [GeneratedRegex(@"^定时=([0-9]+)$")]
    private static partial Regex DelayRegex();

    /// <summary>A written timing with the spaces before it, in either width (R22).</summary>
    [GeneratedRegex(@"[ \t　]*定时[^ \t　]*")]
    private static partial Regex WrittenDelayRegex();
}
