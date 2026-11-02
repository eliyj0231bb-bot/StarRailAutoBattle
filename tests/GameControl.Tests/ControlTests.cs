using System.Diagnostics;
using System.Runtime.InteropServices;
using GameControl;
using GameLauncher.Core;

namespace GameControl.Tests;

public class ControlTests
{
    private sealed class FakeTarget(string? error = null) : IKeyTarget
    {
        public List<string> Log { get; } = [];

        public string? KeyDown(int vk)
        {
            Log.Add($"down {KeyNames.Format(vk)}");
            return error;
        }

        public string? KeyUp(int vk)
        {
            Log.Add($"up {KeyNames.Format(vk)}");
            return null;
        }
    }

    // R62
    [Theory]
    [InlineData("""{"op":"basic"}""", "basic", null)]
    [InlineData("""{"op":"skill"}""", "skill", null)]
    [InlineData("""{"op":"ult","slot":4}""", "ult", 4)]
    [InlineData("""{"op":"confirm"}""", "confirm", null)]
    [InlineData("""{"op":"left"}""", "left", null)]
    [InlineData("""{"op":"menu"}""", "menu", null)]
    public void Reads_each_instruction(string line, string op, int? slot)
    {
        Assert.True(ControlCommand.TryParseJson(line, out var command, out _));
        Assert.Equal(new ControlCommand(op, slot), command);
    }

    // R62: unclear instructions are refused with a reason, never guessed.
    [Theory]
    [InlineData("""{"op":"ult"}""", "ult 需要 slot 1–4")]
    [InlineData("""{"op":"ult","slot":5}""", "ult 需要 slot 1–4")]
    [InlineData("""{"op":"ult","slot":"2"}""", "slot 要是整数")]
    [InlineData("""{"op":"skill","slot":1}""", "skill 不带 slot")]
    [InlineData("""{"op":"jump"}""", "op 要是 basic、skill、ult、confirm、left、right、menu 之一")]
    [InlineData("""skill""", "不是 JSON 对象")]
    [InlineData("""[1]""", "不是 JSON 对象")]
    public void Refuses_unclear_instructions(string line, string error)
    {
        Assert.False(ControlCommand.TryParseJson(line, out _, out var actual));
        Assert.Equal(error, actual);
    }

    // Real run: the first piped line arrived as U+FEFF + {"op":"ult","slot":1} and was refused, so the ult never fired.
    [Fact]
    public void Reads_a_first_line_with_a_bom()
    {
        Assert.True(ControlCommand.TryParseJson("﻿{\"op\":\"ult\",\"slot\":1}", out var command, out _));
        Assert.Equal(new ControlCommand("ult", 1), command);
    }

    [Fact]
    public void Reads_send_words()
    {
        Assert.True(ControlCommand.TryParseWords(["ult", "2"], out var command, out _));
        Assert.Equal(new ControlCommand("ult", 2), command);
        Assert.False(ControlCommand.TryParseWords(["ult", "二"], out _, out var error));
        Assert.Equal("号位要是整数", error);
    }

    // R64: defaults Q/E/1-4/Space/A/D/Esc.
    [Theory]
    [InlineData("basic", null, "Q")]
    [InlineData("skill", null, "E")]
    [InlineData("ult", 1, "1")]
    [InlineData("ult", 3, "3")]
    [InlineData("confirm", null, "Space")]
    [InlineData("left", null, "A")]
    [InlineData("right", null, "D")]
    [InlineData("menu", null, "Esc")]
    public void Default_keys(string op, int? slot, string key)
    {
        Assert.Equal(key, new KeyMap().KeyFor(new ControlCommand(op, slot)));
    }

    // R62, R65: key-down, held, key-up, then one ack line.
    [Fact]
    public void Ult_presses_the_slot_key_and_acks()
    {
        var target = new FakeTarget();
        var held = new List<int>();
        var ack = new Controller(new KeyMap(), target, held.Add).HandleJson("""{"op":"ult","slot":2}""");

        Assert.Equal(["down 2", "up 2"], target.Log);
        Assert.Equal([30], held);
        Assert.StartsWith("""{"ok":true,"op":"ult","slot":2,"key":"2","ms":""", ack);
    }

    [Fact]
    public void Bad_instruction_presses_nothing()
    {
        var target = new FakeTarget();
        var ack = new Controller(new KeyMap(), target, _ => { }).HandleJson("""{"op":"ult"}""");
        Assert.Empty(target.Log);
        Assert.Equal("""{"ok":false,"error":"ult 需要 slot 1–4"}""", ack);
    }

    // R63: a refused send is reported in the ack, and no key-up follows.
    [Fact]
    public void Send_failure_is_reported()
    {
        var target = new FakeTarget("拒绝访问");
        var ack = new Controller(new KeyMap(), target, _ => { }).HandleJson("""{"op":"skill"}""");
        Assert.Equal(["down E"], target.Log);
        Assert.Equal("""{"ok":false,"op":"skill","key":"E","error":"拒绝访问"}""", ack);
    }

    // R64
    [Fact]
    public void Changed_key_is_saved_and_used()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"srctl-test-{Guid.NewGuid():N}");
        try
        {
            var storage = new KeyMapStorage(dir);
            storage.Save(storage.Load().With("confirm", "e").With("ult2", "f2") with { HoldMs = 50 });
            var loaded = storage.Load();
            Assert.Equal("E", loaded.Confirm);
            Assert.Equal("F2", loaded.Ult2);
            Assert.Equal(50, loaded.HoldMs);
            Assert.Equal("Q", loaded.Basic);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Unknown_key_or_op_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new KeyMap().With("confirm", "鼠标左键"));
        Assert.Throws<ArgumentException>(() => new KeyMap().With("jump", "Space"));
    }

    [Fact]
    public void Damaged_key_file_is_an_error_not_defaults()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"srctl-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(dir);
            var storage = new KeyMapStorage(dir);
            File.WriteAllText(storage.FilePath, """{"confirm":"???"}""");
            Assert.Throws<InvalidDataException>(storage.Load);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Theory]
    [InlineData("q", 0x51)]
    [InlineData("7", 0x37)]
    [InlineData("F12", 0x7B)]
    [InlineData("space", 0x20)]
    [InlineData("空格", 0x20)]
    [InlineData("Esc", 0x1B)]
    [InlineData("Left", 0x25)]
    public void Key_names(string name, int vk)
    {
        Assert.True(KeyNames.TryParse(name, out var actual));
        Assert.Equal(vk, actual);
    }

    [Fact]
    public void Key_lparam_matches_a_real_keyboard()
    {
        // Q: scan code 0x10.
        Assert.Equal(0x00100001, (int)BackgroundKeys.KeyLParam(0x10, false, false));
        Assert.Equal(unchecked((int)0xC0100001), (int)BackgroundKeys.KeyLParam(0x10, false, true));
        // Left arrow: scan 0x4B, extended.
        Assert.Equal(0x014B0001, (int)BackgroundKeys.KeyLParam(0x4B, true, false));
    }

    [Fact]
    public void Picks_the_unity_window()
    {
        static WindowRecord W(int handle, string cls, int w, int h, bool owner = false) =>
            new(handle, 1, "", cls, true, false, owner, false, w, h);

        var picked = GameWindow.Pick([W(1, "IME", 800, 600), W(2, "UnityWndClass", 1920, 1080), W(3, "Popup", 2000, 2000, owner: true)]);
        Assert.Equal(2, (int)picked!.Handle);
        Assert.Null(GameWindow.Pick([W(3, "Popup", 2000, 2000, owner: true)]));
    }

    // R63, R65: real PostMessage into a window of our own while it is NOT in front; the window receives the keys.
    [Fact]
    public void Background_keys_reach_a_window_that_is_not_in_front()
    {
        using var window = new MessageWindow();
        var target = new BackgroundKeys(() => window.Handle);
        var stopwatch = Stopwatch.StartNew();
        var ack = new Controller(new KeyMap() with { HoldMs = 0 }, target).HandleJson("""{"op":"ult","slot":3}""");
        Assert.StartsWith("""{"ok":true""", ack);
        Assert.NotEqual(window.Handle, GetForegroundWindow());

        var received = window.Pump();
        Assert.Equal([(0x0100u, 0x33), (0x0101u, 0x33)], received);
        Assert.True(stopwatch.ElapsedMilliseconds < 50, $"{stopwatch.ElapsedMilliseconds} ms");
    }

    // R66: with the experiment on, activation messages come first, the window stays in the background, and the ack says so.
    [Fact]
    public void Fake_activate_posts_activation_before_the_key()
    {
        using var window = new MessageWindow();
        var keys = new KeyMap { HoldMs = 0, FakeActivate = true };
        var ack = new Controller(keys, new BackgroundKeys(() => window.Handle, fakeActivate: true)).HandleJson("""{"op":"basic"}""");

        Assert.StartsWith("""{"ok":true,"op":"basic","key":"Q","activate":true,""", ack);
        Assert.NotEqual(window.Handle, GetForegroundWindow());
        Assert.Equal([(0x001Cu, 1), (0x0006u, 1), (0x0007u, 0), (0x0100u, 0x51), (0x0101u, 0x51)], window.PumpWithActivation());
    }

    [Fact]
    public void Fake_activate_is_off_by_default()
    {
        using var window = new MessageWindow();
        var ack = new Controller(new KeyMap { HoldMs = 0 }, new BackgroundKeys(() => window.Handle)).HandleJson("""{"op":"basic"}""");

        Assert.DoesNotContain("activate", ack);
        Assert.Equal([(0x0100u, 0x51), (0x0101u, 0x51)], window.PumpWithActivation());
    }

    [Fact]
    public void Fake_activate_switch_is_saved()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"srctl-test-{Guid.NewGuid():N}");
        try
        {
            var storage = new KeyMapStorage(dir);
            Assert.False(storage.Load().FakeActivate);
            storage.Save(storage.Load() with { FakeActivate = true });
            Assert.True(storage.Load().FakeActivate);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // R65: serve finds the window up front and presses no key while warming up.
    [Fact]
    public void Prepare_caches_the_window_without_pressing_a_key()
    {
        using var window = new MessageWindow();
        var searches = 0;
        var target = new BackgroundKeys(() => { searches++; return window.Handle; });
        Assert.Null(target.Prepare());
        Assert.Equal(window.Handle, target.Window);
        Assert.Empty(window.Pump());

        new Controller(new KeyMap() with { HoldMs = 0 }, target).HandleJson("""{"op":"basic"}""");
        Assert.Equal(1, searches);
        Assert.Contains("没找到游戏窗口", new BackgroundKeys(() => IntPtr.Zero).Prepare());
    }

    [Fact]
    public void Missing_game_window_is_reported()
    {
        var ack = new Controller(new KeyMap(), new BackgroundKeys(() => IntPtr.Zero), _ => { }).HandleJson("""{"op":"basic"}""");
        Assert.Contains("没找到游戏窗口", ack);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
