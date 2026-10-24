using System.Runtime.InteropServices;

namespace MouseWithOutBorders;

internal static class CursorReturnUiTests
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, NativeMouse.Input[] input, int size);

    internal static void RunNative()
    {
        if (!NativeMouse.GetCursorPos(out var original)) throw new Exception("Cannot read cursor position.");
        Point start = new(original.X, original.Y);
        Rectangle bounds = SystemInformation.VirtualScreen;
        Rectangle monitor = Screen.FromPoint(start).Bounds;
        Point anchor = new(monitor.Left + monitor.Width / 2, monitor.Top + monitor.Height / 2);
        IntPtr physicalMarker = (IntPtr)0x54455354;
        try
        {
            foreach (bool right in new[] { true, false })
            {
                CursorMotionGuard motion = new();
                List<NativeMouse.Input> inputs = [];
                uint Capture(uint _, NativeMouse.Input[] input, int __) { inputs.Add(input[0]); return 1; }
                motion.MoveCursor(anchor, bounds, Capture);
                NativeMouse.Input stalePark = inputs[0];
                motion.BeginTransition();
                Point edge = new(right ? monitor.Right - 26 : monitor.Left + 25, monitor.Top + monitor.Height / 3);
                motion.MoveCursor(edge, bounds, Capture);
                NativeMouse.Input restore = inputs[1];
                NativeMouse.Input stalePhysical = stalePark;
                stalePhysical.Data.Mouse.ExtraInfo = physicalMarker;
                // The former hook passed all MWB-tagged input through. Verify
                // that this event ordering reproduces the reported centre jump.
                using (MouseCapture unguarded = new((message, _) => message == NativeMouse.WM_MOUSEMOVE))
                {
                    NativeMouse.Input[] oldSequence = [restore, stalePark];
                    if (SendInput((uint)oldSequence.Length, oldSequence, Marshal.SizeOf<NativeMouse.Input>()) != oldSequence.Length)
                        throw new Exception("Could not replay the unguarded cursor return.");
                    Application.DoEvents();
                    if (!NativeMouse.GetCursorPos(out var oldPosition) || oldPosition.X != anchor.X || oldPosition.Y != anchor.Y)
                        throw new Exception("The unguarded sequence did not reproduce the central parking jump.");
                }
                int suppressedPhysical = 0, suppressedParking = 0;
                List<string> trace = [];
                using MouseCapture capture = new((message, info) =>
                {
                    if (message != NativeMouse.WM_MOUSEMOVE) return false;
                    if (trace.Count < 20) trace.Add($"physical {info.ExtraInfo.ToInt64():X} {info.Position.X},{info.Position.Y} pending={motion.PlacementPending}");
                    if (info.ExtraInfo == physicalMarker && motion.PlacementPending) suppressedPhysical++;
                    return motion.PlacementPending || info.ExtraInfo != physicalMarker;
                }, (message, info) =>
                {
                    if (message != NativeMouse.WM_MOUSEMOVE) return false;
                    bool suppress = motion.SuppressInjectedMove(info);
                    if (trace.Count < 20) trace.Add($"injected {info.ExtraInfo.ToInt64():X} {info.Position.X},{info.Position.Y} suppress={suppress}");
                    if (suppress) suppressedParking++;
                    return suppress;
                });
                // Replay native events around a return, including an old centre
                // placement delivered after the new edge placement.
                NativeMouse.Input[] sequence = [stalePark, stalePhysical, restore, stalePark];
                if (SendInput((uint)sequence.Length, sequence, Marshal.SizeOf<NativeMouse.Input>()) != sequence.Length)
                    throw new Exception("Could not queue the native return regression sequence.");
                DateTime deadline = DateTime.UtcNow.AddSeconds(2);
                while ((motion.PlacementPending || suppressedParking < 2) && DateTime.UtcNow < deadline)
                { Application.DoEvents(); Thread.Sleep(5); }
                bool read = NativeMouse.GetCursorPos(out var returned);
                if (motion.PlacementPending || suppressedPhysical != 1 || suppressedParking != 2 ||
                    !read || returned.X != edge.X || returned.Y != edge.Y)
                    throw new Exception($"Delayed parking/input moved the returning cursor away from the shared edge: " +
                        $"expected={edge}, actual={returned.X},{returned.Y}, pending={motion.PlacementPending}, " +
                        $"physical={suppressedPhysical}, parks={suppressedParking}, trace={string.Join("; ", trace)}");

                Point next = new(edge.X + (right ? -2 : 2), edge.Y + 2);
                NativeMouse.MoveCursor(next, bounds, (count, input, size) =>
                {
                    input[0].Data.Mouse.ExtraInfo = physicalMarker;
                    return SendInput(count, input, size);
                });
                Application.DoEvents();
                if (!NativeMouse.GetCursorPos(out var moved) || moved.X != next.X || moved.Y != next.Y)
                    throw new Exception("Local mouse movement did not resume after the edge placement.");
            }
        }
        finally { NativeMouse.MoveCursor(start, bounds); Application.DoEvents(); }
        Console.WriteLine("PASS: unguarded input reproduces the centre jump; guarded native returns keep both edges, drop stale parking/input and resume local movement; no buttons injected.");
    }
}
