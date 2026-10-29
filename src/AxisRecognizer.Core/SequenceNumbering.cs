using System.Globalization;
using System.Text.RegularExpressions;

namespace AxisRecognizer;

/// <summary>A replacement of <see cref="Length"/> characters at <see cref="Start"/>.</summary>
public sealed record TextEdit(int Start, int Length, string Text);

/// <summary>
/// Keeps the indexes of a hand-written expected sequence in order when a line is inserted or deleted (R49).
/// </summary>
public static partial class SequenceNumbering
{
    /// <summary>
    /// The index changes that follow one edit from <paramref name="before"/> to <paramref name="after"/>, as edits of
    /// <paramref name="after"/> ordered from the end, so they can be applied one by one. Empty when nothing changes.
    /// </summary>
    public static IReadOnlyList<TextEdit> Adjust(string before, string after)
    {
        var oldLines = before.Split('\n');
        var newLines = after.Split('\n');
        var prefix = 0;
        while (prefix < oldLines.Length && prefix < newLines.Length && oldLines[prefix] == newLines[prefix])
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < oldLines.Length - prefix && suffix < newLines.Length - prefix
            && oldLines[^(suffix + 1)] == newLines[^(suffix + 1)])
        {
            suffix++;
        }

        var removed = oldLines[prefix..^suffix];
        var added = newLines[prefix..^suffix];
        if (removed.Concat(added).Any(IsMarker))
        {
            return [];
        }

        var removedKeys = removed.Select(Parse).OfType<LineIndex>().ToList();
        var addedKeys = added.Select(Parse).OfType<LineIndex>().ToList();
        var lines = (string[])newLines.Clone();
        var from = prefix + added.Length;

        // R49: a line becomes an action where there was none, or an action line is gone without a new one.
        // Anything else (editing an old line's index, splitting or joining lines) is left to the format check.
        if (removedKeys.Count == 0 && addedKeys.Count > 0 && removed.All(IsBare))
        {
            foreach (var key in addedKeys)
            {
                Insert(lines, from, key);
            }
        }
        else if (addedKeys.Count == 0 && removedKeys.Count > 0 && added.All(IsBare))
        {
            // Last first, so each deleted index still has its next one right after it.
            for (var i = removedKeys.Count - 1; i >= 0; i--)
            {
                Delete(lines, from, removedKeys[i]);
            }
        }

        var edits = new List<TextEdit>();
        var start = after.Length;
        for (var i = newLines.Length - 1; i >= 0; i--)
        {
            start -= newLines[i].Length;
            if (lines[i] != newLines[i] && Parse(newLines[i]) is { } index)
            {
                edits.Add(new TextEdit(start + index.Column, index.Length, Parse(lines[i])!.Text));
            }

            start--;
        }

        return edits;
    }

    private static void Insert(string[] lines, int from, LineIndex key)
    {
        if (!Later(lines, from).Any(i => key.Extra is null ? i.index.Main == key.Main : i.index.Main == key.Main && i.index.Extra == key.Extra))
        {
            return;
        }

        foreach (var (line, index) in Later(lines, from).ToList())
        {
            if (key.Extra is null && index.Main >= key.Main)
            {
                lines[line] = index.With(index.Main + 1, index.Extra);
            }
            else if (key.Extra is { } extra && index.Main == key.Main && index.Extra >= extra)
            {
                lines[line] = index.With(index.Main, index.Extra + 1);
            }
        }
    }

    private static void Delete(string[] lines, int from, LineIndex key)
    {
        if (!Later(lines, from).Any(i => key.Extra is null ? i.index.Main == key.Main + 1 && i.index.Extra is null : i.index.Main == key.Main && i.index.Extra == key.Extra + 1))
        {
            return;
        }

        foreach (var (line, index) in Later(lines, from).ToList())
        {
            if (key.Extra is null && index.Main > key.Main)
            {
                lines[line] = index.With(index.Main - 1, index.Extra);
            }
            else if (key.Extra is { } extra && index.Main == key.Main && index.Extra > extra)
            {
                lines[line] = index.With(index.Main, index.Extra - 1);
            }
        }
    }

    /// <summary>Action lines from <paramref name="from"/> to the end of the segment (R13).</summary>
    private static IEnumerable<(int line, LineIndex index)> Later(string[] lines, int from)
    {
        for (var i = from; i < lines.Length && !IsMarker(lines[i]); i++)
        {
            if (Parse(lines[i]) is { } index)
            {
                yield return (i, index);
            }
        }
    }

    private static bool IsMarker(string line) => line.TrimStart().StartsWith("==", StringComparison.Ordinal);

    /// <summary>
    /// Blank, or an index not yet followed by a space ("3", "3-", and "3昔" for the moment an IME commit is in
    /// before R48 adds the space).
    /// </summary>
    private static bool IsBare(string line) => Parse(line) is null && BareRegex().IsMatch(line);

    /// <summary>The index of an action line: one followed by a space (R16).</summary>
    private static LineIndex? Parse(string line)
    {
        var match = IndexRegex().Match(line);
        if (!match.Success)
        {
            return null;
        }

        var main = Number(match.Groups[2].Value);
        int? extra = match.Groups[3].Success ? Number(match.Groups[3].Value) : null;
        return new LineIndex(line, match.Groups[1].Index, match.Groups[1].Length, main, extra);
    }

    /// <summary>Half- or full-width digits (R22).</summary>
    private static int Number(string digits) =>
        int.Parse(string.Concat(digits.Select(c => c is >= '０' and <= '９' ? (char)('0' + (c - '０')) : c)), CultureInfo.InvariantCulture);

    private sealed record LineIndex(string Line, int Column, int Length, int Main, int? Extra)
    {
        public string Text => Extra is { } extra ? $"{Main}-{extra}" : Main.ToString(CultureInfo.InvariantCulture);

        public string With(int main, int? extra) =>
            Line[..Column] + (this with { Main = main, Extra = extra }).Text + Line[(Column + Length)..];
    }

    [GeneratedRegex(@"^\s*(([0-9０-９]+)(?:[-－]([0-9０-９]+))?)[ \t　]")]
    private static partial Regex IndexRegex();

    [GeneratedRegex(@"^\s*(?:[0-9０-９].*)?$")]
    private static partial Regex BareRegex();
}
