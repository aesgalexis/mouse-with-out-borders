using System.Drawing;

namespace MouseWithOutBorders;

// SendInput can still be queued when cursor ownership changes on the UI thread.
// Tag placements by ownership so an old central parking move cannot undo a return.
internal sealed class CursorMotionGuard
{
    private static int _generation;
    private IntPtr _marker;
    private bool _placementStarted;
    internal bool PlacementPending { get; private set; }

    internal CursorMotionGuard() => BeginTransition();

    internal void BeginTransition()
    {
        // Keep the complete tag within 32 bits: Windows/input bridges can strip
        // the upper half of dwExtraInfo even when this application is x64.
        do
        {
            uint generation = unchecked((uint)Interlocked.Increment(ref _generation)) & 0xFFFF;
            _marker = (IntPtr)(NativeMouse.CursorMarkerPrefix | generation);
        } while (_marker == NativeMouse.InputMarker);
        _placementStarted = false;
        PlacementPending = false;
    }

    internal void MoveCursor(Point position, Rectangle bounds, NativeMouse.InputSender? sender = null)
    {
        // Keep input at the old anchor out until Windows applies this ownership's
        // first placement. Later moves keep their order, including before clicks.
        if (!_placementStarted) PlacementPending = true;
        _placementStarted = true;
        try { NativeMouse.MoveCursor(position, bounds, sender, _marker); }
        catch { PlacementPending = false; throw; }
    }

    internal bool SuppressInjectedMove(NativeMouse.HookInfo info)
    {
        if (info.ExtraInfo == NativeMouse.InputMarker) return false;
        if (info.ExtraInfo != _marker) return true;
        PlacementPending = false;
        return false;
    }
}
