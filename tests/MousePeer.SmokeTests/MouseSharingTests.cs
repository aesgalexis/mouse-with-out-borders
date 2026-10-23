using System.ComponentModel;
using System.Drawing;

namespace MouseWithOutBorders;

internal static class MouseSharingTests
{
    internal static void Run()
    {
        TestNoPhantomClicks();
        TestBothDirections(true);
        TestBothDirections(false);
        TestLocalTakeover();
        TestSimultaneousCrossings();
        TestHeldButtons();
        TestStalePackets();
        TestPointerRestoration();
        TestPointerInput();
        Console.WriteLine("PASS: no phantom releases, both mice/directions, takeover, crossing races, stale packets and pointer input.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private sealed class Desktop : IMouseDesktop
    {
        public Rectangle Bounds { get; } = new(-500, 0, 1000, 700);
        public Point Cursor { get; set; } = new(0, 300);
        internal bool PointerHidden;
        public void SetPointerHidden(bool hidden)
        {
            PointerHidden = hidden;
            if (hidden) Cursor = new Point(Bounds.Left + Bounds.Width / 2, Bounds.Top + Bounds.Height / 2);
        }
        internal readonly List<(uint flags, int data)> Injected = [];
        public void MoveCursor(Point point) => Cursor = point;
        public void Inject(uint flags, int data) => Injected.Add((flags, data));
    }

    private sealed class Pair : IDisposable
    {
        internal readonly Desktop[] Desktops = [new(), new()];
        internal readonly MouseSharingSession[] Sessions;
        internal readonly Queue<(int from, MousePacket packet)> Packets = new();
        internal readonly List<(int from, MousePacket packet)> Sent = [];

        internal Pair(bool right = true)
        {
            Sessions = [Create(0, right), Create(1, right)];
            Sessions[0].Start();
            Sessions[1].Start();
            Flush();
        }

        private MouseSharingSession Create(int side, bool right) => new(side == 0, right, Desktops[side], packet =>
        {
            Packets.Enqueue((side, packet));
            Sent.Add((side, packet));
            return true;
        }, ex => throw new Exception("Unexpected input error.", ex), _ => { });

        internal void Flush()
        {
            int limit = 1000;
            while (Packets.TryDequeue(out var entry))
            {
                if (--limit == 0) throw new Exception("Mouse packets are feeding back into the connection.");
                Sessions[1 - entry.from].Receive(entry.packet);
            }
        }

        internal void Move(int side, int dx, int dy = 0, bool flush = true)
        {
            Desktop desktop = Desktops[side];
            Point previous = desktop.Cursor;
            Point next = new(Math.Clamp(previous.X + dx, desktop.Bounds.Left, desktop.Bounds.Right - 1),
                Math.Clamp(previous.Y + dy, desktop.Bounds.Top, desktop.Bounds.Bottom - 1));
            if (!Sessions[side].PhysicalMove(previous, next)) desktop.Cursor = next;
            if (flush) Flush();
        }

        internal void Cross(int side, bool flush = true)
        {
            Desktop desktop = Desktops[side];
            desktop.Cursor = new(Sessions[side].ExitRight ? desktop.Bounds.Right - 2 : desktop.Bounds.Left + 1, 300);
            Move(side, Sessions[side].ExitRight ? 4 : -4, flush: flush);
        }

        internal void Button(int side, uint flags, int data = 0)
        {
            Sessions[side].PhysicalButton(flags, data);
            Flush();
        }

        public void Dispose()
        {
            foreach (MouseSharingSession session in Sessions) session.Dispose();
        }
    }

    private static void TestNoPhantomClicks()
    {
        List<(uint, int)> events = [];
        RemoteMouseButtons buttons = new();
        buttons.Release((flags, data) => events.Add((flags, data)), _ => { });
        buttons.Apply(NativeMouse.RIGHTUP, 0, (flags, data) => events.Add((flags, data)));
        buttons.Apply(NativeMouse.XDOWN, 4, (flags, data) => events.Add((flags, data)));
        Assert(events.Count == 0, "Cleanup or an unmatched button-up generated a phantom click.");
        buttons.Apply(NativeMouse.RIGHTDOWN, 0, (flags, data) => events.Add((flags, data)));
        buttons.Apply(NativeMouse.RIGHTUP, 0, (flags, data) => events.Add((flags, data)));
        buttons.Release((flags, data) => events.Add((flags, data)), _ => { });
        Assert(events.SequenceEqual(new[] { (NativeMouse.RIGHTDOWN, 0), (NativeMouse.RIGHTUP, 0) }),
            "A real right click was duplicated by cleanup.");

        using Pair pair = new();
        for (int round = 0; round < 30; round++)
        {
            pair.Cross(0);
            pair.Move(0, -100);
            Assert(!pair.Sessions[0].IsRemote && !pair.Sessions[1].IsReceiving, "Mouse did not return.");
        }
        Assert(pair.Desktops.All(desktop => desktop.Injected.Count == 0), "Crossing without clicks injected button events.");
    }

    private static void TestBothDirections(bool right)
    {
        using Pair pair = new(right);
        Assert(pair.Sessions[0].Ready && pair.Sessions[1].Ready && pair.Sessions[1].ExitRight != right,
            "Both PCs must share the same physical screen layout.");
        for (int side = 0; side < 2; side++)
        {
            pair.Cross(side);
            Assert(pair.Desktops[side].PointerHidden && !pair.Desktops[1 - side].PointerHidden,
                "The departed PC must hide its pointer while the destination stays visible.");
            Assert(pair.Sessions[side].IsRemote && pair.Sessions[1 - side].IsReceiving,
                "A local mouse could not cross from one of the two PCs.");
            int direction = pair.Sessions[side].ExitRight ? 1 : -1;
            Point before = pair.Desktops[1 - side].Cursor;
            pair.Move(side, direction * 30, 17);
            Assert(pair.Desktops[1 - side].Cursor == new Point(before.X + direction * 30, before.Y + 17),
                "Remote movement has the wrong direction or scale.");
            pair.Button(side, NativeMouse.LEFTDOWN);
            pair.Button(side, NativeMouse.LEFTUP);
            pair.Sessions[side].PhysicalWheel(NativeMouse.WHEEL, -120);
            pair.Flush();
            Assert(pair.Desktops[1 - side].Injected.TakeLast(3).SequenceEqual(new[] {
                (NativeMouse.LEFTDOWN, 0), (NativeMouse.LEFTUP, 0), (NativeMouse.WHEEL, -120) }),
                "Clicks or scrolling did not work in both directions.");
            pair.Move(side, -direction * 150);
            Assert(!pair.Sessions[side].IsRemote && !pair.Sessions[1 - side].IsReceiving,
                "The mouse could not return from one of the two PCs.");
            Assert(!pair.Desktops[side].PointerHidden && pair.Desktops[1 - side].PointerHidden,
                "Returning must reveal the source and hide the cursor on the PC just left.");
        }
        for (int side = 0; side < 2; side++)
        {
            Rectangle area = pair.Desktops[side].Bounds;
            pair.Desktops[side].Cursor = new Point(pair.Sessions[side].ExitRight ? area.Right - 1 : area.Left, 300);
            pair.Move(side, 0);
            Assert(pair.Sessions[side].IsRemote, "Pushing a mouse already at the shared edge did not cross.");
            int direction = pair.Sessions[side].ExitRight ? 1 : -1;
            pair.Move(side, direction * 20);
            pair.Button(side, NativeMouse.XDOWN, 3);
            pair.Button(side, NativeMouse.XUP, 3);
            Assert(pair.Desktops[1 - side].Injected.TakeLast(4).SequenceEqual(new[] {
                (NativeMouse.XDOWN, 1), (NativeMouse.XDOWN, 2), (NativeMouse.XUP, 1), (NativeMouse.XUP, 2) }),
                "Combined thumb-button events did not work.");
            pair.Move(side, -direction * 100);
        }
    }

    private static void TestLocalTakeover()
    {
        using Pair pair = new();
        pair.Cross(0);
        pair.Move(1, 30);
        Assert(!pair.Sessions[0].IsRemote && !pair.Sessions[1].IsReceiving,
            "Moving the receiver's physical mouse did not restore local control.");
        Assert(pair.Desktops.All(desktop => !desktop.PointerHidden), "Local takeover did not restore the source pointer.");
        pair.Cross(1);
        Assert(pair.Sessions[1].IsRemote && pair.Sessions[0].IsReceiving,
            "The second PC's mouse could not cross after taking control.");
        pair.Button(0, NativeMouse.LEFTDOWN);
        pair.Button(0, NativeMouse.LEFTUP);
        Assert(!pair.Sessions[1].IsRemote && !pair.Sessions[0].IsReceiving,
            "A local click did not reclaim the PC from remote control.");
    }

    private static void TestSimultaneousCrossings()
    {
        using Pair pair = new();
        pair.Cross(0, flush: false);
        pair.Cross(1, flush: false);
        pair.Flush();
        Assert(pair.Sessions.Count(session => session.IsRemote) == 1 &&
            pair.Sessions.Count(session => session.IsReceiving) == 1,
            "Simultaneous crossings locked both PCs or lost both mice.");
        Assert(pair.Desktops.Count(desktop => desktop.PointerHidden) == 1, "Simultaneous crossings hid the wrong number of pointers.");
        int side = pair.Sessions[0].IsRemote ? 0 : 1;
        pair.Move(side, pair.Sessions[side].ExitRight ? 10 : -10, 20);
        Assert(pair.Sessions[side].IsRemote && pair.Sessions[1 - side].IsReceiving,
            "The winning mouse lost control after simultaneous crossings.");
    }

    private static void TestHeldButtons()
    {
        using Pair pair = new();
        pair.Button(0, NativeMouse.LEFTDOWN);
        pair.Cross(0);
        Assert(!pair.Sessions[0].IsRemote, "A local drag was split between two PCs.");
        pair.Button(0, NativeMouse.LEFTUP);
        pair.Cross(0);
        pair.Button(0, NativeMouse.LEFTDOWN);
        pair.Move(0, -100);
        Assert(pair.Desktops[1].Injected.SequenceEqual(new[] { (NativeMouse.LEFTDOWN, 0), (NativeMouse.LEFTUP, 0) }),
            "Returning did not release exactly the remote button held by MWB.");
        Assert(pair.Sessions[0].PhysicalButton(NativeMouse.LEFTUP, 0),
            "Releasing a button after returning generated an unmatched local release.");
        Assert(!pair.Sessions[0].PhysicalButton(NativeMouse.LEFTUP, 0), "Release suppression did not clear.");

        pair.Cross(1);
        pair.Button(1, NativeMouse.XDOWN, 2);
        pair.Sessions[0].Dispose();
        Assert(pair.Desktops[0].Injected.TakeLast(2).SequenceEqual(new[] { (NativeMouse.XDOWN, 2), (NativeMouse.XUP, 2) }),
            "Disconnect did not release the held X button.");
        Assert(!pair.Desktops[0].Injected.Any(input => input.flags == NativeMouse.RIGHTUP),
            "Disconnect generated an unpressed right-button release.");
    }

    private static void TestStalePackets()
    {
        using Pair pair = new();
        pair.Cross(0);
        MousePacket oldEnter = pair.Sent.Last(entry => entry.packet.Kind == "enter").packet;
        pair.Move(0, -100);
        pair.Cross(1);
        pair.Sessions[1].Receive(new MousePacket("return", Ratio: 0.5, Transfer: oldEnter.Transfer, Origin: oldEnter.Origin));
        Assert(pair.Sessions[1].IsRemote, "An old return cancelled a newer crossing.");
        int before = pair.Desktops[1].Injected.Count;
        pair.Sessions[1].Receive(oldEnter with { Kind = "button", Flags = NativeMouse.RIGHTDOWN });
        Assert(pair.Desktops[1].Injected.Count == before, "A stale button packet generated a click.");
        pair.Sessions[0].Receive(oldEnter with { Kind = "button", Flags = NativeMouse.RIGHTDOWN });
        Assert(pair.Desktops[0].Injected.Count == 0, "An old packet affected the new mouse owner.");
    }

    private static void TestPointerRestoration()
    {
        using Pair pair = new();
        pair.Cross(0);
        pair.Move(0, -100);
        Assert(pair.Desktops[1].PointerHidden, "Leaving the receiving PC left its ghost pointer visible.");
        pair.Move(1, 10);
        Assert(!pair.Desktops[1].PointerHidden, "Local movement did not reveal the hidden pointer.");
        pair.Cross(1);
        pair.Sessions[1].Dispose();
        Assert(!pair.Desktops[1].PointerHidden, "Disconnect left the source pointer hidden.");
        pair.Desktops[0].PointerHidden = true;
        pair.Sessions[0].Dispose();
        Assert(!pair.Desktops[0].PointerHidden, "Cleanup did not restore a hidden idle pointer.");
    }

    private static void TestPointerInput()
    {
        Assert(!NativeMouse.IsOurInput(new NativeMouse.HookInfo { Flags = 1, ExtraInfo = (IntPtr)123 }),
            "Windows-generated touchpad/accessibility input was discarded.");
        Assert(NativeMouse.IsOurInput(new NativeMouse.HookInfo { Flags = 1, ExtraInfo = NativeMouse.InputMarker }),
            "MWB input would feed back into the other PC.");
        NativeMouse.MoveCursor(new Point(-500, 0), new Rectangle(-500, 0, 1000, 700), (_, inputs, _) =>
        {
            var mouse = inputs[0].Data.Mouse;
            Assert(mouse.Flags == (NativeMouse.MOVE | NativeMouse.ABSOLUTE | NativeMouse.VIRTUALDESK) &&
                mouse.ExtraInfo == NativeMouse.InputMarker && mouse.Dx is >= 0 and < 66 && mouse.Dy is >= 0 and < 94,
                "Absolute cursor input did not map the virtual desktop or include the feedback marker.");
            return 1;
        });
    }
}
