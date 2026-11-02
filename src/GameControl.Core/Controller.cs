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

/// <summary>Turns one instruction line into a key press and one JSON ack line (R62, R65, R67–R71).</summary>
public sealed class Controller(KeyMap keys, IKeyTarget target, Action<int>? hold = null, Func<double>? clockMs = null)
{
    private static readonly JsonSerializerOptions AckOptions = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly Action<int> _hold = hold ?? Thread.Sleep;
    private readonly Func<double> _clockMs = clockMs ?? (() => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency);

    /// <summary>R71: when the last ult key went down, until every queued ult is confirmed.</summary>
    private double? _ultDownMs;

    /// <summary>R69: slots queued in the current batch of ult keys, and how many are still waiting for a confirm.</summary>
    private readonly List<int> _batch = [];
    private int _pending;

    /// <summary>R68: no key before this time, while the released ult's animation plays.</summary>
    private double? _animEndMs;

    /// <summary>R68: when the previous key went down; "after" counts from here.</summary>
    private double? _lastDownMs;

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

        var waited = TimeSpan.Zero;
        if (ReadyAtMs(command) is { } readyAt && (int)Math.Ceiling(readyAt - _clockMs()) is var wait and > 0)
        {
            var waitStarted = Stopwatch.GetTimestamp();
            _hold(wait);
            waited = Stopwatch.GetElapsedTime(waitStarted);
            ack["wait"] = wait;
        }

        if (target.KeyDown(vk) is { } downError)
        {
            return Fail(downError, ack);
        }

        var downMs = _clockMs();
        _lastDownMs = downMs;
        if (command.Op == "ult")
        {
            if (_pending == 0)
            {
                _batch.Clear();
            }

            _batch.Add(command.Slot!.Value);
            _pending++;
            _ultDownMs = downMs;
        }
        else if (command.Op == "confirm" && _pending > 0)
        {
            // R69: which queued ult this confirm released is unknown (Q8), so wait for the longest animation in the batch.
            _animEndMs = downMs + _batch.Max(keys.AnimFor);
            if (--_pending == 0)
            {
                _ultDownMs = null;
            }
        }

        // R65: ms is from reading the instruction to sending the key-down, not counting the R67 wait.
        var ms = Math.Round((Stopwatch.GetElapsedTime(started) - waited).TotalMilliseconds, 1);
        _hold(keys.HoldMs);
        if (target.KeyUp(vk) is { } upError)
        {
            return Fail(upError, ack);
        }

        ack["ms"] = ms;
        ack.Insert(0, "ok", true);
        return ack.ToJsonString(AckOptions);
    }

    /// <summary>
    /// The earliest time this key may go down. With "after" the caller decides alone (R68); otherwise the
    /// released ult's animation (R68) and, for an ult key or confirm while ults are queued, the splash of the last ult key (R71).
    /// </summary>
    private double? ReadyAtMs(ControlCommand command)
    {
        if (command.After is { } after)
        {
            return _lastDownMs + after;
        }

        var splashEnd = command.Op is "ult" or "confirm" && _pending > 0 ? _ultDownMs + keys.SplashMs : null;
        return new[] { _animEndMs, splashEnd }.Max();
    }

    public static string Fail(string error, JsonObject? ack = null)
    {
        ack ??= [];
        ack.Insert(0, "ok", false);
        ack["error"] = error;
        return ack.ToJsonString(AckOptions);
    }
}
