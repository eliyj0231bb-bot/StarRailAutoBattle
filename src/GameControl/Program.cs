using System.Diagnostics;
using System.Text;

namespace GameControl;

/// <summary>srctl: the controller (R61). It only posts keys to the game window; it never reads memory or injects.</summary>
internal static class Program
{
    private static int Main(string[] argv)
    {
        Console.OutputEncoding = Encoding.UTF8;
        // Encoding.UTF8 here makes the first piped line start with U+FEFF, which broke the first instruction.
        Console.InputEncoding = new UTF8Encoding(false);
        var words = argv.ToList();
        var process = TakeOption(words, "process") ?? GameWindow.DefaultProcess;
        var storage = new KeyMapStorage(TakeOption(words, "settings"));
        try
        {
            switch (words.ElementAtOrDefault(0))
            {
                case "serve" when words.Count == 1:
                    return Serve(storage.Load(), process);
                case "send":
                    return Send(storage.Load(), process, words.Skip(1).ToList());
                case "keys":
                    return Keys(storage, words.Skip(1).ToList());
                case null or "help":
                    PrintHelp();
                    return 0;
                default:
                    Console.Error.WriteLine($"用法错误：{string.Join(' ', words)}");
                    Console.Error.WriteLine("运行 srctl help 查看命令。");
                    return 2;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            Console.Error.WriteLine("错误：" + ex.Message);
            return 1;
        }
    }

    /// <summary>R62/R65: stays running, one JSON instruction per input line, one JSON ack per output line.</summary>
    private static int Serve(KeyMap keys, string process)
    {
        var target = new BackgroundKeys(() => GameWindow.Find(process), keys.FakeActivate);
        var controller = new Controller(keys, target);
        Console.Error.WriteLine(target.Prepare() is { } error
            ? $"srctl：{error}；收到指令时再试。"
            : $"srctl：已找到 {process}.exe 的窗口，等待指令。");

        // R65: run the parse/ack path once without pressing anything, so the first real instruction is not slowed by JIT.
        var warm = new Controller(keys, new NoKeys(), _ => { });
        warm.HandleJson("""{"op":"ult","slot":1}""");
        warm.HandleJson("""{"op":"?"}""");
        while (Console.In.ReadLine() is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                Console.Out.WriteLine(controller.HandleJson(line));
            }
        }

        return 0;
    }

    private static int Send(KeyMap keys, string process, List<string> words)
    {
        var started = Stopwatch.GetTimestamp();
        if (!ControlCommand.TryParseWords(words, out var command, out var error))
        {
            Console.Out.WriteLine(Controller.Fail(error));
            return 2;
        }

        var ack = new Controller(keys, new BackgroundKeys(() => GameWindow.Find(process), keys.FakeActivate)).Press(command!, started);
        Console.Out.WriteLine(ack);
        return ack.StartsWith("{\"ok\":true", StringComparison.Ordinal) ? 0 : 1;
    }

    /// <summary>R64: show, change or reset the key map.</summary>
    private static int Keys(KeyMapStorage storage, List<string> words)
    {
        var keys = storage.Load();
        switch (words.ElementAtOrDefault(0))
        {
            case null:
                break;
            case "set" when words.Count == 3:
                keys = keys.With(words[1], words[2]);
                storage.Save(keys);
                break;
            case "hold" when words.Count == 2 && int.TryParse(words[1], out var ms):
                keys = keys with { HoldMs = ms };
                keys.Validate();
                storage.Save(keys);
                break;
            case "twice" when words.Count == 2 && int.TryParse(words[1], out var twice):
                keys = keys with { TwiceMs = twice };
                keys.Validate();
                storage.Save(keys);
                break;
            case "splash" when words.Count == 2 && int.TryParse(words[1], out var splash):
                keys = keys with { SplashMs = splash };
                keys.Validate();
                storage.Save(keys);
                break;
            case "anim" when words.Count == 2 && int.TryParse(words[1], out var anim):
                keys = keys with { UltAnimMs = anim };
                keys.Validate();
                storage.Save(keys);
                break;
            case "anim" when words.Count == 3 && int.TryParse(words[1], out var animSlot) && int.TryParse(words[2], out var slotAnim):
                keys = keys.WithSlotAnim(animSlot, slotAnim);
                keys.Validate();
                storage.Save(keys);
                break;
            case "activate" when words.Count == 2 && words[1] is "on" or "off":
                keys = keys with { FakeActivate = words[1] == "on" };
                storage.Save(keys);
                break;
            case "reset" when words.Count == 1:
                keys = new KeyMap();
                storage.Save(keys);
                break;
            default:
                Console.Error.WriteLine("用法错误：keys | keys set <操作> <键> | keys hold <毫秒> | keys twice <毫秒> | keys splash <毫秒> | keys anim [号位] <毫秒> | keys activate on|off | keys reset");
                return 2;
        }

        foreach (var name in KeyMap.Names)
        {
            Console.Out.WriteLine($"{name,-8} {keys.KeyFor(name)}");
        }

        Console.Out.WriteLine($"{"hold",-8} {keys.HoldMs} 毫秒");
        Console.Out.WriteLine($"{"twice",-8} {(keys.TwiceMs > 0 ? $"{keys.TwiceMs} 毫秒（普攻、战技按两次，中间隔这么久）" : "0（普攻、战技只按一次）")}");
        Console.Out.WriteLine($"{"splash",-8} {keys.SplashMs} 毫秒（终结技键按下后放立绘，下一个终结技键或确认至少隔这么久）");
        Console.Out.WriteLine($"{"anim",-8} {keys.UltAnimMs} 毫秒（终结技释放后等动画这么久再按下一个键）");
        for (var slot = 1; slot <= 4; slot++)
        {
            Console.Out.WriteLine($"{$"anim {slot}",-8} {keys.AnimFor(slot)} 毫秒{(keys.AnimFor(slot) == keys.UltAnimMs ? "" : "（单独设置）")}");
        }
        Console.Out.WriteLine($"{"activate",-8} {(keys.FakeActivate ? "on（假激活，实验）" : "off")}");
        Console.Out.WriteLine($"文件：{storage.FilePath}");
        return 0;
    }

    private sealed class NoKeys : IKeyTarget
    {
        public string? KeyDown(int vk) => null;

        public string? KeyUp(int vk) => null;
    }

    private static string? TakeOption(List<string> words, string name)
    {
        var i = words.FindIndex(w => w.Equals("--" + name, StringComparison.OrdinalIgnoreCase));
        if (i < 0 || i + 1 >= words.Count)
        {
            return null;
        }

        var value = words[i + 1];
        words.RemoveRange(i, 2);
        return value;
    }

    private static void PrintHelp()
    {
        Console.Out.WriteLine("""
            srctl —— 星铁操控端：只给游戏窗口发按键（后台发送，游戏不用在前台）。

              srctl serve                    常驻：每行读一个 JSON 指令，每条回一行 JSON 回执
              srctl send <操作> [号位]        发一次就退出，例：srctl send ult 2
              srctl keys                     查看键位
              srctl keys set <操作> <键>      改键位，例：srctl keys set confirm E
              srctl keys hold <毫秒>          按键按住多久再抬起（默认 30）
              srctl keys twice <毫秒>         普攻、战技按两次（先选中再放出），中间隔多久；0 = 只按一次（默认 300）
              srctl keys splash <毫秒>终结技键按下后的立绘时长，下一个终结技键或确认等它放完（默认 800）
              srctl keys anim <毫秒>          终结技释放后等动画多久再按下一个键（默认 3000）
              srctl keys anim <号位> <毫秒>   给某个号位单独设动画时长（比如带附带行动的终结技）
              srctl keys activate on|off   实验：按下前先告诉后台的游戏“已激活”（默认 off）
              srctl keys reset               恢复默认键位

            操作：basic 普攻、skill 战技、ult 终结技（号位 1–4）、confirm 确认、left/right 换目标、menu 菜单
            指令例：{"op":"skill"}  {"op":"ult","slot":2}  {"op":"confirm"}
            自己定时：{"op":"ult","slot":2,"after":1200}  从上一个按键按下起等 1200 毫秒再按，不套 splash 和 anim
            回执例：{"ok":true,"op":"ult","slot":2,"key":"2","ms":0.1}

            选项：--process <进程名>  默认 StarRail
                  --settings <目录>   键位文件所在目录，默认 %LOCALAPPDATA%\StarRailAutoBattle
            游戏以管理员身份运行时，srctl 也要以管理员身份运行。
            """);
    }
}
