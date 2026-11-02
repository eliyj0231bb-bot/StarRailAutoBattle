using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GameControl;

/// <summary>Where key presses go. Returns null on success, otherwise the reason in plain words.</summary>
public interface IKeyTarget
{
    string? KeyDown(int vk);

    string? KeyUp(int vk);
}

/// <summary>Turns one instruction line into a key press and one JSON ack line (R62, R65).</summary>
public sealed class Controller(KeyMap keys, IKeyTarget target, Action<int>? hold = null)
{
    private static readonly JsonSerializerOptions AckOptions = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly Action<int> _hold = hold ?? Thread.Sleep;

    public string HandleJson(string line)
    {
        var started = Stopwatch.GetTimestamp();
        return ControlCommand.TryParseJson(line, out var command, out var error)
            ? Press(command!, started)
            : Fail(error);
    }

    public string Press(ControlCommand command, long started)
    {
        KeyNames.TryParse(keys.KeyFor(command), out var vk);
        var ack = new JsonObject { ["op"] = command.Op };
        if (command.Slot is { } slot)
        {
            ack["slot"] = slot;
        }

        ack["key"] = KeyNames.Format(vk);
        if (keys.FakeActivate)
        {
            ack["activate"] = true;
        }

        if (target.KeyDown(vk) is { } downError)
        {
            return Fail(downError, ack);
        }

        // R65: ms is from reading the instruction to sending the key-down.
        var ms = Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds, 1);
        _hold(keys.HoldMs);
        if (target.KeyUp(vk) is { } upError)
        {
            return Fail(upError, ack);
        }

        ack["ms"] = ms;
        ack.Insert(0, "ok", true);
        return ack.ToJsonString(AckOptions);
    }

    public static string Fail(string error, JsonObject? ack = null)
    {
        ack ??= [];
        ack.Insert(0, "ok", false);
        ack["error"] = error;
        return ack.ToJsonString(AckOptions);
    }
}
