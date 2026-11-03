using AxisRecognizer;

namespace AxisRecognizer.Tests;

public class SequenceTeamTests
{
    private static List<string> Issues(ExpectedSequence sequence) => sequence.Issues.Select(i => i.ToString()).ToList();

    [Fact]
    public void R74_example_team_line_and_ultimate_outside_the_team()
    {
        var sequence = ExpectedSequence.Parse("""
            队伍 1号昔涟 2号风堇
            1 昔涟
            1-1 风堇 终结技
            1-2 长夜月 终结技
            2 长夜月
            """);

        Assert.Equal(["昔涟", "风堇", null, null], sequence.Team);
        Assert.Equal(["第 4 行提醒：长夜月不在队伍站位里，自动操作不知道按几号位。"], Issues(sequence));
        Assert.Equal("队伍 1号昔涟 2号风堇", SequenceTeam.Line(sequence.Team));
    }

    [Fact]
    public void R74_no_team_no_reminder_and_old_files_read()
    {
        var sequence = ExpectedSequence.Parse("1 昔涟\n1-1 长夜月 终结技");
        Assert.Empty(sequence.Issues);
        Assert.All(sequence.Team, Assert.Null);
    }

    [Fact]
    public void R74_team_line_must_come_first_once_and_be_readable()
    {
        var sequence = ExpectedSequence.Parse("""
            # 注释可以在前面
            队伍 1号昔涟 5号风堇 1号长夜月 风堇
            1 昔涟
            队伍 2号风堇
            """);

        Assert.Equal(
        [
            "第 2 行错误：队伍应写成 队伍 1号昔涟 2号风堇 …（号位 1–4）：5号风堇",
            "第 2 行错误：1号位写了两次。",
            "第 2 行错误：队伍应写成 队伍 1号昔涟 2号风堇 …（号位 1–4）：风堇",
            "第 4 行错误：队伍站位要写在流程开头（第一个非注释行），只写一次。",
        ], Issues(sequence));
        Assert.Equal("昔涟", sequence.Team[0]);
        Assert.Equal(["第 1 行错误：队伍后面没有写站位，应写成 队伍 1号昔涟 2号风堇 …"],
            Issues(ExpectedSequence.Parse("队伍\n1 昔涟")));
    }

    [Fact]
    public void R74_team_from_the_boxes_is_checked_on_line_0()
    {
        var lookup = (string name) => name switch
        {
            "昔涟" or "U0001" => UnitLookup.Found("U0001"),
            "风堇" => UnitLookup.Found("U0002"),
            _ => UnitLookup.Failed($"知识库里没有叫“{name}”的单位，请写名称或 U 编号。"),
        };

        var sequence = ExpectedSequence.Parse("1 昔涟\n1-1 U0001 终结技\n1-2 风堇 终结技", lookup, ["昔涟", "  ", "风 堇", "U0001"]);
        Assert.Equal(
        [
            "队伍站位错误：3号位的名称里不能有空格：风 堇",
            "队伍站位错误：“U0001”同时在 1 号位和 4 号位。",
            "第 3 行提醒：风堇不在队伍站位里，自动操作不知道按几号位。",
        ], Issues(sequence));

        Assert.Equal("第 1 行错误：队伍站位在上面的 4 个格子里填，这里不用写。",
            Issues(ExpectedSequence.Parse("队伍 1号昔涟\n1 昔涟", lookup, ["昔涟", null, null, null]))[0]);
        Assert.Equal("队伍站位错误：2号位：知识库里没有叫“无名”的单位，请写名称或 U 编号。",
            Issues(ExpectedSequence.Parse("1 昔涟", lookup, [null, "无名", null, null]))[0]);
    }

    [Fact]
    public void R74_split_and_join_round_trip()
    {
        var file = "队伍 1号昔涟 3号长夜月\r\n# 第1波\r\n1 昔涟\r\n";
        var (slots, body) = SequenceTeam.Split(file);
        Assert.Equal(["昔涟", null, "长夜月", null], (IEnumerable<string?>)slots);
        Assert.Equal("# 第1波\r\n1 昔涟\r\n", body);
        Assert.Equal(file, SequenceTeam.Join(slots, body).Replace(Environment.NewLine, "\r\n"));

        // Full width, after comments.
        Assert.Equal(["昔涟", null, null, null], (IEnumerable<string?>)SequenceTeam.Split("# 注\n队伍　１号昔涟\n1 昔涟").Slots);
        Assert.Equal("# 注\n1 昔涟", SequenceTeam.Split("# 注\n队伍　１号昔涟\n1 昔涟").Body);

        // No team, or an unreadable one: the text stays whole.
        Assert.Equal("1 昔涟", SequenceTeam.Split("1 昔涟").Body);
        Assert.Equal("队伍 9号昔涟\n1 昔涟", SequenceTeam.Split("队伍 9号昔涟\n1 昔涟").Body);
        Assert.Equal("1 昔涟", SequenceTeam.Join([null, " ", null, null], "1 昔涟"));
    }
}
