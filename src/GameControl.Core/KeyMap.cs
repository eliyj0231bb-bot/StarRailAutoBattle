using System.Text.Json;

namespace GameControl;

/// <summary>Which key each operation sends (R64) and how long a key is held (R65).</summary>
public sealed record KeyMap
{
    public string Basic { get; init; } = "Q";
    public string Skill { get; init; } = "E";
    public string Ult1 { get; init; } = "1";
    public string Ult2 { get; init; } = "2";
    public string Ult3 { get; init; } = "3";
    public string Ult4 { get; init; } = "4";
    public string Confirm { get; init; } = "Space";
    public string Left { get; init; } = "A";
    public string Right { get; init; } = "D";
    public string Menu { get; init; } = "Esc";
    public int HoldMs { get; init; } = 30;

    /// <summary>R71: the splash after an ult key; the next ult key or confirm goes down at least this long after it.</summary>
    public int SplashMs { get; init; } = 800;

    /// <summary>R72: basic and skill are pressed twice (select, then use) with this pause between; 0 = once.</summary>
    public int TwiceMs { get; init; } = 300;

    /// <summary>R68, R69: after the confirm that releases an ult, the next key waits this long for the animation.</summary>
    public int UltAnimMs { get; init; } = 3000;

    /// <summary>R69: per team slot overrides of <see cref="UltAnimMs"/>, e.g. for an ult with a follow-up action.</summary>
    public int? Ult1AnimMs { get; init; }
    public int? Ult2AnimMs { get; init; }
    public int? Ult3AnimMs { get; init; }
    public int? Ult4AnimMs { get; init; }

    public int AnimFor(int slot) => slot switch
    {
        1 => Ult1AnimMs,
        2 => Ult2AnimMs,
        3 => Ult3AnimMs,
        4 => Ult4AnimMs,
        _ => null,
    } ?? UltAnimMs;

    public KeyMap WithSlotAnim(int slot, int? ms) => slot switch
    {
        1 => this with { Ult1AnimMs = ms },
        2 => this with { Ult2AnimMs = ms },
        3 => this with { Ult3AnimMs = ms },
        4 => this with { Ult4AnimMs = ms },
        _ => throw new ArgumentException($"号位要是 1–4：{slot}"),
    };

    /// <summary>R66 experiment: tell the background game it is active before each key-down. Off by default.</summary>
    public bool FakeActivate { get; init; }

    /// <summary>The settings names users type in `srctl keys set`: the ops of R62, with ult split per slot.</summary>
    public static readonly string[] Names = ["basic", "skill", "ult1", "ult2", "ult3", "ult4", "confirm", "left", "right", "menu"];

    public string KeyFor(string name) => name switch
    {
        "basic" => Basic,
        "skill" => Skill,
        "ult1" => Ult1,
        "ult2" => Ult2,
        "ult3" => Ult3,
        "ult4" => Ult4,
        "confirm" => Confirm,
        "left" => Left,
        "right" => Right,
        "menu" => Menu,
        _ => throw new ArgumentException($"没有这个操作：{name}。可用：{string.Join("、", Names)}"),
    };

    public string KeyFor(ControlCommand command) => KeyFor(command.Op == "ult" ? $"ult{command.Slot}" : command.Op);

    public KeyMap With(string name, string key)
    {
        if (!KeyNames.TryParse(key, out var vk))
        {
            throw new ArgumentException($"认不出这个键：{key}。可用字母、数字、F1–F12、Space、Enter、Tab、Esc、Shift、Ctrl、Alt、Left、Right、Up、Down。");
        }

        var k = KeyNames.Format(vk);
        return name switch
        {
            "basic" => this with { Basic = k },
            "skill" => this with { Skill = k },
            "ult1" => this with { Ult1 = k },
            "ult2" => this with { Ult2 = k },
            "ult3" => this with { Ult3 = k },
            "ult4" => this with { Ult4 = k },
            "confirm" => this with { Confirm = k },
            "left" => this with { Left = k },
            "right" => this with { Right = k },
            "menu" => this with { Menu = k },
            _ => throw new ArgumentException($"没有这个操作：{name}。可用：{string.Join("、", Names)}"),
        };
    }

    /// <summary>Settings file errors are reported, not silently replaced: a wrong key would press the wrong button.</summary>
    public void Validate()
    {
        foreach (var name in Names)
        {
            if (!KeyNames.TryParse(KeyFor(name), out _))
            {
                throw new InvalidDataException($"键位文件里 {name} 的键认不出：{KeyFor(name)}");
            }
        }

        if (HoldMs is < 0 or > 1000)
        {
            throw new InvalidDataException($"键位文件里 holdMs 要在 0–1000：{HoldMs}");
        }

        if (SplashMs is < 0 or > 5000)
        {
            throw new InvalidDataException($"键位文件里 splashMs 要在 0–5000：{SplashMs}");
        }

        if (TwiceMs is < 0 or > 2000)
        {
            throw new InvalidDataException($"键位文件里 twiceMs 要在 0–2000：{TwiceMs}");
        }

        for (var slot = 1; slot <= 4; slot++)
        {
            if (AnimFor(slot) is < 0 or > 20000)
            {
                throw new InvalidDataException($"键位文件里 {slot} 号位的动画时长要在 0–20000：{AnimFor(slot)}");
            }
        }

        if (UltAnimMs is < 0 or > 20000)
        {
            throw new InvalidDataException($"键位文件里 ultAnimMs 要在 0–20000：{UltAnimMs}");
        }
    }
}

/// <summary>control-keys.json next to the launcher's settings (R64).</summary>
public sealed class KeyMapStorage(string? rootDirectory = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public string RootDirectory { get; } = rootDirectory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StarRailAutoBattle");

    public string FilePath => Path.Combine(RootDirectory, "control-keys.json");

    public KeyMap Load()
    {
        if (!File.Exists(FilePath))
        {
            return new KeyMap();
        }

        KeyMap map;
        try
        {
            map = JsonSerializer.Deserialize<KeyMap>(File.ReadAllText(FilePath), JsonOptions) ?? new KeyMap();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"键位文件读不懂：{FilePath}（{ex.Message}）");
        }

        map.Validate();
        return map;
    }

    public void Save(KeyMap map)
    {
        Directory.CreateDirectory(RootDirectory);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(map, JsonOptions));
    }
}
