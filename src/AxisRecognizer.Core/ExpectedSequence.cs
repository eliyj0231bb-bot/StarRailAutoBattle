using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AxisRecognizer;

public enum StepKind
{
    /// <summary>A main-axis action "n".</summary>
    Main,
    /// <summary>An extra action "n-i" (ultimates) under main action n; "0-i" before the segment's first main action.</summary>
    Extra,
    /// <summary>"== 换波 ==": a new wave (混沌回忆's second half counts as one). Numbering restarts at 1.</summary>
    WaveChange,
    /// <summary>"== 转阶段 ==": a boss phase change. Numbering restarts at 1.</summary>
    PhaseChange,
}

public enum SkillKind
{
    Basic,
    Skill,
    Ultimate,
}

/// <summary>
/// The main target: "敌2" (counted from the left), "敌(-1)" (relative to the boss, 敌(0)), "我3" (ally, from the left).
/// Left out = the boss (敌(0)), or the game's default target when there is no boss. Target reticles can't be read
/// reliably yet, so sequences taken from videos leave it out.
/// </summary>
public sealed record TargetRef(bool Enemy, bool BossRelative, int Number)
{
    public override string ToString() =>
        (Enemy ? "敌" : "我") +(BossRelative ? $"({(Number > 0 ? "+" : "")}{Number})" : Number.ToString(CultureInfo.InvariantCulture));
}

public sealed record ValueRange(int Min, int Max)
{
    public bool Contains(int value) => value >= Min && value <= Max;

    public override string ToString() => Min == Max ? $"{Min}" : $"{Min}~{Max}";
}

/// <summary>The "@ 轮次=… 行动值=…" part of a line: checked when the step is reached in play.</summary>
public sealed record StepCheck(ValueRange? Round, ValueRange? ActionValue)
{
    public override string ToString()
    {
        var parts = new List<string>();
        if (Round is not null)
        {
            parts.Add($"轮次={Round}");
        }

        if (ActionValue is not null)
        {
            parts.Add($"行动值={ActionValue}");
        }

        return "@ " + string.Join(' ', parts);
    }
}

/// <summary>
/// One line of an expected sequence. <see cref="Skill"/> and <see cref="Target"/> are notes taken from the strategy
/// video (editable by the user); they are not part of the pass/fail check. <see cref="DelayMs"/> ("定时=1250") is
/// for the auto-play code only: release this ultimate that many milliseconds after the previous line's action
/// starts (R60).
/// </summary>
public sealed record SequenceStep(
    int Line,
    int Segment,
    StepKind Kind,
    int Main,
    int Extra,
    string? UnitText,
    string? UnitId,
    SkillKind? Skill,
    TargetRef? Target,
    StepCheck? Check,
    int? DelayMs = null)
{
    /// <summary>"3", "3-1", "换波", "转阶段".</summary>
    public string Index => Kind switch
    {
        StepKind.Main => Main.ToString(CultureInfo.InvariantCulture),
        StepKind.Extra => $"{Main}-{Extra}",
        StepKind.WaveChange => "换波",
        _ => "转阶段",
    };

    public bool IsSegmentChange => Kind is StepKind.WaveChange or StepKind.PhaseChange;

    /// <summary>The line as it is written in the sequence text.</summary>
    public string ToText()
    {
        var sb = new StringBuilder();
        if (IsSegmentChange)
        {
            sb.Append($"== {Index} ==");
        }
        else
        {
            sb.Append(Index).Append(' ').Append(UnitText);
            if (Skill is { } skill)
            {
                sb.Append(' ').Append(ExpectedSequence.SkillText(skill));
            }

            if (Target is not null)
            {
                sb.Append(' ').Append(Target);
            }

            if (DelayMs is { } delay)
            {
                sb.Append(" 定时=").Append(delay.ToString(CultureInfo.InvariantCulture));
            }
        }

        if (Check is not null)
        {
            sb.Append("  ").Append(Check);
        }

        return sb.ToString();
    }
}

public enum IssueLevel
{
    /// <summary>The sequence cannot be used for checking until this is fixed.</summary>
    Error,
    /// <summary>Allowed, but probably not what was meant.</summary>
    Warning,
}

/// <summary>A problem on a line; line 0 is the team slots passed in from outside the text (R74).</summary>
public sealed record SequenceIssue(int Line, IssueLevel Level, string Message)
{
    public override string ToString() =>
        $"{(Line == 0 ? "队伍站位" : $"第 {Line} 行")}{(Level == IssueLevel.Error ? "错误" : "提醒")}：{Message}";
}

/// <summary>Result of looking up a unit written in a sequence line: its global id, or why it can't be used.</summary>
public sealed record UnitLookup(string? UnitId, string? Error)
{
    public static UnitLookup Found(string id) => new(id, null);

    public static UnitLookup Failed(string error) => new(null, error);
}

/// <summary>
/// The expected action sequence of one whole battle, used by the 凹分 check. Plain text, one step per line:
/// <code>
/// # 第1波
/// 1 长夜月
/// 2 昔涟 战技 敌2      @ 轮次=1 行动值=120~160
/// 2-1 长夜月 终结技
/// == 换波 ==
/// 1 U0005 普攻 敌(0)
/// == 转阶段 ==  @ 行动值=50~100
/// </code>
/// Enemy actions are not written. Skill and target may be left out (the target then is the boss, or the game's
/// default without one). Text
/// after # is a comment; full-width （）＝～＠ are accepted, and an older "→敌2" still reads as "敌2".
/// </summary>
public sealed partial class ExpectedSequence
{
    public required IReadOnlyList<SequenceStep> Steps { get; init; }
    public required IReadOnlyList<SequenceIssue> Issues { get; init; }

    /// <summary>Team slots 1–4 as written (index 0 = 1号); null where empty (R74).</summary>
    public IReadOnlyList<string?> Team { get; init; } = new string?[SequenceTeam.Slots];

    public bool IsValid => Issues.All(i => i.Level != IssueLevel.Error);

    public IEnumerable<SequenceStep> Actions => Steps.Where(s => !s.IsSegmentChange);

    public static string SkillText(SkillKind skill) => skill switch
    {
        SkillKind.Basic => "普攻",
        SkillKind.Skill => "战技",
        _ => "终结技",
    };

    /// <summary>Text for a sequence file, one step per line.</summary>
    public static string Format(IEnumerable<SequenceStep> steps) =>
        string.Join(Environment.NewLine, steps.Select(s => s.ToText())) + Environment.NewLine;

    /// <summary>
    /// Parses and checks the text. It never throws on bad input: every problem is an issue with its line number.
    /// Without <paramref name="lookupUnit"/>, unit names are kept as written and not checked. The team comes from a
    /// "队伍 1号…" line at the top of the text, or from <paramref name="team"/> when the slots are edited apart from
    /// the text (the launcher's boxes); problems with those are issues on line 0 (R74).
    /// </summary>
    public static ExpectedSequence Parse(string text, Func<string, UnitLookup>? lookupUnit = null, IReadOnlyList<string?>? team = null)
    {
        var steps = new List<SequenceStep>();
        var issues = new List<SequenceIssue>();
        var segment = 1;
        var lastMain = 0;
        var lastExtra = 0;
        var actionsInSegment = 0;
        var lineNo = 0;

        var teamSlots = new string?[SequenceTeam.Slots];
        var teamIds = new string?[SequenceTeam.Slots];
        var contentSeen = false;
        void SetTeam(IReadOnlyList<string?> slots, Action<string> error)
        {
            for (var i = 0; i < SequenceTeam.Slots && i < slots.Count; i++)
            {
                var name = slots[i]?.Trim();
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                if (name.Any(char.IsWhiteSpace))
                {
                    error($"{i + 1}号位的名称里不能有空格：{name}");
                    continue;
                }

                string? id = null;
                if (lookupUnit is not null)
                {
                    var lookup = lookupUnit(name);
                    if (lookup.Error is not null)
                    {
                        error($"{i + 1}号位：{lookup.Error}");
                    }

                    id = lookup.UnitId;
                }

                var same = Enumerable.Range(0, i)
                    .FirstOrDefault(j => teamSlots[j] == name || (id is not null && teamIds[j] == id), -1);
                if (same >= 0)
                {
                    error($"“{name}”同时在 {same + 1} 号位和 {i + 1} 号位。");
                }

                teamSlots[i] = name;
                teamIds[i] = id;
            }
        }

        bool InTeam(string unitText, string? unitId) =>
            Enumerable.Range(0, SequenceTeam.Slots).Any(i => teamSlots[i] == unitText || (unitId is not null && teamIds[i] == unitId));

        if (team is not null)
        {
            SetTeam(team, message => issues.Add(new SequenceIssue(0, IssueLevel.Error, message)));
        }

        foreach (var rawLine in text.Split('\n'))
        {
            lineNo++;
            var line = Normalize(rawLine);
            var hash = line.IndexOf('#');
            if (hash >= 0)
            {
                line = line[..hash];
            }

            line = line.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            void Error(string message) => issues.Add(new SequenceIssue(lineNo, IssueLevel.Error, message));
            void Warn(string message) => issues.Add(new SequenceIssue(lineNo, IssueLevel.Warning, message));

            // R74: "队伍 1号昔涟 2号风堇 …" as the first line.
            if (SequenceTeam.IsTeamLine(line))
            {
                if (team is not null)
                {
                    Error("队伍站位在上面的 4 个格子里填，这里不用写。");
                }
                else if (contentSeen)
                {
                    Error("队伍站位要写在流程开头（第一个非注释行），只写一次。");
                }
                else if (SequenceTeam.ParseLine(line, Error) is { } slots)
                {
                    SetTeam(slots, Error);
                }

                contentSeen = true;
                continue;
            }

            contentSeen = true;

            var at = line.IndexOf('@');
            var head = (at >= 0 ? line[..at] : line).Trim();
            var check = at >= 0 ? ParseCheck(line[(at + 1)..], Error) : null;

            if (head.StartsWith("==", StringComparison.Ordinal))
            {
                var marker = MarkerRegex().Match(head);
                if (!marker.Success)
                {
                    Error("特殊行动应写成 == 换波 == 或 == 转阶段 ==。");
                    continue;
                }

                if (actionsInSegment == 0)
                {
                    Warn(segment == 1 ? "战斗开始后还没有行动就换段了。" : "上一段没有任何行动。");
                }

                var kind = marker.Groups[1].Value == "换波" ? StepKind.WaveChange : StepKind.PhaseChange;
                steps.Add(new SequenceStep(lineNo, segment, kind, 0, 0, null, null, null, null, check));
                segment++;
                lastMain = 0;
                lastExtra = 0;
                actionsInSegment = 0;
                continue;
            }

            var tokens = head.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            var index = IndexRegex().Match(tokens[0]);
            if (!index.Success)
            {
                Error($"行首应为序号（如 3 或 3-1）：{tokens[0]}");
                continue;
            }

            if (tokens.Length < 2)
            {
                Error("序号后面要写单位（名称或 U 编号）。");
                continue;
            }

            var main = int.Parse(index.Groups[1].Value, CultureInfo.InvariantCulture);
            var isExtra = index.Groups[2].Success;
            var extra = isExtra ? int.Parse(index.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
            if (isExtra)
            {
                if (main != lastMain)
                {
                    Error($"额外行动要挂在本段当前的主轴行动 {lastMain} 下，应写成 {lastMain}-{lastExtra + 1}。");
                }
                else if (extra != lastExtra + 1)
                {
                    Error($"序号应为 {lastMain}-{lastExtra + 1}。");
                }

                lastExtra = extra;
            }
            else
            {
                if (main != lastMain + 1)
                {
                    Error(lastMain == 0 && segment > 1
                        ? $"换段后序号从 1 重新开始，这里应为 1。"
                        : $"序号应为 {lastMain + 1}。");
                }

                lastMain = main;
                lastExtra = 0;
            }

            var unitText = tokens[1];
            string? unitId = null;
            if (lookupUnit is not null)
            {
                var lookup = lookupUnit(unitText);
                if (lookup.Error is not null)
                {
                    Error(lookup.Error);
                }

                unitId = lookup.UnitId;
            }

            SkillKind? skill = null;
            TargetRef? target = null;
            int? delay = null;
            var delayWritten = false;
            foreach (var token in tokens.Skip(2))
            {
                if (token.StartsWith("定时", StringComparison.Ordinal))
                {
                    if (delayWritten)
                    {
                        Error("一行只能写一个定时。");
                    }

                    delayWritten = true;
                    var match = DelayRegex().Match(token);
                    if (!match.Success)
                    {
                        Error($"定时应写成 定时=毫秒数（如 定时=1250）：{token}");
                    }
                    else if (!int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var ms))
                    {
                        Error($"定时的毫秒数太大：{match.Groups[1].Value}");
                    }
                    else
                    {
                        delay = ms;
                    }

                    continue;
                }

                if (TryParseSkill(token, out var s))
                {
                    if (skill is not null)
                    {
                        Error("一行只能写一个招式。");
                    }

                    skill = s;
                }
                else if (token.TrimStart('→') is ['敌' or '我', ..])
                {
                    if (target is not null)
                    {
                        Error("一行只能写一个目标（主目标）。");
                    }

                    target = ParseTarget(token, Error);
                }
                else
                {
                    Error($"看不懂“{token}”：单位后面只能写招式（普攻、战技、终结技）、目标（如 敌2、敌(-1)、我1）和定时（如 定时=1250）。");
                }
            }

            // R60: only an ultimate written as an extra action, timed from the previous action of this segment.
            if (delayWritten && (!isExtra || skill != SkillKind.Ultimate))
            {
                Error("定时只能写在额外行动的终结技行（如 3-1 长夜月 终结技 定时=1250）。");
            }
            else if (delayWritten && (steps.Count == 0 || steps[^1].IsSegmentChange || steps[^1].Segment != segment))
            {
                Error("前面没有本段的行动，无法定时：定时从前一行的行动开始计。");
            }

            if (skill == SkillKind.Ultimate && !isExtra)
            {
                Warn("终结技一般写成额外行动（n-i），这里写成了主轴行动。");
            }
            else if (isExtra && skill is SkillKind.Basic or SkillKind.Skill)
            {
                Warn("额外行动一般是终结技，这里写成了普攻或战技。");
            }

            // R74: the auto-play code presses the ultimate by team slot.
            if (isExtra && skill == SkillKind.Ultimate && teamSlots.Any(s => s is not null) && !InTeam(unitText, unitId))
            {
                Warn($"{unitText}不在队伍站位里，自动操作不知道按几号位。");
            }

            steps.Add(new SequenceStep(lineNo, segment, isExtra ? StepKind.Extra : StepKind.Main, main, extra,
                unitText, unitId, skill, target, check, delay));
            actionsInSegment++;
        }

        if (steps.Count == 0)
        {
            issues.Add(new SequenceIssue(Math.Max(lineNo, 1), IssueLevel.Error, "流程里没有任何行动。"));
        }
        else if (actionsInSegment == 0)
        {
            issues.Add(new SequenceIssue(steps[^1].Line, IssueLevel.Warning, "最后一段没有任何行动。"));
        }

        return new ExpectedSequence { Steps = steps, Issues = issues, Team = teamSlots };
    }

    /// <summary>
    /// Looks units up in the knowledge base: a global id or old id (merges followed), or a unit name. Enemy units,
    /// markers and attachments are rejected because they are not written in the sequence.
    /// </summary>
    public static Func<string, UnitLookup> KbLookup(KnowledgeBase kb) => text =>
    {
        UnitRecord? unit;
        if (kb.FindUnit(text) is not null)
        {
            unit = kb.ResolveUnit(text);
        }
        else
        {
            var named = kb.Units.Where(u => u.MergedInto is null && string.Equals(u.Name, text, StringComparison.Ordinal)).ToList();
            if (named.Count == 0)
            {
                return UnitLookup.Failed($"知识库里没有叫“{text}”的单位，请写名称或 U 编号。");
            }

            if (named.Count > 1)
            {
                return UnitLookup.Failed($"“{text}”对应多个单位（{string.Join("、", named.Select(u => u.Id))}），请改写 U 编号。");
            }

            unit = named[0];
        }

        return unit.Kind switch
        {
            UnitKind.Monster or UnitKind.MonsterSummon =>
                UnitLookup.Failed($"{unit.Id}（{unit.Name ?? "未命名"}）是敌方单位，敌方行动不写进流程。"),
            UnitKind.Marker or UnitKind.Attachment or UnitKind.Empty =>
                UnitLookup.Failed($"{unit.Id}（{unit.Name ?? "未命名"}）不是行动单位，不写进流程。"),
            _ => UnitLookup.Found(unit.Id),
        };
    };

    private static bool TryParseSkill(string token, out SkillKind skill)
    {
        switch (token)
        {
            case "普攻":
                skill = SkillKind.Basic;
                return true;
            case "战技":
                skill = SkillKind.Skill;
                return true;
            case "终结技":
                skill = SkillKind.Ultimate;
                return true;
            default:
                skill = default;
                return false;
        }
    }

    private static TargetRef? ParseTarget(string token, Action<string> error)
    {
        var match = TargetRegex().Match(token);
        if (!match.Success)
        {
            error($"目标写法不对：{token}（应为 敌2、敌(0)、敌(-1)、我1）");
            return null;
        }

        var enemy = match.Groups[1].Value == "敌";
        if (match.Groups[2].Success)
        {
            var number = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            if (number < 1)
            {
                error($"从左数的目标从 1 开始：{token}");
                return null;
            }

            return new TargetRef(enemy, false, number);
        }

        if (!enemy)
        {
            error($"以 boss 为基准的写法只用于敌方：{token}");
            return null;
        }

        return new TargetRef(true, true, int.Parse(match.Groups[3].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture));
    }

    private static StepCheck? ParseCheck(string text, Action<string> error)
    {
        ValueRange? round = null;
        ValueRange? actionValue = null;
        var any = false;
        foreach (var part in text.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            any = true;
            var match = CheckRegex().Match(part);
            if (!match.Success)
            {
                error($"检定条件写法不对：{part}（应为 轮次=1 或 行动值=120~160）");
                continue;
            }

            var min = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            var max = match.Groups[3].Success ? int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture) : min;
            if (min > max)
            {
                error($"区间下限大于上限：{part}");
                continue;
            }

            var range = new ValueRange(min, max);
            if (match.Groups[1].Value == "轮次")
            {
                if (round is not null)
                {
                    error("轮次写了两次。");
                }

                round = range;
            }
            else
            {
                if (actionValue is not null)
                {
                    error("行动值写了两次。");
                }

                actionValue = range;
            }
        }

        if (!any)
        {
            error("@ 后面没有写检定条件。");
            return null;
        }

        return round is null && actionValue is null ? null : new StepCheck(round, actionValue);
    }

    /// <summary>Full-width punctuation to half-width; "->" to "→" (older target notation).</summary>
    internal static string Normalize(string line)
    {
        var sb = new StringBuilder(line.Length);
        foreach (var c in line.TrimEnd('\r'))
        {
            sb.Append(c switch
            {
                '（' => '(',
                '）' => ')',
                '＝' => '=',
                '～' => '~',
                '＠' => '@',
                '＋' => '+',
                '－' => '-',
                '＃' => '#',
                '　' => ' ',
                >= '０' and <= '９' => (char)('0' + (c - '０')),
                _ => c,
            });
        }

        return sb.ToString().Replace("->", "→", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"^==\s*(换波|转阶段)\s*==$")]
    private static partial Regex MarkerRegex();

    [GeneratedRegex(@"^(\d+)(?:-(\d+))?$")]
    private static partial Regex IndexRegex();

    [GeneratedRegex(@"^→?(敌|我)(?:(\d+)|\(([+-]?\d+)\))$")]
    private static partial Regex TargetRegex();

    [GeneratedRegex(@"^(轮次|行动值)=(\d+)(?:[~-](\d+))?$")]
    private static partial Regex CheckRegex();

    [GeneratedRegex(@"^定时=([0-9]+)$")]
    private static partial Regex DelayRegex();
}
