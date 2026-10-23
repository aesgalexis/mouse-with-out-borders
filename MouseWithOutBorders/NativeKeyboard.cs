using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MouseWithOutBorders;

internal sealed class NativeKeyboard : IDisposable
{
    internal static readonly IntPtr Marker = (IntPtr)0x4D57424B;
    [StructLayout(LayoutKind.Sequential)]
    internal struct HookInfo
    {
        public uint Key, Scan, Flags, Time;
        public IntPtr ExtraInfo;
    }
    private readonly NativeMouse.HookProcedure _callback;
    private readonly Func<int, bool, bool> _handler;
    private IntPtr _hook;
    internal NativeKeyboard(Func<int, bool, bool> handler)
    {
        _handler = handler;
        _callback = OnHook;
        _hook = NativeMouse.SetWindowsHookEx(13, _callback, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot install the keyboard remapping hook.");
    }
    private IntPtr OnHook(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            HookInfo key = Marshal.PtrToStructure<HookInfo>(data);
            if (key.ExtraInfo != Marker && message.ToInt64() is 0x100 or 0x101 or 0x104 or 0x105)
            {
                int source = key.Key == 13 && (key.Flags & 1) != 0 ? KeyCatalog.NumpadEnter : (int)key.Key;
                if (_handler(source, (key.Flags & 0x80) == 0)) return (IntPtr)1;
            }
        }
        return NativeMouse.CallNextHookEx(_hook, code, message, data);
    }
    internal static void Inject(int key, bool down) => Inject(key, down, SendInput);
    internal static KeyChoice Describe(KeyChoice key)
    {
        if (key.Code < 186) return key;
        uint scan = MapVirtualKey((uint)key.Code, 0);
        System.Text.StringBuilder name = new(128);
        return scan != 0 && GetKeyNameText((int)(scan << 16), name, name.Capacity) > 0
            ? key with { Name = $"{name} · OEM {key.Code}" } : key;
    }
    internal static void Inject(int key, bool down, NativeMouse.InputSender sender)
    {
        NativeMouse.Input[] input = [new()
        {
            Type = 1,
            Data = new NativeMouse.InputUnion { Keyboard = new NativeMouse.KeyboardInput
            {
                Key = (ushort)(key & 255), Flags = (down ? 0u : 2u) | (KeyCatalog.Extended(key) ? 1u : 0u), ExtraInfo = Marker
            } }
        }];
        if (sender(1, input, Marshal.SizeOf<NativeMouse.Input>()) != 1)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows rejected keyboard input. Elevated apps require MWB to run at the same privilege level.");
    }
    public void Dispose()
    {
        if (_hook != IntPtr.Zero) { NativeMouse.UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, [In] NativeMouse.Input[] input, int size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint MapVirtualKey(uint key, uint type);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetKeyNameText(int data, System.Text.StringBuilder name, int size);
}
