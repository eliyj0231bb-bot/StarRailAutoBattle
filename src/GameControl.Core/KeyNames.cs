namespace GameControl;

/// <summary>Key names accepted in control-keys.json and `srctl keys set`, mapped to Windows virtual-key codes.</summary>
public static class KeyNames
{
    private static readonly Dictionary<string, int> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Space"] = 0x20, ["空格"] = 0x20,
        ["Enter"] = 0x0D, ["Tab"] = 0x09,
        ["Esc"] = 0x1B, ["Escape"] = 0x1B,
        ["Shift"] = 0x10, ["Ctrl"] = 0x11, ["Alt"] = 0x12,
        ["Left"] = 0x25, ["Up"] = 0x26, ["Right"] = 0x27, ["Down"] = 0x28,
    };

    /// <summary>Arrow keys sit on the extended part of the keyboard; their lParam needs bit 24.</summary>
    public static bool IsExtended(int vk) => vk is >= 0x25 and <= 0x28;

    public static bool TryParse(string? text, out int vk)
    {
        vk = 0;
        var name = text?.Trim() ?? "";
        if (name.Length == 1 && char.IsAsciiLetterOrDigit(name[0]))
        {
            vk = char.ToUpperInvariant(name[0]);
            return true;
        }

        if (name.Length is 2 or 3 && (name[0] is 'F' or 'f') && int.TryParse(name[1..], out var f) && f is >= 1 and <= 12)
        {
            vk = 0x70 + f - 1;
            return true;
        }

        return Named.TryGetValue(name, out vk);
    }

    /// <summary>The name written back to the settings file and the ack.</summary>
    public static string Format(int vk) => vk switch
    {
        >= '0' and <= '9' or >= 'A' and <= 'Z' => ((char)vk).ToString(),
        >= 0x70 and <= 0x7B => $"F{vk - 0x70 + 1}",
        0x20 => "Space",
        0x1B => "Esc",
        _ => Named.FirstOrDefault(p => p.Value == vk && p.Key.All(char.IsAscii)).Key ?? $"0x{vk:X2}",
    };
}
