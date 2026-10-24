using System.ComponentModel;
using System.Drawing;

namespace MouseWithOutBorders;

internal static class CursorMotionGuardTests
{
    internal static void Run()
    {
        TestMissingAcknowledgement();
        TestLateAcknowledgementAfterTimeout();
        TestTransitionAndMarkerNormalization();
        TestFailedPlacement();
        Console.WriteLine("PASS: missing cursor acknowledgements expire, repeated moves retain their deadline, current markers survive 32-bit bridges and transitions reject obsolete placements.");
    }

    private static void TestMissingAcknowledgement()
    {
        long now = 1000;
        CursorMotionGuard motion = new(() => now);
        List<NativeMouse.HookInfo> inputs = [];
        uint Capture(uint _, NativeMouse.Input[] input, int __)
        {
            inputs.Add(new NativeMouse.HookInfo { ExtraInfo = input[0].Data.Mouse.ExtraInfo });
            return 1;
        }

        motion.MoveCursor(new Point(500, 400), new Rectangle(0, 0, 1920, 1080), Capture);
        Assert(motion.PlacementPending, "The first placement must wait for Windows before physical input resumes.");
        now += CursorMotionGuard.PlacementTimeoutMilliseconds - 1;
        Assert(motion.PlacementPending, "The first placement expired before its acknowledgement deadline.");
        motion.MoveCursor(new Point(520, 410), new Rectangle(0, 0, 1920, 1080), Capture);
        now++;
        Assert(!motion.PlacementPending, "An absent acknowledgement left physical movement suppressed indefinitely.");
        Assert(motion.SuppressInjectedMove(inputs[1]) && motion.SuppressInjectedMove(inputs[0]),
            "Expired placements must not overwrite physical movement after its guard has recovered.");

        motion.MoveCursor(new Point(10, 200), new Rectangle(0, 0, 1920, 1080), Capture);
        Assert(motion.PlacementPending, "A new ownership must receive its own acknowledgement deadline.");
        Assert(motion.SuppressInjectedMove(inputs[0]) && motion.PlacementPending,
            "An old acknowledgement released the new ownership's placement guard.");
        Assert(!motion.SuppressInjectedMove(inputs[2]) && !motion.PlacementPending,
            "A valid placement did not release the new ownership's placement guard.");
    }

    private static void TestLateAcknowledgementAfterTimeout()
    {
        long now = 0;
        CursorMotionGuard motion = new(() => now);
        List<NativeMouse.HookInfo> inputs = [];
        uint Capture(uint _, NativeMouse.Input[] input, int __)
        {
            inputs.Add(new NativeMouse.HookInfo { ExtraInfo = input[0].Data.Mouse.ExtraInfo });
            return 1;
        }
        Rectangle bounds = new(0, 0, 1920, 1080);
        motion.MoveCursor(new Point(960, 540), bounds, Capture);
        now += CursorMotionGuard.PlacementTimeoutMilliseconds;
        // The late callback can arrive before any timer/physical input has read
        // PlacementPending. It must enforce the same deadline itself.
        Assert(motion.SuppressInjectedMove(inputs[0]) && !motion.PlacementPending,
            "A late acknowledgement revived an expired central parking placement.");
        motion.MoveCursor(new Point(30, 400), bounds, Capture);
        Assert(motion.PlacementPending && motion.SuppressInjectedMove(inputs[0]),
            "A late parking acknowledgement escaped after a new placement started.");
        Assert(!motion.SuppressInjectedMove(inputs[1]) && !motion.PlacementPending,
            "A recovered placement did not accept its own acknowledgement.");

        motion.BeginTransition();
        motion.MoveCursor(new Point(960, 540), bounds, Capture);
        now += CursorMotionGuard.PlacementTimeoutMilliseconds;
        motion.MoveCursor(new Point(30, 400), bounds, Capture);
        Assert(motion.SuppressInjectedMove(inputs[2]) && motion.PlacementPending,
            "A replacement placement reused a timed-out parking tag.");
        Assert(!motion.SuppressInjectedMove(inputs[3]) && !motion.PlacementPending,
            "A replacement placement failed to establish a fresh acknowledgement deadline.");
    }

    private static void TestTransitionAndMarkerNormalization()
    {
        CursorMotionGuard motion = new(() => 0);
        NativeMouse.HookInfo current = default;
        uint Capture(uint _, NativeMouse.Input[] input, int __)
        {
            current = new NativeMouse.HookInfo { ExtraInfo = input[0].Data.Mouse.ExtraInfo };
            return 1;
        }
        Rectangle bounds = new(0, 0, 1920, 1080);
        motion.MoveCursor(new Point(500, 400), bounds, Capture);
        NativeMouse.HookInfo stale = current;
        motion.BeginTransition();
        Assert(!motion.PlacementPending && motion.SuppressInjectedMove(stale),
            "A transition without placement must immediately release physical input and reject old motion.");
        motion.MoveCursor(new Point(30, 400), bounds, Capture);
        NativeMouse.HookInfo ordinary = new() { ExtraInfo = NativeMouse.InputMarker };
        if (IntPtr.Size == 8)
        {
            current.ExtraInfo = (IntPtr)unchecked((long)(0xFFFFFFFF00000000UL | (uint)current.ExtraInfo.ToInt64()));
            stale.ExtraInfo = (IntPtr)unchecked((long)(0xFFFFFFFF00000000UL | (uint)stale.ExtraInfo.ToInt64()));
            ordinary.ExtraInfo = (IntPtr)unchecked((long)(0xFFFFFFFF00000000UL | (uint)ordinary.ExtraInfo.ToInt64()));
        }
        Assert(!motion.SuppressInjectedMove(ordinary) && motion.PlacementPending,
            "An ordinary injected event must pass without acknowledging a pending placement.");
        Assert(motion.SuppressInjectedMove(stale) && motion.PlacementPending,
            "Normalizing marker width let an obsolete placement acknowledge current ownership.");
        Assert(!motion.SuppressInjectedMove(current) && !motion.PlacementPending,
            "A bridge that retains the low 32 marker bits must acknowledge the current placement.");
    }

    private static void TestFailedPlacement()
    {
        CursorMotionGuard motion = new(() => 0);
        try
        {
            motion.MoveCursor(new Point(50, 40), new Rectangle(0, 0, 100, 100), (_, _, _) =>
                throw new Win32Exception(5, "Simulated input rejection."));
            throw new Exception("A failed placement did not report its input error.");
        }
        catch (Win32Exception)
        {
            Assert(!motion.PlacementPending, "A rejected placement left physical input suppressed.");
        }
        NativeMouse.HookInfo retry = default;
        motion.MoveCursor(new Point(50, 40), new Rectangle(0, 0, 100, 100), (_, input, _) =>
        {
            retry = new NativeMouse.HookInfo { ExtraInfo = input[0].Data.Mouse.ExtraInfo };
            return 1;
        });
        Assert(motion.PlacementPending && !motion.SuppressInjectedMove(retry) && !motion.PlacementPending,
            "A successful retry after input rejection did not get its own placement acknowledgement.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
