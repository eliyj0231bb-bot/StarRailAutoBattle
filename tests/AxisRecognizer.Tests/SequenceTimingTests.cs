using AxisRecognizer;

namespace AxisRecognizer.Tests;

public class SequenceTimingTests
{
    [Theory]
    [InlineData("3-1 长夜月 终结技", true)]
    [InlineData("  3-2 风堇 我1 终结技 定时=300  @ 行动值=100  # 注", true)]
    [InlineData("３－１ 长夜月 终结技", true)]
    [InlineData("3-1 长夜月", false)]
    [InlineData("3 长夜月 终结技", false)]
    [InlineData("3-1 终结技", false)]
    [InlineData("# 3-1 长夜月 终结技", false)]
    [InlineData("== 换波 ==", false)]
    public void R60_only_extra_ultimates_take_a_timing(string line, bool expected)
    {
        Assert.Equal(expected, SequenceTiming.CanTime(line));
    }

    [Theory]
    [InlineData("3-1 长夜月 终结技 定时=1250", 1250)]
    [InlineData("3-1 长夜月 定时＝３００ 终结技", 300)]
    [InlineData("3-1 长夜月 终结技 定时=1.5", null)]
    [InlineData("3-1 长夜月 终结技  # 定时=1250", null)]
    public void R60_reads_the_written_timing(string line, int? expected)
    {
        Assert.Equal(expected, SequenceTiming.Delay(line));
    }

    [Theory]
    [InlineData("3-1 长夜月 终结技", 1250, "3-1 长夜月 终结技 定时=1250")]
    [InlineData("3-1 长夜月 终结技 定时=1250", 80, "3-1 长夜月 终结技 定时=80")]
    [InlineData("3-1 长夜月 定时＝３００ 终结技 敌2", 0, "3-1 长夜月 终结技 敌2 定时=0")]
    [InlineData("3-1 长夜月 终结技 定时=1250", null, "3-1 长夜月 终结技")]
    [InlineData("3-1 长夜月 终结技      @ 行动值=100  # 注", 40, "3-1 长夜月 终结技 定时=40      @ 行动值=100  # 注")]
    [InlineData("3-1 长夜月 终结技@ 行动值=100", 40, "3-1 长夜月 终结技 定时=40 @ 行动值=100")]
    [InlineData("3-1 长夜月 终结技 定时=1250  # 注", null, "3-1 长夜月 终结技  # 注")]
    public void R60_writes_the_timing_in_place(string line, int? ms, string expected)
    {
        Assert.Equal(expected, SequenceTiming.WithDelay(line, ms));
        var parsed = ExpectedSequence.Parse("3 记忆主\n" + expected);
        Assert.Equal(ms, parsed.Steps[^1].DelayMs);
    }

    [Fact]
    public void R60_timing_counts_from_the_previous_action_of_the_segment()
    {
        string[] lines = ["1 昔涟 战技  @ 轮次=1", "", "# 注释", "1-1 长夜月 终结技", "== 换波 ==", "0-1 风堇 终结技"];
        Assert.Equal("1 昔涟 战技", SequenceTiming.PreviousAction(lines, 3));
        Assert.Null(SequenceTiming.PreviousAction(lines, 5));
        Assert.Null(SequenceTiming.PreviousAction(lines, 0));
    }
}
