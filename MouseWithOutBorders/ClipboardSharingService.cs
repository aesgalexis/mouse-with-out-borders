using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Channels;

namespace MouseWithOutBorders;

internal interface IClipboardSharingService : IDisposable
{
    void SetEnabled(bool enabled);
    void Attach(MousePeer peer);
    void Detach(MousePeer peer);
}

/// <summary>Clipboard work has its own STA and bounded, acknowledged transfer queue.</summary>
internal sealed class ClipboardSharingService : IClipboardSharingService
{
    private static readonly JsonSerializerOptions EnvelopeJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private sealed record Envelope(Guid Id, bool Ack = false, ClipboardMessage? Message = null, bool Accepted = true);
    private sealed record Delivery(Session Session, byte[] Bytes);
    private sealed class Session
    {
        internal readonly MousePeer Peer;
        internal readonly CancellationTokenSource Lifetime;
        internal readonly CancellationToken Token;
        internal Session(MousePeer peer, CancellationToken stopping)
        {
            Peer = peer;
            Lifetime = CancellationTokenSource.CreateLinkedTokenSource(stopping);
            Token = Lifetime.Token;
        }
        internal Action<ReadOnlyMemory<byte>>? Received;
        internal Action? Availability;
        internal volatile bool Ready;
        internal Guid PendingId;
        internal TaskCompletionSource? PendingAck;
    }

    private readonly object _gate = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly SemaphoreSlim _configuration = new(1, 1);
    private readonly SemaphoreSlim _sending = new(1, 1);
    private readonly Channel<Delivery> _incoming = Channel.CreateBounded<Delivery>(
        new BoundedChannelOptions(8) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly ClipboardTransfer _transfer;
    private readonly Action<string> _status;
    private readonly IClipboardAdapter _adapter;
    private readonly Thread _thread;
    private readonly Task _receiver;
    private readonly AsyncLocal<Session?> _outgoingSession = new();
    private Control? _dispatcher;
    private Session? _session;
    private Session? _receivingSession;
    private MousePeer? _peer;
    private CancellationTokenSource? _copy;
    private uint _sequence;
    private bool _baseline;
    private bool _enabled;
    private bool _disposed;

    internal ClipboardSharingService(Action<string> status, IClipboardAdapter? adapter = null, string? cacheRoot = null)
    {
        _status = status;
        _adapter = adapter ?? new WindowsClipboardAdapter();
        _transfer = new ClipboardTransfer(cacheRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MouseWithOutBorders", "Clipboard"), SendMessageAsync, Complete, Report);
        using ManualResetEventSlim ready = new();
        Exception? startupError = null;
        _thread = new Thread(() =>
        {
            bool started = false;
            try
            {
                using Control dispatcher = new();
                _ = dispatcher.Handle;
                _dispatcher = dispatcher;
                using System.Windows.Forms.Timer timer = new() { Interval = 150 };
                timer.Tick += (_, _) => Poll();
                timer.Start();
                started = true;
                ready.Set();
                Application.Run();
                _dispatcher = null;
            }
            catch (Exception ex)
            {
                if (!started) { startupError = ex; ready.Set(); }
                Report("Clipboard sharing stopped: " + ex.Message);
            }
        }) { IsBackground = true, Name = "MWB clipboard" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        ready.Wait();
        if (startupError is not null) { _transfer.Dispose(); throw new InvalidOperationException("Could not start clipboard sharing.", startupError); }
        _receiver = ReceiveLoopAsync();
    }

    public void SetEnabled(bool enabled)
    {
        lock (_gate)
        {
            if (_disposed || _enabled == enabled) return;
            _enabled = enabled;
            Reconfigure();
        }
    }

    public void Attach(MousePeer peer)
    {
        lock (_gate)
        {
            if (_disposed || ReferenceEquals(_peer, peer)) return;
            _peer = peer;
            Reconfigure();
        }
    }

    public void Detach(MousePeer peer)
    {
        lock (_gate)
        {
            if (_disposed || !ReferenceEquals(_peer, peer)) return;
            _peer = null;
            Reconfigure();
        }
    }

    // Called under _gate; cancellation invalidates old callbacks before asynchronous cleanup.
    private void Reconfigure()
    {
        Session? old = _session;
        _session = null;
        if (old is not null)
        {
            old.Lifetime.Cancel();
            old.Lifetime.Dispose();
            old.PendingAck?.TrySetCanceled();
            old.Peer.ClipboardReceived -= old.Received;
            old.Peer.ClipboardAvailabilityChanged -= old.Availability;
            old.Peer.ConfigureClipboard(false);
        }
        Session? session = _enabled && _peer is not null ? new Session(_peer, _stopping.Token) : null;
        _session = session;
        Post(() => { _baseline = false; CancelCopy(); });
        if (session is not null)
        {
            session.Received = bytes =>
            {
                lock (_gate)
                {
                    if (!Current(session)) return;
                    if (!_incoming.Writer.TryWrite(new(session, bytes.ToArray())))
                    {
                        session.Lifetime.Cancel();
                        session.Peer.ConfigureClipboard(false);
                        Report("Clipboard transfer stopped: receive queue is full.");
                    }
                }
            };
            session.Availability = () => Post(() =>
            {
                lock (_gate) { if (!Current(session)) return; }
                _baseline = false;
                if (!session.Peer.ClipboardAvailable) CancelCopy();
            });
            session.Peer.ClipboardReceived += session.Received;
            session.Peer.ClipboardAvailabilityChanged += session.Availability;
        }
        _ = ConfigureAsync(session);
    }

    private async Task ConfigureAsync(Session? session)
    {
        try
        {
            await _configuration.WaitAsync(_stopping.Token).ConfigureAwait(false);
            try
            {
                lock (_gate) { if (_disposed || !ReferenceEquals(_session, session)) return; }
                await _transfer.ResetAsync(_stopping.Token).ConfigureAwait(false);
                lock (_gate)
                {
                    if (_disposed || !ReferenceEquals(_session, session) || session is null) return;
                    session.Ready = true;
                    session.Peer.ConfigureClipboard(true);
                }
            }
            finally { _configuration.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Report("Could not enable clipboard sharing: " + ex.Message); }
    }

    private bool Current(Session session) => !_disposed && ReferenceEquals(_session, session) &&
        session.Ready && !session.Lifetime.IsCancellationRequested;

    private void Poll()
    {
        Session? session;
        lock (_gate) { session = _session; if (session is null || !Current(session) || !session.Peer.ClipboardAvailable) { _baseline = false; return; } }
        try
        {
            uint sequence = _adapter.Sequence;
            if (!_baseline) { _sequence = sequence; _baseline = true; return; }
            if (_sequence == sequence) return;
            ClipboardContent? content = _adapter.Read();
            // Read and sequence must represent the same local copy.
            if (_adapter.Sequence != sequence) return;
            _sequence = sequence;
            CancelCopy();
            if (content is null) return;
            _copy = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
            _ = SendCopyAsync(session, content, _copy);
        }
        catch (ExternalException) { /* Another app has the clipboard open; retry next tick. */ }
        catch (Exception ex) { _sequence = _adapter.Sequence; Report("Clipboard not shared: " + ex.Message); }
    }

    private void CancelCopy()
    {
        try { _copy?.Cancel(); }
        catch (ObjectDisposedException) { }
        _copy = null;
    }

    private async Task SendCopyAsync(Session session, ClipboardContent content, CancellationTokenSource copy)
    {
        try
        {
            _outgoingSession.Value = session;
            await _transfer.SendAsync(content, copy.Token).ConfigureAwait(false);
            Report(content.Files is not null ? "Files copied to the other PC." : "");
        }
        catch (OperationCanceledException)
        {
            if (!copy.IsCancellationRequested && !session.Token.IsCancellationRequested)
                Report("Clipboard transfer timed out.");
        }
        catch (Exception ex) { if (!session.Lifetime.IsCancellationRequested) Report("Clipboard not shared: " + ex.Message); }
        finally { _outgoingSession.Value = null; copy.Dispose(); }
    }

    private async Task SendMessageAsync(ClipboardMessage message, CancellationToken ct)
    {
        await _sending.WaitAsync(ct).ConfigureAwait(false);
        Session? session = null;
        TaskCompletionSource ack = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Guid id = Guid.NewGuid();
        try
        {
            lock (_gate)
            {
                session = _outgoingSession.Value;
                if (session is null || !Current(session) || !session.Peer.ClipboardAvailable)
                    throw new OperationCanceledException("Clipboard connection changed.", ct);
                session.PendingId = id;
                session.PendingAck = ack;
            }
            using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(ct, session.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            await session.Peer.SendClipboardAsync(JsonSerializer.SerializeToUtf8Bytes(new Envelope(id, Message: message), EnvelopeJson), deadline.Token).ConfigureAwait(false);
            await ack.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate) { if (session?.PendingId == id) session.PendingAck = null; }
            _sending.Release();
        }
    }

    private async Task ReceiveLoopAsync()
    {
        try
        {
            await foreach (Delivery delivery in _incoming.Reader.ReadAllAsync(_stopping.Token).ConfigureAwait(false))
            {
                Session session = delivery.Session;
                lock (_gate) { if (!Current(session)) continue; }
                try
                {
                    Envelope? envelope = JsonSerializer.Deserialize<Envelope>(delivery.Bytes);
                    if (envelope is null || envelope.Id == Guid.Empty || envelope.Ack == (envelope.Message is not null))
                        throw new InvalidDataException("Invalid clipboard envelope.");
                    if (envelope.Ack)
                    {
                        lock (_gate)
                        {
                            if (session.PendingId == envelope.Id)
                            {
                                if (envelope.Accepted) session.PendingAck?.TrySetResult();
                                else session.PendingAck?.TrySetException(new InvalidDataException("The other PC rejected the clipboard transfer."));
                            }
                        }
                        continue;
                    }
                    _receivingSession = session;
                    bool accepted = await _transfer.ReceiveAsync(envelope.Message!, session.Token).ConfigureAwait(false);
                    await session.Peer.SendClipboardAsync(JsonSerializer.SerializeToUtf8Bytes(new Envelope(envelope.Id, Ack: true, Accepted: accepted), EnvelopeJson), session.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { Report("Clipboard transfer failed: " + ex.Message); await _transfer.ResetAsync(_stopping.Token).ConfigureAwait(false); }
                finally { _receivingSession = null; }
            }
        }
        catch (OperationCanceledException) { }
    }

    private void Complete(ClipboardContent content)
    {
        Session? session = _receivingSession;
        if (session is null) return;
        Post(() =>
        {
            try
            {
                lock (_gate)
                {
                    if (!Current(session) || !session.Peer.ClipboardAvailable) return;
                    CancelCopy();
                    _adapter.Write(content);
                    // Ignore our own Windows clipboard update to prevent echoing it back.
                    _sequence = _adapter.Sequence;
                    _baseline = true;
                }
                Report(content.Files is not null ? "Files received." : "");
            }
            catch (Exception ex) { Report("Could not update the clipboard: " + ex.Message); }
        });
    }

    private void Post(Action action)
    {
        try { _dispatcher?.BeginInvoke((MethodInvoker)(() => { if (!_disposed) action(); })); }
        catch (InvalidOperationException) { }
    }

    private void Report(string message)
    {
        try { _status(message); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _enabled = false;
            _peer = null;
            Reconfigure();
            _disposed = true;
            _stopping.Cancel();
            _incoming.Writer.TryComplete();
        }
        try { _dispatcher?.BeginInvoke((MethodInvoker)(() => { CancelCopy(); Application.ExitThread(); })); }
        catch (InvalidOperationException) { }
        if (Thread.CurrentThread != _thread) _thread.Join(TimeSpan.FromSeconds(2));
        _ = CleanupAsync();
    }

    private async Task CleanupAsync()
    {
        try { await _receiver.ConfigureAwait(false); _transfer.Dispose(); }
        catch (Exception ex) { Report("Clipboard cleanup failed: " + ex.Message); }
    }
}
