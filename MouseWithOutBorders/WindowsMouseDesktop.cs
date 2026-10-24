using System.ComponentModel;

namespace MouseWithOutBorders;

// Lives on the input thread. Window creation/destruction is always posted out
// of WH_MOUSE_LL, which must not run WinForms focus/lifecycle code.
internal sealed class WindowsMouseDesktop : IMouseDesktop, IDisposable
{
    private readonly Action<Action> _post;
    private readonly CursorMotionGuard _motion = new();
    private HiddenPointerWindow? _hiddenPointer;
    private Point _anchor;
    private bool _hideRequested, _refreshQueued, _disposed;

    internal WindowsMouseDesktop(Action<Action> post) => _post = post;
    public void BeginCursorTransition() => _motion.BeginTransition();
    internal bool PlacementPending => _motion.PlacementPending;

    internal bool SuppressInjectedMove(NativeMouse.HookInfo info)
    {
        bool suppress = _motion.SuppressInjectedMove(info);
        if (!suppress && !_hideRequested) QueueVisibilityRefresh();
        return suppress;
    }

    public void SetPointerHidden(bool hidden)
    {
        if (_disposed) return;
        bool changed = _hideRequested != hidden;
        _hideRequested = hidden;
        if (hidden && changed)
        {
            Rectangle bounds = (Screen.PrimaryScreen ?? Screen.AllScreens[0]).Bounds;
            _anchor = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
            MoveCursor(_anchor);
        }
        if (changed || (!hidden && _hiddenPointer is not null)) QueueVisibilityRefresh();
    }

    private void QueueVisibilityRefresh()
    {
        if (_disposed || _refreshQueued) return;
        _refreshQueued = true;
        _post(() =>
        {
            _refreshQueued = false;
            if (_disposed) return;
            if (_hideRequested)
            {
                if (_hiddenPointer is not null) return;
                _hiddenPointer = new HiddenPointerWindow(_anchor);
                _hiddenPointer.HidePointer();
            }
            else if (!PlacementPending) RevealPointer();
        });
    }

    // Called by the input-loop timer too, so a lost placement notification
    // cannot leave a hidden window or local-input gate stuck indefinitely.
    internal void RecoverVisibility()
    {
        if (!_disposed && !_hideRequested && !PlacementPending) RevealPointer();
    }

    private void RevealPointer()
    {
        HiddenPointerWindow? pointer = _hiddenPointer;
        _hiddenPointer = null;
        pointer?.Dispose();
    }

    public Rectangle Bounds => SystemInformation.VirtualScreen;
    public Point Cursor => NativeMouse.GetCursorPos(out var position)
        ? new Point(position.X, position.Y) : throw new Win32Exception("Cannot read the mouse position.");
    public void MoveCursor(Point position) => _motion.MoveCursor(position, Bounds);
    public void Inject(uint flags, int data) => NativeMouse.Inject(flags, data);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        RevealPointer();
    }
}
