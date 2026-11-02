using System.Text.Json;
using System.Text.Json.Nodes;

namespace GameControl;

/// <summary>One instruction of R62: an op, the team slot for ult, and the caller's own timing (R68).</summary>
public sealed record ControlCommand(string Op, int? Slot = null, int? After = null)
{
    public static readonly string[] Ops = ["basic", "skill", "ult", "confirm", "left", "right", "menu"];

    /// <summary>Parses one JSON line such as {"op":"ult","slot":2}. Anything unclear is an error, not a guess (R62).</summary>
    public static bool TryParseJson(string line, out ControlCommand? command, out string error)
    {
        command = null;
        JsonObject? obj;
        try
        {
            // A caller writing UTF-8 with a BOM puts U+FEFF before the first instruction.
            obj = JsonNode.Parse(line.TrimStart('﻿')) as JsonObject;
        }
        catch (JsonException)
        {
            obj = null;
        }

        if (obj is null)
        {
            error = "不是 JSON 对象";
            return false;
        }

        var op = obj["op"] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
        int? slot = null;
        if (obj["slot"] is { } slotNode)
        {
            if (slotNode is not JsonValue sv || !sv.TryGetValue<int>(out var n))
            {
                error = "slot 要是整数";
                return false;
            }

            slot = n;
        }

        if (!TryCreate(op, slot, out command, out error))
        {
            return false;
        }

        if (obj["after"] is { } afterNode)
        {
            if (afterNode is not JsonValue av || !av.TryGetValue<int>(out var after) || after < 0)
            {
                command = null;
                error = "after 要是 0 或正整数（毫秒）";
                return false;
            }

            command = command! with { After = after };
        }

        return true;
    }

    /// <summary>`srctl send ult 2` style: the op word and an optional slot word.</summary>
    public static bool TryParseWords(IReadOnlyList<string> words, out ControlCommand? command, out string error)
    {
        command = null;
        int? slot = null;
        if (words.Count > 2 || words.Count == 0)
        {
            error = "写法：send <操作> [号位]";
            return false;
        }

        if (words.Count == 2)
        {
            if (!int.TryParse(words[1], out var n))
            {
                error = "号位要是整数";
                return false;
            }

            slot = n;
        }

        return TryCreate(words[0], slot, out command, out error);
    }

    private static bool TryCreate(string? op, int? slot, out ControlCommand? command, out string error)
    {
        command = null;
        if (op is null || !Ops.Contains(op))
        {
            error = $"op 要是 {string.Join("、", Ops)} 之一";
            return false;
        }

        if (op == "ult" && slot is not (>= 1 and <= 4))
        {
            error = "ult 需要 slot 1–4";
            return false;
        }

        if (op != "ult" && slot is not null)
        {
            error = $"{op} 不带 slot";
            return false;
        }

        command = new ControlCommand(op, slot);
        error = "";
        return true;
    }
}
