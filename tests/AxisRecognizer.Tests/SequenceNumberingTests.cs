using AxisRecognizer;

namespace AxisRecognizer.Tests;

public class SequenceNumberingTests
{
    private static string Lines(params string[] lines) => string.Join("\r\n", lines);

    /// <summary>The text after one edit and the index changes that follow it.</summary>
    private static string Edit(string before, string after)
    {
        foreach (var edit in SequenceNumbering.Adjust(before, after))
        {
            after = after[..edit.Start] + edit.Text + after[(edit.Start + edit.Length)..];
        }

        return after;
    }

    private static readonly string Example = Lines("2 昔涟", "3 长夜月", "3-1 风堇 终结技", "4 记忆主");

    [Fact]
    public void R54_example_insert_then_delete_restores()
    {
        var typing = Lines("2 昔涟", "3", "3 长夜月", "3-1 风堇 终结技", "4 记忆主");
        var spaced = Lines("2 昔涟", "3 ", "3 长夜月", "3-1 风堇 终结技", "4 记忆主");
        var inserted = Edit(typing, spaced);
        Assert.Equal(Lines("2 昔涟", "3 ", "4 长夜月", "4-1 风堇 终结技", "5 记忆主"), inserted);

        var named = inserted.Replace("3 \r\n", "3 风堇\r\n", StringComparison.Ordinal);
        Assert.Equal(named, Edit(inserted, named));
        Assert.Equal(Example, Edit(named, Lines("2 昔涟", "4 长夜月", "4-1 风堇 终结技", "5 记忆主")));
    }

    [Fact]
    public void R54_insert_extra_shifts_only_the_same_main()
    {
        var before = Lines("3 长夜月", "3-1", "3-1 风堇 终结技", "3-2 昔涟 终结技", "4 记忆主");
        var after = Lines("3 长夜月", "3-1 长夜月 终结技", "3-1 风堇 终结技", "3-2 昔涟 终结技", "4 记忆主");
        Assert.Equal(Lines("3 长夜月", "3-1 长夜月 终结技", "3-2 风堇 终结技", "3-3 昔涟 终结技", "4 记忆主"), Edit(before, after));
    }

    [Fact]
    public void R54_delete_extra_shifts_only_the_same_main()
    {
        var before = Lines("3 长夜月", "3-1 长夜月 终结技", "3-2 风堇 终结技", "4 记忆主");
        Assert.Equal(Lines("3 长夜月", "3-1 风堇 终结技", "4 记忆主"), Edit(before, Lines("3 长夜月", "3-2 风堇 终结技", "4 记忆主")));
    }

    [Fact]
    public void R54_stops_at_the_segment_end()
    {
        var before = Lines("1 昔涟", "2", "2 长夜月", "== 换波 ==", "1 风堇", "2 记忆主");
        var after = Lines("1 昔涟", "2 风堇", "2 长夜月", "== 换波 ==", "1 风堇", "2 记忆主");
        Assert.Equal(Lines("1 昔涟", "2 风堇", "3 长夜月", "== 换波 ==", "1 风堇", "2 记忆主"), Edit(before, after));
    }

    [Fact]
    public void R54_no_change_when_nothing_collides_or_follows()
    {
        // Written in order: the next line is new.
        Assert.Equal(Lines("1 昔涟", "2 长夜月"), Edit(Lines("1 昔涟", "2"), Lines("1 昔涟", "2 长夜月")));
        // Deleting the last line.
        Assert.Equal(Lines("1 昔涟"), Edit(Lines("1 昔涟", "2 长夜月"), Lines("1 昔涟")));
        // A gap after the deleted line is left as it is.
        Assert.Equal(Lines("1 昔涟", "4 风堇"), Edit(Lines("1 昔涟", "2 长夜月", "4 风堇"), Lines("1 昔涟", "4 风堇")));
    }

    [Fact]
    public void R54_editing_an_old_index_does_not_renumber()
    {
        var before = Lines("1 昔涟", "2 长夜月", "3 风堇");
        // Select "2" and type "3".
        Assert.Equal(Lines("1 昔涟", "3 长夜月", "3 风堇"), Edit(before, Lines("1 昔涟", "3 长夜月", "3 风堇")));
        // Backspace over "2", then type "3".
        var cleared = Lines("1 昔涟", " 长夜月", "3 风堇");
        Assert.Equal(cleared, Edit(before, cleared));
        Assert.Equal(Lines("1 昔涟", "3 长夜月", "3 风堇"), Edit(cleared, Lines("1 昔涟", "3 长夜月", "3 风堇")));
    }

    [Fact]
    public void R54_splitting_or_joining_lines_does_not_renumber()
    {
        var before = Lines("1 昔涟", "2 长夜月", "3 风堇");
        Assert.Equal(Lines("1 昔涟", "2 ", "长夜月", "3 风堇"), Edit(before, Lines("1 昔涟", "2 ", "长夜月", "3 风堇")));
        Assert.Equal(Lines("1 昔涟2 长夜月", "3 风堇"), Edit(before, Lines("1 昔涟2 长夜月", "3 风堇")));
        // Enter at the end of a line, and a blank line removed.
        Assert.Equal(Lines("1 昔涟", "", "2 长夜月", "3 风堇"), Edit(before, Lines("1 昔涟", "", "2 长夜月", "3 风堇")));
        Assert.Equal(before, Edit(Lines("1 昔涟", "", "2 长夜月", "3 风堇"), before));
    }

    [Fact]
    public void R54_deleting_a_line_char_by_char_and_retyping_the_space_is_symmetric()
    {
        var full = Lines("1 昔涟", "2 ", "2 长夜月", "3 风堇");
        var bare = Lines("1 昔涟", "2", "3 长夜月", "4 风堇");
        // "2 " → "2" (the action is gone) after an insert had shifted the rest.
        Assert.Equal(Lines("1 昔涟", "2", "2 长夜月", "3 风堇"), Edit(Lines("1 昔涟", "2 ", "3 长夜月", "4 风堇"), bare));
        Assert.Equal(Lines("1 昔涟", "2 ", "3 长夜月", "4 风堇"), Edit(Lines("1 昔涟", "2", "2 长夜月", "3 风堇"), full));
    }

    [Fact]
    public void R54_pasting_or_deleting_several_lines()
    {
        var before = Lines("1 昔涟", "", "2 长夜月", "3 风堇");
        var pasted = Lines("1 昔涟", "2 A", "3 B", "2 长夜月", "3 风堇");
        Assert.Equal(Lines("1 昔涟", "2 A", "3 B", "4 长夜月", "5 风堇"), Edit(before, pasted));

        var all = Lines("1 昔涟", "2 A", "3 B", "4 长夜月", "5 风堇");
        Assert.Equal(Lines("1 昔涟", "2 长夜月", "3 风堇"), Edit(all, Lines("1 昔涟", "4 长夜月", "5 风堇")));
    }

    [Fact]
    public void R54_ime_commit_then_r48_space()
    {
        var committed = Lines("1 昔涟", "2风", "2 长夜月");
        Assert.Equal(committed, Edit(Lines("1 昔涟", "2", "2 长夜月"), committed));
        Assert.Equal(Lines("1 昔涟", "2 风", "3 长夜月"), Edit(committed, Lines("1 昔涟", "2 风", "2 长夜月")));
    }

    [Fact]
    public void R54_only_the_index_changes()
    {
        var before = Lines("1 昔涟", "２", "２ 长夜月 战技 敌(-1)  @ 轮次=1 行动值=120~160  # 注释", "  3-1 风堇 终结技");
        var after = Lines("1 昔涟", "２ 风堇", "２ 长夜月 战技 敌(-1)  @ 轮次=1 行动值=120~160  # 注释", "  3-1 风堇 终结技");
        Assert.Equal(Lines("1 昔涟", "２ 风堇", "3 长夜月 战技 敌(-1)  @ 轮次=1 行动值=120~160  # 注释", "  4-1 风堇 终结技"), Edit(before, after));
    }

    [Fact]
    public void R54_deleting_a_main_leaves_its_extras_to_the_format_check()
    {
        var before = Lines("2 昔涟", "3 长夜月", "3-1 风堇 终结技", "4 记忆主");
        Assert.Equal(Lines("2 昔涟", "3-1 风堇 终结技", "3 记忆主"), Edit(before, Lines("2 昔涟", "3-1 风堇 终结技", "4 记忆主")));
    }
}
