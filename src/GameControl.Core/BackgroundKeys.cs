using System.Runtime.InteropServices;
using GameLauncher.Core;

namespace GameControl;

/// <summary>
/// Posts WM_KEYDOWN / WM_KEYUP straight to the game window, so the game does not have to be in front (R63).
/// No SendInput, no foreground switching, no injection; if the game ignores posted keys that is reported, not worked around.
/// </summary>
public sealed class BackgroundKeys(Func<IntPtr> findWindow, bool fakeActivate = false) : IKeyTarget
{
    internal const uint WM_NULL = 0x0000;
    internal const uint WM_ACTIVATE = 0x0006;
    internal const uint WM_SETFOCUS = 0x0007;
    internal const uint WM_ACTIVATEAPP = 0x001C;
    internal const uint WM_KEYDOWN = 0x0100;
    internal const uint WM_KEYUP = 0x0101;
    private const int ERROR_ACCESS_DENIED = 5;

    private IntPtr _window;

    /// <summary>The window keys went to last time; cached so a press costs one PostMessage (R65).</summary>
    public IntPtr Window => _window;

    /// <summary>R66: optionally tells the game it is active (in the order Windows sends these) before the key-down.</summary>
    public string? KeyDown(int vk) => fakeActivate
        ? FindWindow() ?? Send(WM_ACTIVATEAPP, 1) ?? Send(WM_ACTIVATE, 1) ?? Send(WM_SETFOCUS, 0) ?? Send(WM_KEYDOWN, vk)
        : Post(WM_KEYDOWN, vk);

    public string? KeyUp(int vk) => Post(WM_KEYUP, vk);

    /// <summary>
    /// R65: finds and caches the window and posts WM_NULL (which the game ignores) so the first real key
    /// does not pay for the window search or the first native call. Returns the same errors a key would.
    /// </summary>
    public string? Prepare() => FindWindow() ?? Send(WM_NULL, 0);

    private string? FindWindow()
    {
        if (_window == IntPtr.Zero || !IsWindow(_window))
        {
            _window = findWindow();
            if (_window == IntPtr.Zero)
            {
                return "没找到游戏窗口（StarRail.exe 没在运行）";
            }
        }

        return null;
    }

    private string? Post(uint message, int vk) => FindWindow() ?? Send(message, vk);

    /// <summary>For key messages wParam is the virtual key; for the R66 activation messages it is their flag.</summary>
    private string? Send(uint message, int wParam)
    {
        var lParam = message is not (WM_KEYDOWN or WM_KEYUP) ? IntPtr.Zero : KeyLParam(MapVirtualKeyW((uint)wParam, 0), KeyNames.IsExtended(wParam), message == WM_KEYUP);
        if (PostMessageW(_window, message, wParam, lParam))
        {
            return null;
        }

        var error = Marshal.GetLastWin32Error();
        return error != ERROR_ACCESS_DENIED
            ? $"发送按键失败（Windows 错误 {error}）"
            : IsElevated()
                ? "拒绝访问：srctl 已是管理员身份，游戏仍拒收按键消息（R63，不绕过）"
                : "拒绝访问：srctl 不是管理员身份；游戏以管理员身份运行时，srctl 也要以管理员身份运行（R63）";
    }

    private static bool IsElevated() =>
        new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent())
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);

    /// <summary>The lParam a real keyboard produces: repeat 1, scan code, extended bit, and for key-up the previous-state and transition bits.</summary>
    internal static IntPtr KeyLParam(uint scanCode, bool extended, bool keyUp)
    {
        var value = 1u | ((scanCode & 0xFF) << 16);
        if (extended)
        {
            value |= 1u << 24;
        }

        if (keyUp)
        {
            value |= (1u << 30) | (1u << 31);
        }

        return (IntPtr)(nint)unchecked((int)value);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(IntPtr hwnd, uint msg, nint wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyW(uint code, uint mapType);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hwnd);
}

/// <summary>Finds the game's main window by process name. The game is a native client, never a browser (R1).</summary>
public static class GameWindow
{
    public const string DefaultProcess = "StarRail";

    public static IntPtr Find(string processName = DefaultProcess)
    {
        var processes = WindowService.GetProcessesByName(processName);
        if (processes.Count == 0)
        {
            return IntPtr.Zero;
        }

        var windows = WindowService.GetTopLevelWindows(processes.Select(p => p.Id).ToHashSet());
        return Pick(windows)?.Handle ?? IntPtr.Zero;
    }

    /// <summary>The Unity main window if there is one, else the largest plain top-level window.</summary>
    internal static WindowRecord? Pick(IEnumerable<WindowRecord> windows)
    {
        var plain = windows.Where(w => !w.HasOwner && !w.IsToolWindow && w.Width > 0 && w.Height > 0).ToList();
        return plain.FirstOrDefault(w => w.ClassName == "UnityWndClass")
            ?? plain.OrderByDescending(w => (long)w.Width * w.Height).FirstOrDefault();
    }
}
