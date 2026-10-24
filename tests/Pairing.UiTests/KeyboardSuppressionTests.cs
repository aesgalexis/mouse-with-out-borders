using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;

namespace MouseWithOutBorders;

// These tests observe the bottom of the low-level-hook chain. Observing only
// generated destinations cannot detect the original character leaking through.
internal static class KeyboardSuppressionTests
{
    private static readonly IntPtr TestMarker = (IntPtr)0x4B535550;
    private readonly record struct Transition(int Key, bool Down);
    private readonly record struct Input(int Key, bool Down, int VirtualKey = -1, int Scan = -1, bool? Extended = null);
    private readonly record struct Observed(int Key, bool Down, bool Generated);

    internal static void RunNative()
    {
        ConcurrentQueue<string> errors = new();
        // Low-level hooks run newest first: install the sink before production
        // so a failure to consume an original source reaches this observer.
        // Its separate message loop also prevents SendInput/Flush deadlocks.
        using BottomHook sink = new();
        using KeyboardRemappingService service = new(errors.Enqueue);
        int cases = 0;

        foreach (KeyChoice source in KeyCatalog.Choices)
        foreach (int target in new[] { 8, 46, 13, KeyCatalog.NumpadEnter })
        {
            if (source.Code == target) continue;
            Check(service, sink, errors, [new(source.Code, target)],
                [new(source.Code, true), new(source.Code, true), new(source.Code, false)],
                [new(target, true), new(target, true), new(target, false)], [],
                $"{source.Name} -> {KeyCatalog.Name(target)}");
            cases++;
        }

        int[] printable = [32, 48, 57, 65, 90, 186, 187, 188, 189, 190, 191, 192, 219, 220, 221, 222, 223, 226];
        int[] modifiers = [160, 162, 164, 91];
        foreach (int source in printable)
        for (int mask = 0; mask < 16; mask++)
        {
            int[] held = modifiers.Where((_, index) => (mask & (1 << index)) != 0).ToArray();
            foreach (int target in new[] { 8, 46, 13, KeyCatalog.NumpadEnter })
            {
                Input[] sequence = held.Select(key => new Input(key, true))
                    .Concat([new(source, true), new(source, true), new(source, false)])
                    .Concat(held.Reverse().Select(key => new Input(key, false))).ToArray();
                Transition[] passedModifiers = held.Select(key => new Transition(key, true))
                    .Concat(held.Reverse().Select(key => new Transition(key, false))).ToArray();
                Check(service, sink, errors, [new(source, target)], sequence,
                    [new(target, true), new(target, true), new(target, false)], passedModifiers,
                    $"printable {source} -> {target}, modifier mask {mask}");
                cases++;
            }
        }

        // Windows represents AltGr as left Ctrl plus extended right Alt. Both
        // orders occur in injected and physical input streams.
        foreach (int source in printable)
        foreach (int[] altGr in new[] { new[] { 162, 165 }, new[] { 165, 162 } })
        foreach (int target in new[] { 8, 46, 13, KeyCatalog.NumpadEnter })
        {
            Input[] sequence = altGr.Select(key => new Input(key, true))
                .Concat([new(source, true), new(source, false)])
                .Concat(altGr.Reverse().Select(key => new Input(key, false))).ToArray();
            Transition[] passedModifiers = altGr.Select(key => new Transition(key, true))
                .Concat(altGr.Reverse().Select(key => new Transition(key, false))).ToArray();
            Check(service, sink, errors, [new(source, target)], sequence,
                [new(target, true), new(target, false)], passedModifiers,
                $"AltGr {string.Join('+', altGr)} + {source} -> {target}");
            cases++;
        }

        foreach (var modifier in new[]
        {
            (Key: 160, Generic: 16, Scan: 0x2A, Extended: false),
            (Key: 161, Generic: 16, Scan: 0x36, Extended: false),
            (Key: 162, Generic: 17, Scan: 0x1D, Extended: false),
            (Key: 163, Generic: 17, Scan: 0x1D, Extended: true),
            (Key: 164, Generic: 18, Scan: 0x38, Extended: false),
            (Key: 165, Generic: 18, Scan: 0x38, Extended: true)
        })
        {
            Check(service, sink, errors, [new(modifier.Key, 8)],
                [new(modifier.Key, true, modifier.Generic, modifier.Scan, modifier.Extended),
                 new(modifier.Key, false, modifier.Generic, modifier.Scan, modifier.Extended)],
                [new(8, true), new(8, false)], [], $"generic modifier {modifier.Key}");
            cases++;
        }

        // Chains and swaps must consume both original sources without applying
        // another mapping to the already-generated output.
        Check(service, sink, errors, [new(191, 8), new(8, 13)],
            [new(191, true), new(191, false), new(8, true), new(8, false)],
            [new(8, true), new(8, false), new(13, true), new(13, false)], [], "printable chain");
        Check(service, sink, errors, [new(191, 8), new(8, 191)],
            [new(191, true), new(191, false), new(8, true), new(8, false)],
            [new(8, true), new(8, false), new(191, true), new(191, false)], [], "printable swap");

        service.Configure(false, []);
        Complete(service.FlushAsync(), "Disabling keyboard remapping timed out.");
        sink.Clear();
        Send([new(191, true), new(191, false)]);
        WaitFor(() => sink.Count >= 2, "Disabled source did not reach the test sink.");
        Assert(sink.Snapshot().SequenceEqual(new[] { new Observed(191, true, false), new Observed(191, false, false) }),
            "Disabled remapping still generated or suppressed input.");

        CheckInjectionFailures(sink);
        CheckMessageDecoding();
        Assert(errors.IsEmpty, "Keyboard worker failed: " + string.Join("; ", errors));
        Console.WriteLine($"PASS: {cases + 3} native source-suppression cases cover every selectable source, editing targets, repeats, printable/OEM modifier combinations, AltGr, generic modifiers, swaps/chains and disabled passthrough. All test input was intercepted.");
    }

    private static void CheckInjectionFailures(BottomHook sink)
    {
        foreach (bool failOnDown in new[] { true, false })
        {
            ConcurrentQueue<string> errors = new();
            ConcurrentQueue<Transition> attempted = new();
            int failuresRemaining = 1;
            using KeyboardRemappingService service = new(errors.Enqueue, (key, down) =>
            {
                attempted.Enqueue(new(key, down));
                if (down == failOnDown && Interlocked.Exchange(ref failuresRemaining, 0) == 1)
                    throw new Win32Exception(5, "Deliberately rejected test-only keyboard output.");
            });
            service.Configure(true, [new(191, 8)]);
            Complete(service.FlushAsync(), "Fault-injection keyboard configure timed out.");
            sink.Clear();
            Send([new(191, true), new(191, true), new(191, false)]);
            WaitFor(() => errors.Count == 1 && attempted.Contains(new(8, false)),
                "Rejected output did not report and release the remapped key.");
            Complete(service.FlushAsync(), "Fault-injection keyboard flush timed out.");
            Assert(sink.Count == 0, "Rejected output let the original printable source/repeat/up through: " +
                string.Join(", ", sink.Snapshot()));
            Assert(attempted.Last() == new Transition(8, false), "Rejected output left its generated destination held.");

            // Fault recovery must not keep swallowing the next independent key
            // press after the failed gesture's original key-up was consumed.
            while (attempted.TryDequeue(out _)) { }
            service.Configure(true, [new(191, 8)]);
            Complete(service.FlushAsync(), "Fault recovery configure timed out.");
            Send([new(191, true), new(191, false)]);
            WaitFor(() => attempted.Count >= 2, "Keyboard remapping did not recover after rejected output.");
            Complete(service.FlushAsync(), "Fault recovery flush timed out.");
            Assert(attempted.SequenceEqual(new[] { new Transition(8, true), new Transition(8, false) }) && sink.Count == 0,
                "Fault recovery duplicated or leaked the original source.");
            Assert(errors.Count == 1, "A single rejected output produced repeated worker failures.");
        }
        Console.WriteLine("PASS: rejected destination down/up remains suppressed, releases held output and recovers on a fresh press.");
    }

    private static void Check(KeyboardRemappingService service, BottomHook sink, ConcurrentQueue<string> errors,
        KeyMapping[] mappings, Input[] input, Transition[] generated, Transition[] passed, string context)
    {
        service.Configure(true, mappings);
        Complete(service.FlushAsync(), "Keyboard configure timed out: " + context);
        sink.Clear();
        Send(input);
        WaitFor(() => sink.Count >= generated.Length + passed.Length || !errors.IsEmpty,
            "Missing native input: " + context + "; observed=" + string.Join(", ", sink.Snapshot()));
        Complete(service.FlushAsync(), "Keyboard flush timed out: " + context);
        Observed[] actual = sink.Snapshot();
        Assert(errors.IsEmpty, context + ": " + string.Join("; ", errors));
        Assert(actual.Where(item => item.Generated).Select(item => new Transition(item.Key, item.Down)).SequenceEqual(generated),
            context + ": generated input differed; observed=" + string.Join(", ", actual));
        Assert(actual.Where(item => !item.Generated).Select(item => new Transition(item.Key, item.Down)).SequenceEqual(passed),
            context + ": an original source leaked or a modifier was lost; observed=" + string.Join(", ", actual));
    }

    private static void Send(Input[] transitions)
    {
        NativeMouse.Input[] input = transitions.Select(transition =>
        {
            int virtualKey = transition.VirtualKey < 0 ? transition.Key & 255 : transition.VirtualKey;
            uint scan = transition.Scan < 0 ? MapVirtualKey((uint)virtualKey, 4) & 255 : (uint)transition.Scan;
            return new NativeMouse.Input
            {
                Type = 1,
                Data = new NativeMouse.InputUnion { Keyboard = new NativeMouse.KeyboardInput
                {
                    Key = (ushort)virtualKey, Scan = (ushort)scan,
                    Flags = (transition.Down ? 0u : 2u) |
                        ((transition.Extended ?? KeyCatalog.Extended(transition.Key)) ? 1u : 0u),
                    ExtraInfo = TestMarker
                } }
            };
        }).ToArray();
        if (SendInput((uint)input.Length, input, Marshal.SizeOf<NativeMouse.Input>()) != input.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows rejected safe keyboard regression input.");
    }

    private static void CheckMessageDecoding()
    {
        bool decoding = false;
        List<Transition> observed = [];
        using NativeKeyboard keyboard = new((key, down) =>
        {
            if (!decoding) return false;
            observed.Add(new(key, down));
            return true;
        });
        MethodInfo callback = typeof(NativeKeyboard).GetMethod("OnHook", BindingFlags.Instance | BindingFlags.NonPublic)!;
        IntPtr pointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeKeyboard.HookInfo>());
        try
        {
            foreach (int source in new[] { 191, 13, KeyCatalog.NumpadEnter })
            foreach (int message in new[] { 0x100, 0x101, 0x104, 0x105 })
            {
                bool down = message is 0x100 or 0x104;
                Marshal.StructureToPtr(new NativeKeyboard.HookInfo
                {
                    Key = (uint)(source & 255), Scan = 0x1C,
                    Flags = (down ? 0u : 0x80u) | (source == KeyCatalog.NumpadEnter ? 1u : 0u),
                    ExtraInfo = TestMarker
                }, pointer, false);
                decoding = true;
                IntPtr result;
                try { result = (IntPtr)callback.Invoke(keyboard, [0, (IntPtr)message, pointer])!; }
                finally { decoding = false; }
                Assert(result == (IntPtr)1 && observed.Last() == new Transition(source, down),
                    $"Native callback did not consume/decode source {source}, message {message:X}.");
            }
        }
        finally { Marshal.FreeHGlobal(pointer); }
        Console.WriteLine("PASS: regular/system key messages and extended Enter decode and suppress consistently.");
    }

    private static void Complete(Task task, string message)
    {
        WaitFor(() => task.IsCompleted, message);
        task.GetAwaiter().GetResult();
    }

    private static void WaitFor(Func<bool> completed, string message)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(3);
        while (!completed() && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(1); }
        Assert(completed(), message);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private sealed class BottomHook : IDisposable
    {
        private readonly ConcurrentQueue<Observed> _events = new();
        private readonly Thread _thread;
        private Control? _dispatch;
        private bool _disposed;
        internal int Count => _events.Count;
        internal Observed[] Snapshot() => _events.ToArray();
        internal void Clear() { while (_events.TryDequeue(out _)) { } }

        internal BottomHook()
        {
            TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _thread = new(() =>
            {
                NativeMouse.HookProcedure callback = (code, message, pointer) =>
                {
                    if (code >= 0)
                    {
                        NativeKeyboard.HookInfo key = Marshal.PtrToStructure<NativeKeyboard.HookInfo>(pointer);
                        if (key.ExtraInfo == TestMarker || key.ExtraInfo == NativeKeyboard.Marker)
                        {
                            int source = key.Key == 13 && (key.Flags & 1) != 0 ? KeyCatalog.NumpadEnter : (int)key.Key;
                            _events.Enqueue(new(source, (key.Flags & 0x80) == 0, key.ExtraInfo == NativeKeyboard.Marker));
                            return (IntPtr)1;
                        }
                    }
                    return NativeMouse.CallNextHookEx(IntPtr.Zero, code, message, pointer);
                };
                IntPtr hook = IntPtr.Zero;
                try
                {
                    using Control dispatch = new();
                    _ = dispatch.Handle;
                    _dispatch = dispatch;
                    hook = NativeMouse.SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
                    if (hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot install the safe keyboard test sink.");
                    ready.SetResult();
                    Application.Run();
                }
                catch (Exception error) { ready.TrySetException(error); }
                finally
                {
                    if (hook != IntPtr.Zero) NativeMouse.UnhookWindowsHookEx(hook);
                    GC.KeepAlive(callback);
                }
            }) { IsBackground = true, Name = "MWB keyboard regression sink" };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            Complete(ready.Task, "Safe keyboard test sink did not start.");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_dispatch is not null && !_dispatch.IsDisposed && _thread.IsAlive)
                _dispatch.BeginInvoke((System.Windows.Forms.MethodInvoker)Application.ExitThread);
            Assert(_thread.Join(TimeSpan.FromSeconds(3)), "Safe keyboard test sink did not stop.");
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, [In] NativeMouse.Input[] input, int size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint MapVirtualKey(uint key, uint type);
}
