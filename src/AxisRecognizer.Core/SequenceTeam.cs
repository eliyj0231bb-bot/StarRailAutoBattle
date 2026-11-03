using System.Globalization;
using System.Text.RegularExpressions;

namespace AxisRecognizer;

/// <summary>
/// The team slots of a target flow (R74): "队伍 1号昔涟 2号风堇 3号长夜月 4号记忆主" as the first line of the file.
/// Slot n is the n-th avatar of the team bar from the left, the slot srctl presses for an ultimate (R62).
/// </summary>
public static partial class SequenceTeam
{
    public const int Slots = 4;

    /// <summary>The team line for these slots (index 0 = 1号), or null when all are empty.</summary>
    public static string? Line(IReadOnlyList<string?> slots)
    {
        var parts = slots.Take(Slots)
            .Select((name, i) => string.IsNullOrWhiteSpace(name) ? null : $"{i + 1}号{name.Trim()}")
            .OfType<string>()
            .ToList();
        return parts.Count == 0 ? null : "队伍 " + string.Join(' ', parts);
    }

    /// <summary>
    /// Takes a readable team line off the top of a flow file: the slots, and the rest of the text as it was.
    /// A missing or unreadable team line leaves the text whole (the parser then reports what is wrong).
    /// </summary>
    public static (string?[] Slots, string Body) Split(string text)
    {
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = Content(lines[i]);
            if (line.Length == 0)
            {
                continue;
            }

            var ok = true;
            var slots = IsTeamLine(line) ? ParseLine(line, _ => ok = false) : null;
            if (slots is null || !ok)
            {
                break;
            }

            return (slots, string.Join('\n', lines.Take(i).Concat(lines.Skip(i + 1))));
        }

        return (new string?[Slots], text);
    }

    /// <summary>The flow file text: the team line (if any slot is filled) on top of <paramref name="body"/>.</summary>
    public static string Join(IReadOnlyList<string?> slots, string body) =>
        Line(slots) is { } line ? line + Environment.NewLine + body : body;

    /// <summary>A line (normalized, comment removed, trimmed) that sets the team.</summary>
    internal static bool IsTeamLine(string line) => line.StartsWith("队伍", StringComparison.Ordinal);

    /// <summary>The slots of a team line; problems go to <paramref name="error"/>. Null when nothing could be read.</summary>
    internal static string?[]? ParseLine(string line, Action<string> error)
    {
        var tokens = line[2..].Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            error("队伍后面没有写站位，应写成 队伍 1号昔涟 2号风堇 …");
            return null;
        }

        var slots = new string?[Slots];
        foreach (var token in tokens)
        {
            var match = SlotRegex().Match(token);
            if (!match.Success)
            {
                error($"队伍应写成 队伍 1号昔涟 2号风堇 …（号位 1–4）：{token}");
                continue;
            }

            var slot = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            if (slots[slot - 1] is not null)
            {
                error($"{slot}号位写了两次。");
                continue;
            }

            slots[slot - 1] = match.Groups[2].Value;
        }

        return slots;
    }

    private static string Content(string line)
    {
        var normalized = ExpectedSequence.Normalize(line);
        var hash = normalized.IndexOf('#');
        return (hash >= 0 ? normalized[..hash] : normalized).Trim();
    }

    [GeneratedRegex(@"^([1-4])号(\S+)$")]
    private static partial Regex SlotRegex();
}
