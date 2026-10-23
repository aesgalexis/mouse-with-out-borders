namespace MouseWithOutBorders;

internal interface IKeyboardRemappingService : IDisposable
{
    void Configure(bool enabled, IReadOnlyList<KeyMapping> mappings);
}

// A separate message loop keeps keyboard input responsive during UI dialogs,
// settings writes and mouse connection changes.
internal sealed class KeyboardRemappingService : IKeyboardRemappingService
{
    private readonly Thread _thread;
    private readonly Action<string> _error;
    private Control? _dispatch;
    private KeyboardRemapper? _engine;
    private volatile bool _disposed;
    internal KeyboardRemappingService(Action<string> error)
    {
        _error = error;
        TaskCompletionSource<bool> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _thread = new Thread(() =>
        {
            try
            {
                using Control dispatch = new();
                _ = dispatch.Handle;
                _dispatch = dispatch;
                _engine = new KeyboardRemapper(NativeKeyboard.Inject);
                using NativeKeyboard hook = new((key, down) =>
                {
                    try { return _engine.Process(key, down); }
                    catch (Exception ex)
                    {
                        try { _engine.Configure(false, []); } catch { }
                        _error(ex.Message);
                        return false;
                    }
                });
                ready.TrySetResult(true);
                if (!_disposed) Application.Run();
            }
            catch (Exception ex)
            {
                if (!ready.TrySetException(ex)) _error(ex.Message);
            }
            finally
            {
                try { _engine?.Release(); } catch (Exception ex) { _error(ex.Message); }
            }
        }) { IsBackground = true, Name = "MWB keyboard remapping" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        try
        {
            if (!ready.Task.Wait(TimeSpan.FromSeconds(5)))
            {
                _disposed = true;
                throw new TimeoutException("Keyboard remapping did not start.");
            }
            ready.Task.GetAwaiter().GetResult();
        }
        catch { _disposed = true; throw; }
    }
    public void Configure(bool enabled, IReadOnlyList<KeyMapping> mappings)
    {
        KeyCatalog.Validate(mappings);
        KeyMapping[] copy = mappings.ToArray();
        if (_disposed) return;
        _dispatch!.BeginInvoke((MethodInvoker)(() =>
        {
            try { _engine!.Configure(enabled, copy); }
            catch (Exception ex) { _error(ex.Message); }
        }));
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_dispatch is null || _dispatch.IsDisposed || !_thread.IsAlive) return;
        _dispatch.BeginInvoke((MethodInvoker)(() =>
        {
            try { _engine!.Configure(false, []); }
            catch (Exception ex) { _error(ex.Message); }
            finally { Application.ExitThread(); }
        }));
        _thread.Join(TimeSpan.FromSeconds(2));
    }

    internal Task FlushAsync()
    {
        TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _dispatch!.BeginInvoke((MethodInvoker)(() => completion.SetResult(true)));
        return completion.Task;
    }
}
