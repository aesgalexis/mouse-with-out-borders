using System.Drawing;

namespace MouseWithOutBorders;

// SendInput can still be queued when cursor ownership changes on the UI thread.
// Tag placements by ownership so an old central parking move cannot undo a return.
internal sealed class CursorMotionGuard
{
    internal const int PlacementTimeoutMilliseconds = 250;
    private static int _generation;
    private readonly Func<long> _monotonicMilliseconds;
    private IntPtr _marker;
    private bool _placementStarted;
    private bool _placementPending;
    private long _placementStartedAt;

    internal CursorMotionGuard(Func<long>? monotonicMilliseconds = null)
    {
        _monotonicMilliseconds = monotonicMilliseconds ?? (() => Environment.TickCount64);
        BeginTransition();
    }

    internal bool PlacementPending
    {
        get
        {
            ExpirePlacement();
            return _placementPending;
        }
    }

    private void ExpirePlacement()
    {
        // A missed acknowledgement must not block physical movement forever.
        // Retire its tag too: after physical movement resumes, a delayed old
        // placement must not overwrite the cursor's recovered position.
        if (_placementPending && unchecked(_monotonicMilliseconds() - _placementStartedAt) is
            < 0 or >= PlacementTimeoutMilliseconds)
            BeginTransition();
    }

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
        _placementPending = false;
    }

    internal void MoveCursor(Point position, Rectangle bounds, NativeMouse.InputSender? sender = null)
    {
        ExpirePlacement();
        // Keep input at the old anchor out until Windows applies this ownership's
        // first placement. Later moves keep their order, including before clicks.
        if (!_placementStarted)
        {
            _placementStartedAt = _monotonicMilliseconds();
            _placementPending = true;
        }
        _placementStarted = true;
        try { NativeMouse.MoveCursor(position, bounds, sender, _marker); }
        catch { BeginTransition(); throw; }
    }

    internal bool SuppressInjectedMove(NativeMouse.HookInfo info)
    {
        ExpirePlacement();
        // Match the same low 32 bits used by NativeMouse.IsOurInput. Input
        // bridges can preserve the tag while extending/replacing its high bits.
        uint marker = unchecked((uint)info.ExtraInfo.ToInt64());
        if (marker == unchecked((uint)NativeMouse.InputMarker.ToInt64())) return false;
        if (marker != unchecked((uint)_marker.ToInt64())) return true;
        _placementPending = false;
        return false;
    }
}
