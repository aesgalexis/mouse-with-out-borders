using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MouseWithOutBorders;

internal static class NativeMouse
{
    internal const int WM_INPUT = 0x00FF;
    internal const uint WM_MOUSEMOVE = 0x0200;
    internal const uint WM_LBUTTONDOWN = 0x0201;
    internal const uint WM_LBUTTONUP = 0x0202;
    internal const uint WM_RBUTTONDOWN = 0x0204;
    internal const uint WM_RBUTTONUP = 0x0205;
    internal const uint WM_MBUTTONDOWN = 0x0207;
    internal const uint WM_MBUTTONUP = 0x0208;
    internal const uint WM_MOUSEWHEEL = 0x020A;
    internal const uint WM_XBUTTONDOWN = 0x020B;
    internal const uint WM_XBUTTONUP = 0x020C;
    internal const uint WM_MOUSEHWHEEL = 0x020E;

    internal const uint LEFTDOWN = 0x0002;
    internal const uint LEFTUP = 0x0004;
    internal const uint RIGHTDOWN = 0x0008;
    internal const uint RIGHTUP = 0x0010;
    internal const uint MIDDLEDOWN = 0x0020;
    internal const uint MIDDLEUP = 0x0040;
    internal const uint XDOWN = 0x0080;
    internal const uint XUP = 0x0100;
    internal const uint WHEEL = 0x0800;
    internal const uint HWHEEL = 0x1000;

    private const int WH_MOUSE_LL = 14;
    private const uint RIDEV_INPUTSINK = 0x00000100;
    private const uint RID_INPUT = 0x10000003;
    private const uint LLMHF_INJECTED = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawHeader
    {
        public uint Type;
        public uint Size;
        public IntPtr Device;
        public IntPtr WParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HookInfo
    {
        public Point Position;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    // INPUT contains a union. Its natural alignment makes the union start at
    // offset 8 on x64 and offset 4 on x86.
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx, Dy;
        public uint MouseData, Flags, Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    internal delegate IntPtr HookProcedure(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices([In] RawDevice[] devices, uint count, uint size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(IntPtr handle, uint command, [Out] byte[] data, ref uint size, uint headerSize);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetWindowsHookEx(int hookId, HookProcedure procedure, IntPtr module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ClipCursor(ref Rect rect);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "ClipCursor")]
    private static extern bool ReleaseClip(IntPtr rect);

    [DllImport("user32.dll")]
    internal static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    internal static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, [In] Input[] input, int size);

    internal static bool IsInjected(HookInfo info) => (info.Flags & LLMHF_INJECTED) != 0;

    internal static IntPtr InstallHook(HookProcedure callback)
    {
        IntPtr handle = SetWindowsHookEx(WH_MOUSE_LL, callback, GetModuleHandle(null), 0);
        if (handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot install mouse hook");
        return handle;
    }

    internal static void RegisterMouse(IntPtr window)
    {
        RawDevice[] devices = [new() { UsagePage = 1, Usage = 2, Flags = RIDEV_INPUTSINK, Target = window }];
        if (!RegisterRawInputDevices(devices, 1, (uint)Marshal.SizeOf<RawDevice>()))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot register raw mouse input");
    }

    internal static (int dx, int dy)? ReadRelativeDelta(IntPtr handle)
    {
        // A RAWINPUT header is 24 bytes on x64 and 16 on x86.
        int header = Marshal.SizeOf<RawHeader>();
        byte[] bytes = new byte[128];
        uint length = (uint)bytes.Length;
        uint read = GetRawInputData(handle, RID_INPUT, bytes, ref length, (uint)header);
        if (read == uint.MaxValue || read < header + 20 || BitConverter.ToUInt32(bytes, 0) != 0)
            return null;
        ushort mouseFlags = BitConverter.ToUInt16(bytes, header);
        if ((mouseFlags & 1) != 0) return null; // absolute movement isn't a relative mouse delta
        return (BitConverter.ToInt32(bytes, header + 12), BitConverter.ToInt32(bytes, header + 16));
    }

    internal static void HoldCursorAt(int x, int y)
    {
        Rect rect = new() { Left = x, Top = y, Right = x + 1, Bottom = y + 1 };
        if (!ClipCursor(ref rect))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot hold local cursor");
    }

    internal static void ReleaseCursor() => ReleaseClip(IntPtr.Zero);

    internal static void Inject(uint flags, int data = 0)
    {
        Input[] input = [new()
        {
            Type = 0,
            Data = new InputUnion
            {
                Mouse = new MouseInput { Flags = flags, MouseData = unchecked((uint)data) }
            }
        }];
        _ = SendInput(1, input, Marshal.SizeOf<Input>());
    }
}

internal sealed class MouseCapture : IDisposable
{
    private readonly NativeMouse.HookProcedure _callback;
    private readonly Func<uint, NativeMouse.HookInfo, bool> _handler;
    private IntPtr _hook;
    private bool _disposed;

    internal MouseCapture(IntPtr window, Func<uint, NativeMouse.HookInfo, bool> handler)
    {
        _handler = handler;
        _callback = OnHook; // keep delegate alive while hook is installed
        NativeMouse.RegisterMouse(window);
        _hook = NativeMouse.InstallHook(_callback);
    }

    private IntPtr OnHook(int code, IntPtr wp, IntPtr lp)
    {
        if (code >= 0 && !_disposed)
        {
            var info = Marshal.PtrToStructure<NativeMouse.HookInfo>(lp);
            if (!NativeMouse.IsInjected(info) && _handler(unchecked((uint)wp.ToInt64()), info))
                return (IntPtr)1;
        }
        return NativeMouse.CallNextHookEx(_hook, code, wp, lp);
    }

    internal void Dispose(bool releasingCursor)
    {
        if (_disposed) return;
        _disposed = true;
        if (_hook != IntPtr.Zero)
        {
            NativeMouse.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
        if (releasingCursor) NativeMouse.ReleaseCursor();
    }

    public void Dispose() => Dispose(true);
}
