using System.Runtime.InteropServices;

namespace GameControl.Tests;

/// <summary>A hidden message-only window owned by the test thread, standing in for a game window that is not in front.</summary>
internal sealed class MessageWindow : IDisposable
{
    private static readonly IntPtr HWND_MESSAGE = -3;

    public MessageWindow()
    {
        Handle = CreateWindowExW(0, "STATIC", "srctl-test", 0, 0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (Handle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastWin32Error()}");
        }
    }

    public IntPtr Handle { get; }

    /// <summary>Removes the queued keyboard messages and returns (message, virtual key) pairs.</summary>
    public List<(uint Message, int Key)> Pump() => Pump(0x0100, 0x0109);

    /// <summary>Keyboard messages plus the R66 activation messages (WM_ACTIVATE, WM_SETFOCUS, WM_ACTIVATEAPP), in queue order.</summary>
    public List<(uint Message, int WParam)> PumpWithActivation() =>
        Pump(0, 0).Where(m => m.Item1 is 0x0006 or 0x0007 or 0x001C or (>= 0x0100 and <= 0x0109)).ToList();

    private List<(uint, int)> Pump(uint min, uint max)
    {
        var result = new List<(uint, int)>();
        while (PeekMessageW(out var msg, Handle, min, max, 1))
        {
            result.Add((msg.message, (int)msg.wParam));
        }

        return result;
    }

    public void Dispose() => DestroyWindow(Handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public int x, y;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessageW(out MSG msg, IntPtr hwnd, uint filterMin, uint filterMax, uint remove);
}
