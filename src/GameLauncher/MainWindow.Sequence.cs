using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AxisRecognizer;

namespace GameLauncher;

/// <summary>The 目标流程 page: write and check the expected sequence of one battle, saved for the auto-play code.</summary>
public partial class MainWindow
{
    private const string NewSequenceItem = "（新流程）";

    private readonly DispatcherTimer _sequenceCheckTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private Func<string, UnitLookup>? _sequenceLookup;
    private string? _sequenceLookupNote;
    private bool _sequenceLookupLoaded;

    /// <summary>Name and text as last loaded or saved; null name = a new sequence not saved yet.</summary>
    private string? _sequenceLoadedName;
    private string _sequenceSavedText = string.Empty;
    private bool _sequencePickerBusy;

    /// <summary>The editor text before the latest change, and whether the change is our own renumbering (R54).</summary>
    private string _sequenceLastText = string.Empty;
    private bool _sequenceRenumbering;
    private string? _sequenceRenumberFrom;
    private bool _sequenceComposing;

    private static string SequenceDataRoot => AxisData.Resolve(AppContext.BaseDirectory);

    private bool SequenceDirty => SequenceFileText != _sequenceSavedText;

    /// <summary>What gets saved: the team line from the slot boxes on top of the editor text (R74).</summary>
    private string SequenceFileText => SequenceTeam.Join(SequenceTeamSlots(), SequenceEditor.Text);

    private TextBox[] SequenceTeamBoxes => [SequenceTeamSlot1, SequenceTeamSlot2, SequenceTeamSlot3, SequenceTeamSlot4];

    private string?[] SequenceTeamSlots() =>
        SequenceTeamBoxes.Select(b => string.IsNullOrWhiteSpace(b.Text) ? null : b.Text.Trim()).ToArray();

    private void SetSequenceTeam(IReadOnlyList<string?> slots)
    {
        var boxes = SequenceTeamBoxes;
        for (var i = 0; i < boxes.Length; i++)
        {
            boxes[i].Text = i < slots.Count ? slots[i] ?? string.Empty : string.Empty;
        }
    }

    private void SequenceTeamSlot_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateSequenceDirty();
        _sequenceCheckTimer.Stop();
        _sequenceCheckTimer.Start();
    }

    private void InitSequencePage()
    {
        _sequenceCheckTimer.Tick += (_, _) =>
        {
            _sequenceCheckTimer.Stop();
            CheckSequence();
        };
        Closing += SequencePage_Closing;
        RefreshSequencePicker(select: null);
        CheckSequence();
    }

    private void SequencePage_Closing(object? sender, CancelEventArgs e)
    {
        if (!e.Cancel && !ConfirmDiscardSequence("关闭程序"))
        {
            e.Cancel = true;
        }
    }

    // ---------- Picking and loading ----------

    private void RefreshSequencePicker(string? select)
    {
        IReadOnlyList<string> names;
        try
        {
            names = SequenceFiles.List(SequenceDataRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            names = [];
            SequenceSaveText.Text = "读取已保存的流程失败：" + ex.Message;
        }

        _sequencePickerBusy = true;
        SequencePicker.Items.Clear();
        SequencePicker.Items.Add(NewSequenceItem);
        foreach (var name in names)
        {
            SequencePicker.Items.Add(name);
        }

        SequencePicker.SelectedItem = select is not null && names.Contains(select) ? select : NewSequenceItem;
        _sequencePickerBusy = false;
    }

    private void SequencePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_sequencePickerBusy || SequencePicker.SelectedItem is not string picked)
        {
            return;
        }

        var name = picked == NewSequenceItem ? null : picked;
        if (name == _sequenceLoadedName)
        {
            return;
        }

        if (!ConfirmDiscardSequence(name is null ? "新建流程" : $"打开“{name}”"))
        {
            // Put the picker back on the sequence still being edited.
            _sequencePickerBusy = true;
            SequencePicker.SelectedItem = _sequenceLoadedName ?? NewSequenceItem;
            _sequencePickerBusy = false;
            return;
        }

        LoadSequence(name);
    }

    private void LoadSequence(string? name)
    {
        var text = string.Empty;
        if (name is not null)
        {
            try
            {
                text = SequenceFiles.Read(SequenceDataRoot, name);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                SequenceSaveText.Text = $"打开“{name}”失败：{ex.Message}";
                RefreshSequencePicker(_sequenceLoadedName);
                return;
            }
        }

        _sequenceLoadedName = name;
        var (team, body) = SequenceTeam.Split(text);
        _sequenceSavedText = SequenceTeam.Join(team, body);
        SequenceNameBox.Text = name ?? string.Empty;
        SetSequenceTeam(team);
        SequenceEditor.Text = body;
        SequenceSaveText.Text = name is null ? "新流程，还没有保存。" : $"已打开“{name}”。";
        UpdateSequenceDirty();
        CheckSequence();
    }

    /// <summary>
    /// Puts a sequence generated from a video into the editor as a new, unsaved sequence, with the team the video
    /// page used in the slot boxes (R74).
    /// </summary>
    private void OpenSequenceDraft(string text, string suggestedName, IReadOnlyList<string?> team)
    {
        SequenceTab.IsSelected = true;
        if (!ConfirmDiscardSequence("填入新生成的流程"))
        {
            SequenceSaveText.Text = "生成的流程没有填入（当前流程有未保存的修改）。草稿已保存在识别结果文件夹的 sequence_draft.txt。";
            return;
        }

        _sequenceLoadedName = null;
        _sequenceSavedText = string.Empty;
        _sequencePickerBusy = true;
        SequencePicker.SelectedItem = NewSequenceItem;
        _sequencePickerBusy = false;
        SequenceNameBox.Text = SequenceFiles.NameError(suggestedName) is null ? suggestedName : string.Empty;
        SetSequenceTeam(team);
        SequenceEditor.Text = text;
        SequenceSaveText.Text = "已填入从视频生成的流程，还没保存。核对后点“保存”。";
        UpdateSequenceDirty();
        CheckSequence();
    }

    /// <summary>True when there are no unsaved changes, or the user chose to save or drop them.</summary>
    private bool ConfirmDiscardSequence(string action)
    {
        if (!SequenceDirty)
        {
            return true;
        }

        var answer = MessageBox.Show(this, $"目标流程有还没保存的修改。{action}之前要先保存吗？\n\n是：保存后继续\n否：丢掉修改后继续\n取消：回去继续编辑",
            "目标流程未保存", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return answer switch
        {
            MessageBoxResult.Yes => SaveSequence(),
            MessageBoxResult.No => true,
            _ => false,
        };
    }

    // ---------- Editing and checking ----------

    private void SequenceEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        var before = _sequenceLastText;
        var after = SequenceEditor.Text;
        _sequenceLastText = after;

        // R54: only the user's own edits renumber; not loading, undo or redo, or the renumbering itself.
        if (!_sequenceRenumbering)
        {
            if (!SequenceEditor.IsKeyboardFocusWithin || e.UndoAction is UndoAction.Undo or UndoAction.Redo or UndoAction.Clear)
            {
                _sequenceRenumberFrom = null;
            }
            else if (_sequenceRenumberFrom is null)
            {
                // Done after the edit settles; an IME composition (pinyin not yet committed) waits for its commit.
                // Edits made before then are taken together, from the text before the first of them.
                _sequenceRenumberFrom = before;
                if (!_sequenceComposing)
                {
                    Dispatcher.BeginInvoke(DispatcherPriority.Input, RenumberSequence);
                }
            }
        }

        UpdateSequenceDirty();
        UpdateSequenceTimingBar();
        _sequenceCheckTimer.Stop();
        _sequenceCheckTimer.Start();
    }

    private void RenumberSequence()
    {
        if (_sequenceRenumberFrom is not { } before)
        {
            return;
        }

        _sequenceRenumberFrom = null;
        var edits = SequenceNumbering.Adjust(before, SequenceEditor.Text);
        if (edits.Count == 0)
        {
            return;
        }

        var caret = SequenceEditor.CaretIndex;
        _sequenceRenumbering = true;
        SequenceEditor.BeginChange();
        foreach (var edit in edits)
        {
            SequenceEditor.Select(edit.Start, edit.Length);
            SequenceEditor.SelectedText = edit.Text;
            if (edit.Start < caret)
            {
                caret += edit.Text.Length - edit.Length;
            }
        }

        SequenceEditor.EndChange();
        SequenceEditor.Select(caret, 0);
        _sequenceRenumbering = false;
    }

    // R48: typed text gets a space after the index; doubled spaces and unused symbols are not added.
    private void SequenceEditor_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (e.TextComposition is FrameworkTextComposition ime)
        {
            // An IME commit is already in the text when this is raised, so fix it up afterwards.
            var offset = ime.ResultOffset;
            var committed = e.Text;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () => FixImeCommit(offset, committed));
            _sequenceComposing = false;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, RenumberSequence);
            return;
        }

        var text = SequenceTyping.Filter(SequenceEditor.Text, SequenceEditor.SelectionStart, SequenceEditor.SelectionLength, e.Text);
        if (text == e.Text)
        {
            return;
        }

        e.Handled = true;
        if (text.Length > 0)
        {
            var start = SequenceEditor.SelectionStart;
            SequenceEditor.SelectedText = text;
            SequenceEditor.Select(start + text.Length, 0);
        }
    }

    private void FixImeCommit(int offset, string committed)
    {
        var current = SequenceEditor.Text;
        if (offset < 0 || offset + committed.Length > current.Length
            || string.CompareOrdinal(current, offset, committed, 0, committed.Length) != 0)
        {
            return;
        }

        var without = current.Remove(offset, committed.Length);
        var text = SequenceTyping.Filter(without, offset, 0, committed);
        if (text != committed)
        {
            SequenceEditor.Select(offset, committed.Length);
            SequenceEditor.SelectedText = text;
            SequenceEditor.Select(offset + text.Length, 0);
        }
    }

    // The space key does not raise PreviewTextInput in a TextBox.
    private void SequenceEditor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // R54: keys the IME takes may change the text before the commit; renumbering waits for the commit.
        _sequenceComposing = e.Key == Key.ImeProcessed;

        if (e.Key == Key.Space && Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Shift
            && SequenceTyping.Filter(SequenceEditor.Text, SequenceEditor.SelectionStart, SequenceEditor.SelectionLength, " ").Length == 0)
        {
            e.Handled = true;
        }
    }

    private void SequenceNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ShowFieldError(SequenceNameError, null);
        UpdateSequenceDirty();
    }

    private void SequenceEditor_SelectionChanged(object sender, RoutedEventArgs e)
    {
        var line = SequenceEditor.GetLineIndexFromCharacterIndex(SequenceEditor.CaretIndex);
        SequenceCaretText.Text = line >= 0 ? $"第 {line + 1} 行" : string.Empty;
        UpdateSequenceTimingBar();
    }

    // ---------- Timing box (R60) ----------

    /// <summary>The editor line the timing box writes to; -1 while the box is hidden.</summary>
    private int _sequenceTimingLine = -1;
    private bool _sequenceTimingSync;

    private void UpdateSequenceTimingBar()
    {
        var lines = SequenceEditor.Text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var index = SequenceEditor.GetLineIndexFromCharacterIndex(SequenceEditor.CaretIndex);
        if (index < 0 || index >= lines.Count || !SequenceTiming.CanTime(lines[index]))
        {
            _sequenceTimingLine = -1;
            SequenceTimingBar.Visibility = Visibility.Collapsed;
            return;
        }

        _sequenceTimingLine = index;
        SequenceTimingBar.Visibility = Visibility.Visible;
        SequenceTimingTitle.Text = $"第 {index + 1} 行终结技";
        if (!SequenceTimingBox.IsKeyboardFocusWithin)
        {
            _sequenceTimingSync = true;
            SequenceTimingBox.Text = SequenceTiming.Delay(lines[index])?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            _sequenceTimingSync = false;
        }

        ShowSequenceTimingNote(lines, index, valid: true);
    }

    private void ShowSequenceTimingNote(IReadOnlyList<string> lines, int index, bool valid)
    {
        var previous = SequenceTiming.PreviousAction(lines, index);
        var warn = !valid || previous is null;
        SequenceTimingText.Foreground = warn ? new SolidColorBrush(StatusColor(StatusKind.Error)) : (Brush)FindResource("MutedBrush");
        SequenceTimingText.Text = !valid
            ? "只能填 0 或正整数（毫秒），这一行还没改。"
            : previous is null
                ? "前面没有本段的行动，这一行不能定时（定时从前一行的行动开始计）。"
                : $"从上一行“{previous}”的行动开始计：主轴行动从回合开始算，终结技从它释放的那一刻算。留空就不定时。只给自动操作用，不参与判定。";
    }

    private void SequenceTimingBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !e.Text.All(char.IsAsciiDigit);
    }

    private void SequenceTimingBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // The space key does not raise PreviewTextInput.
        e.Handled = e.Key == Key.Space;
    }

    private void SequenceTimingBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_sequenceTimingSync || _sequenceTimingLine < 0)
        {
            return;
        }

        var lines = SequenceEditor.Text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var index = _sequenceTimingLine;
        if (index >= lines.Count || !SequenceTiming.CanTime(lines[index]))
        {
            return;
        }

        var text = SequenceTimingBox.Text.Trim();
        int? ms = null;
        if (text.Length > 0)
        {
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            {
                ShowSequenceTimingNote(lines, index, valid: false);
                return;
            }

            ms = value;
        }

        var old = lines[index];
        var updated = SequenceTiming.WithDelay(old, ms);
        if (updated == old)
        {
            ShowSequenceTimingNote(lines, index, valid: true);
            return;
        }

        var start = SequenceEditor.GetCharacterIndexFromLineIndex(index);
        var caret = SequenceEditor.CaretIndex;
        caret = caret > start + old.Length ? caret + updated.Length - old.Length : Math.Min(caret, start + updated.Length);
        SequenceEditor.Select(start, old.Length);
        SequenceEditor.SelectedText = updated;
        SequenceEditor.Select(caret, 0);
    }

    private void UpdateSequenceDirty()
    {
        SequenceReloadButton.IsEnabled = SequenceDirty;
        var name = SequenceNameBox.Text.Trim();
        SequenceSaveButton.Content = _sequenceLoadedName is not null && name.Length > 0 && name != _sequenceLoadedName ? "另存为" : "保存";
    }

    private Func<string, UnitLookup>? SequenceLookup()
    {
        if (_sequenceLookupLoaded)
        {
            return _sequenceLookup;
        }

        _sequenceLookupLoaded = true;
        try
        {
            var kb = KnowledgeBase.Load(AxisData.KbRoot(SequenceDataRoot));
            _sequenceLookup = ExpectedSequence.KbLookup(kb);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or JsonException)
        {
            _sequenceLookupNote = "找不到识别知识库，单位名称没有检查。";
        }

        return _sequenceLookup;
    }

    private ExpectedSequence? CheckSequence()
    {
        _sequenceCheckTimer.Stop();
        SequenceIssueList.Items.Clear();
        if (string.IsNullOrWhiteSpace(SequenceEditor.Text))
        {
            SetSequenceCheck(StatusKind.Idle, "还没有内容", "在上面逐行写目标流程，写法见下方“写法说明”。");
            return null;
        }

        var sequence = ExpectedSequence.Parse(SequenceEditor.Text, SequenceLookup(), SequenceTeamSlots());
        var actions = sequence.Actions.ToList();
        var markers = sequence.Steps.Count - actions.Count;
        var checks = sequence.Steps.Count(s => s.Check is not null);
        var errors = sequence.Issues.Count(i => i.Level == IssueLevel.Error);
        var warnings = sequence.Issues.Count - errors;
        var counts = $"共 {actions.Count} 个行动（主轴 {actions.Count(a => a.Kind == StepKind.Main)}、额外 {actions.Count(a => a.Kind == StepKind.Extra)}），"
            + $"换波或转阶段 {markers} 次，检定 {checks} 处，定时 {sequence.Steps.Count(s => s.DelayMs is not null)} 处。";
        if (_sequenceLookupNote is not null)
        {
            counts += _sequenceLookupNote;
        }

        if (errors > 0)
        {
            SetSequenceCheck(StatusKind.Error, $"有 {errors} 处错误" + (warnings > 0 ? $"、{warnings} 条提醒" : string.Empty),
                "改好错误之前，这份流程不能用于检定。" + counts);
        }
        else if (warnings > 0)
        {
            SetSequenceCheck(StatusKind.Warning, $"格式正确，有 {warnings} 条提醒", counts);
        }
        else
        {
            SetSequenceCheck(StatusKind.Success, "格式正确", counts);
        }

        foreach (var issue in sequence.Issues.OrderBy(i => i.Line).ThenBy(i => i.Level))
        {
            SequenceIssueList.Items.Add(new ListBoxItem
            {
                Content = new TextBlock { Text = issue.ToString(), Foreground = new SolidColorBrush(StatusColor(issue.Level == IssueLevel.Error ? StatusKind.Error : StatusKind.Warning)) },
                Tag = issue.Line,
                Cursor = System.Windows.Input.Cursors.Hand,
            });
        }

        SequenceIssueList.Visibility = SequenceIssueList.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        return sequence;
    }

    private void SetSequenceCheck(StatusKind kind, string title, string text)
    {
        var color = StatusColor(kind);
        SequenceCheckDot.Fill = new SolidColorBrush(color);
        SequenceCheckBorder.BorderBrush = new SolidColorBrush(kind == StatusKind.Idle ? Color.FromRgb(0xD9, 0xDE, 0xE7) : color);
        SequenceCheckTitle.Text = title;
        SequenceCheckText.Text = text;
    }

    private void SequenceIssueList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SequenceIssueList.SelectedItem is not ListBoxItem { Tag: int line })
        {
            return;
        }

        var index = line - 1;
        if (line == 0)
        {
            // R74: a problem with the team slot boxes.
            var boxes = SequenceTeamBoxes;
            var message = SequenceIssueList.SelectedItem is ListBoxItem { Content: TextBlock { Text: var text } } ? text : string.Empty;
            var match = System.Text.RegularExpressions.Regex.Match(message, "([1-4]) ?号位");
            var slot = match.Success ? match.Groups[1].Value[0] - '1' : 0;
            boxes[slot].Focus();
            boxes[slot].SelectAll();
        }
        else if (index >= 0 && index < SequenceEditor.LineCount)
        {
            var start = SequenceEditor.GetCharacterIndexFromLineIndex(index);
            SequenceEditor.Focus();
            SequenceEditor.Select(start, SequenceEditor.GetLineLength(index));
            SequenceEditor.ScrollToLine(index);
        }

        // Let the same issue be clicked again.
        SequenceIssueList.SelectedItem = null;
    }

    // ---------- Saving ----------

    private void SequenceSaveButton_Click(object sender, RoutedEventArgs e) => SaveSequence();

    private bool SaveSequence()
    {
        var name = SequenceNameBox.Text.Trim();
        if (SequenceFiles.NameError(name) is { } nameError)
        {
            ShowFieldError(SequenceNameError, nameError);
            SequenceNameBox.Focus();
            return false;
        }

        var sequence = CheckSequence();
        var errors = sequence?.Issues.Count(i => i.Level == IssueLevel.Error) ?? 0;
        if (sequence is null || errors > 0)
        {
            var reason = sequence is null ? "流程还是空的" : $"流程还有 {errors} 处错误";
            if (MessageBox.Show(this, $"{reason}，保存后在改好之前不能用于检定。仍要保存吗？", "保存目标流程",
                    MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            {
                return false;
            }
        }

        var root = SequenceDataRoot;
        try
        {
            if (name != _sequenceLoadedName && SequenceFiles.Exists(root, name)
                && MessageBox.Show(this, $"已经有一份叫“{name}”的流程。要用现在的内容覆盖它吗？", "覆盖流程",
                    MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            {
                return false;
            }

            SequenceFiles.Write(root, name, SequenceFileText);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SequenceSaveText.Text = "保存失败：" + ex.Message;
            return false;
        }

        _sequenceLoadedName = name;
        _sequenceSavedText = SequenceFileText;
        RefreshSequencePicker(name);
        UpdateSequenceDirty();
        SequenceSaveText.Text = $"已保存“{name}”（{DateTime.Now:HH:mm:ss}）" + (errors > 0 || sequence is null ? "，但还不能用于检定。" : "。");
        _storage.Log("sequence", $"保存序列 | 步数={sequence?.Steps.Count ?? 0} | 错误={errors}");
        return true;
    }

    private void SequenceReloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "丢掉还没保存的修改，回到上次保存的内容吗？", "放弃修改",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK)
        {
            var (team, body) = SequenceTeam.Split(_sequenceSavedText);
            SetSequenceTeam(team);
            SequenceEditor.Text = body;
            SequenceNameBox.Text = _sequenceLoadedName ?? string.Empty;
        }
    }

    private void SequenceFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var folder = SequenceFiles.Folder(SequenceDataRoot);
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SequenceSaveText.Text = "无法打开流程文件夹：" + ex.Message;
            return;
        }

        OpenFolder(folder);
    }
}
