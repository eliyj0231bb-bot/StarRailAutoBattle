using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AxisRecognizer;
using GameControl;
using GameLauncher.Core;

namespace GameLauncher;

/// <summary>
/// The 设置 page (R75): srctl's keys, key timing and ult timing in control-keys.json (shared with srctl), the
/// retry pause in the launcher's settings.json, and the data folders.
/// </summary>
public partial class MainWindow
{
    private readonly KeyMapStorage _keyStorage = new();
    private bool _settingsLoading;

    /// <summary>Operation names of <see cref="KeyMap.Names"/> as the page shows them.</summary>
    private static readonly Dictionary<string, string> KeyLabels = new()
    {
        ["basic"] = "普攻", ["skill"] = "战技",
        ["ult1"] = "终结技 1号", ["ult2"] = "终结技 2号", ["ult3"] = "终结技 3号", ["ult4"] = "终结技 4号",
        ["confirm"] = "确认释放", ["left"] = "目标左移", ["right"] = "目标右移", ["menu"] = "菜单",
    };

    private Dictionary<string, TextBox> SettingsKeyBoxes => new()
    {
        ["basic"] = SettingsKeyBasic, ["skill"] = SettingsKeySkill,
        ["ult1"] = SettingsKeyUlt1, ["ult2"] = SettingsKeyUlt2, ["ult3"] = SettingsKeyUlt3, ["ult4"] = SettingsKeyUlt4,
        ["confirm"] = SettingsKeyConfirm, ["left"] = SettingsKeyLeft, ["right"] = SettingsKeyRight, ["menu"] = SettingsKeyMenu,
    };

    private TextBox[] SettingsAnimBoxes => [SettingsAnim1, SettingsAnim2, SettingsAnim3, SettingsAnim4];

    private void InitSettingsPage()
    {
        foreach (var box in SettingsKeyBoxes.Values.Concat(SettingsAnimBoxes)
                     .Concat([SettingsHold, SettingsTwice, SettingsSplash, SettingsAnim, SettingsPauseAfter,
                         SettingsAllowedPending, SettingsPauseAfterPending]))
        {
            box.TextChanged += (_, _) => SettingsField_Changed(box, new RoutedEventArgs());
        }

        SettingsTab.GotFocus += (_, e) =>
        {
            if (e.OriginalSource == SettingsTab)
            {
                RefreshSettingsContext();
            }
        };
        LoadSettingsPage();
    }

    private void LoadSettingsPage()
    {
        KeyMap keys;
        string? problem = null;
        try
        {
            keys = _keyStorage.Load();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            keys = new KeyMap();
            problem = $"{ex.Message}。下面显示的是默认值，保存会覆盖它。";
        }

        ShowSettings(keys, _storage.LoadSettings());
        SetSettingsStatus(problem is null ? StatusKind.Idle : StatusKind.Error, problem ?? "已读取保存的设置。");
    }

    private void ShowSettings(KeyMap keys, UserSettings retry)
    {
        _settingsLoading = true;
        foreach (var (name, box) in SettingsKeyBoxes)
        {
            box.Text = keys.KeyFor(name);
        }

        SettingsFakeActivate.IsChecked = keys.FakeActivate;
        SettingsHold.Text = Ms(keys.HoldMs);
        SettingsTwice.Text = Ms(keys.TwiceMs);
        SettingsSplash.Text = Ms(keys.SplashMs);
        SettingsAnim.Text = Ms(keys.UltAnimMs);
        int?[] slots = [keys.Ult1AnimMs, keys.Ult2AnimMs, keys.Ult3AnimMs, keys.Ult4AnimMs];
        for (var i = 0; i < 4; i++)
        {
            SettingsAnimBoxes[i].Text = slots[i] is { } ms ? Ms(ms) : string.Empty;
        }

        SettingsPauseAfter.Text = Ms(retry.PauseAfterFailures);
        SettingsAllowedPending.Text = Ms(retry.AllowedPendingPerBattle);
        SettingsPauseAfterPending.Text = Ms(retry.PauseAfterPending);
        _settingsLoading = false;
        RefreshSettingsContext();

        static string Ms(int value) => value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Team names next to the slots (R74) and the folders with their sizes.</summary>
    private void RefreshSettingsContext()
    {
        var team = SequenceTeamSlots();
        TextBlock[] keyLabels = [SettingsKeyUlt1Label, SettingsKeyUlt2Label, SettingsKeyUlt3Label, SettingsKeyUlt4Label];
        TextBlock[] animLabels = [SettingsAnim1Label, SettingsAnim2Label, SettingsAnim3Label, SettingsAnim4Label];
        for (var i = 0; i < 4; i++)
        {
            var who = team[i] is { } name ? $"（{name}）" : string.Empty;
            keyLabels[i].Text = $"终结技 {i + 1}号{who}";
            animLabels[i].Text = $"{i + 1}号{who}";
        }

        var data = SequenceDataRoot;
        SettingsDataPath.Text = data;
        SettingsLogPath.Text = _storage.RootDirectory;
        ShowFolderSize(SettingsVideoRunsPath, Path.Combine(data, "video-runs"));
        ShowFolderSize(SettingsRecordingsPath, Path.Combine(data, "recordings"));
    }

    private static async void ShowFolderSize(TextBlock target, string folder)
    {
        target.Text = folder;
        target.ToolTip = folder;
        var bytes = await Task.Run(() =>
        {
            try
            {
                return Directory.Exists(folder)
                    ? new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length)
                    : 0L;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return -1L;
            }
        });
        var size = bytes switch
        {
            < 0 => "大小读不出",
            >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
            _ => $"{bytes / (double)(1L << 20):0} MB",
        };
        target.Text = $"{folder}（{size}）";
    }

    private void SettingsField_Changed(object sender, RoutedEventArgs e)
    {
        if (!_settingsLoading)
        {
            SetSettingsStatus(StatusKind.Warning, "有改动还没保存，点“保存设置”后生效。");
        }
    }

    private void SettingsSaveButton_Click(object sender, RoutedEventArgs e)
    {
        var problems = new List<string>();
        var keys = new KeyMap();
        foreach (var (name, box) in SettingsKeyBoxes)
        {
            try
            {
                keys = keys.With(name, box.Text.Trim());
            }
            catch (ArgumentException ex)
            {
                problems.Add($"{KeyLabels[name]}：{ex.Message}");
            }
        }

        int Number(TextBox box, string label, int max, List<string> list)
        {
            if (int.TryParse(box.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value <= max)
            {
                return value;
            }

            list.Add($"{label}要填 0–{max} 的整数。");
            return 0;
        }

        keys = keys with
        {
            FakeActivate = SettingsFakeActivate.IsChecked == true,
            HoldMs = Number(SettingsHold, "按住时长", 1000, problems),
            TwiceMs = Number(SettingsTwice, "按两次的间隔", 2000, problems),
            SplashMs = Number(SettingsSplash, "终结技立绘时长", 5000, problems),
            UltAnimMs = Number(SettingsAnim, "终结技动画时长", 20000, problems),
        };
        for (var slot = 1; slot <= 4; slot++)
        {
            var box = SettingsAnimBoxes[slot - 1];
            keys = keys.WithSlotAnim(slot, box.Text.Trim().Length == 0 ? null : Number(box, $"{slot}号位动画时长", 20000, problems));
        }

        var retry = _storage.LoadSettings() with
        {
            PauseAfterFailures = Number(SettingsPauseAfter, "连续失败的次数", UserSettings.MaxPauseAfterFailures, problems),
            AllowedPendingPerBattle = Number(SettingsAllowedPending, "每局容许的待定次数", UserSettings.MaxAllowedPendingPerBattle, problems),
            PauseAfterPending = Number(SettingsPauseAfterPending, "同一步待定累计的次数", UserSettings.MaxPauseAfterPending, problems),
        };
        if (problems.Count > 0)
        {
            SetSettingsStatus(StatusKind.Error, "没有保存：" + string.Join(" ", problems));
            return;
        }

        try
        {
            keys.Validate();
            _keyStorage.Save(keys);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            SetSettingsStatus(StatusKind.Error, "保存失败：" + ex.Message);
            return;
        }

        _storage.SaveSettings(retry);
        _storage.Log("settings", $"保存设置 | splash={keys.SplashMs} | anim={keys.UltAnimMs} | 失败暂停={retry.PauseAfterFailures}"
            + $" | 容许待定={retry.AllowedPendingPerBattle} | 待定暂停={retry.PauseAfterPending}");
        ShowSettings(keys, retry);
        var shared = keys.SharedKeys()
            .Select(s => $"{s.Key} 同时用于{string.Join("、", s.Names.Select(n => KeyLabels[n]))}")
            .ToList();
        SetSettingsStatus(shared.Count > 0 ? StatusKind.Warning : StatusKind.Success,
            $"已保存（{DateTime.Now:HH:mm:ss}）。" + (shared.Count > 0 ? "提醒：" + string.Join("；", shared) + "。" : string.Empty));
    }

    private void SettingsReloadButton_Click(object sender, RoutedEventArgs e) => LoadSettingsPage();

    private void SettingsDefaultsButton_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "把键位、发键方式、战斗参数和自动重开都改回默认值并保存吗？", "恢复默认",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
        {
            return;
        }

        ShowSettings(new KeyMap(), new UserSettings());
        SettingsSaveButton_Click(sender, e);
    }

    private void SettingsOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = (sender as FrameworkElement)?.Tag switch
        {
            "data" => SequenceDataRoot,
            "video-runs" => Path.Combine(SequenceDataRoot, "video-runs"),
            "recordings" => Path.Combine(SequenceDataRoot, "recordings"),
            _ => _storage.RootDirectory,
        };

        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetSettingsStatus(StatusKind.Error, "无法打开文件夹：" + ex.Message);
            return;
        }

        OpenFolder(folder);
    }

    private void SetSettingsStatus(StatusKind kind, string text)
    {
        SettingsStatusText.Foreground = kind == StatusKind.Idle ? (Brush)FindResource("MutedBrush") : new SolidColorBrush(StatusColor(kind));
        SettingsStatusText.Text = text;
    }
}
