using System.Net;
using System.Net.Sockets;

namespace MouseWithOutBorders;

internal sealed class MainWindow : Form
{
    private readonly ComboBox _role = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Left = 22, Top = 62, Width = 446
    };

    private readonly Label _receiverIp = new() { Left = 22, Top = 114, Width = 446, Height = 36 };
    private readonly Label _receiverKeyLabel = new() { Left = 22, Top = 159, Width = 446, Text = "Pairing key (enter this on the mouse PC)" };
    private readonly TextBox _receiverKey = new() { Left = 22, Top = 182, Width = 446, ReadOnly = true };
    private readonly Label _hostLabel = new() { Left = 22, Top = 114, Width = 446, Text = "IP address of the other PC" };
    private readonly TextBox _host = new() { Left = 22, Top = 137, Width = 446 };
    private readonly Label _hostKeyLabel = new() { Left = 22, Top = 174, Width = 446, Text = "Pairing key from the other PC" };
    private readonly TextBox _hostKey = new() { Left = 22, Top = 197, Width = 446 };
    private readonly Button _toggle = new() { Left = 22, Top = 260, Width = 446, Height = 37 };
    private readonly Label _status = new() { Left = 22, Top = 315, Width = 446, Height = 56 };
    private readonly Label _hint = new()
    {
        Left = 22, Top = 390, Width = 446, Height = 60
    };

    private CancellationTokenSource? _runCts;
    private TcpListener? _listener;
    private volatile MousePeer? _peer;
    private MouseCapture? _capture;
    private bool _remote;
    private bool _shuttingDown;

    private bool IsReceiver => _role.SelectedIndex == 0;

    internal MainWindow()
    {
        Text = "Mouse, with out borders";
        ClientSize = new Size(490, 457);
        MinimumSize = MaximumSize = Size;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10);

        Controls.Add(new Label
        {
            Text = "One mouse. Two computers.",
            Left = 22, Top = 15, Width = 446, Height = 30,
            Font = new Font("Segoe UI", 14, FontStyle.Bold)
        });
        Controls.Add(_role);
        Controls.AddRange([
            _receiverIp, _receiverKeyLabel, _receiverKey,
            _hostLabel, _host, _hostKeyLabel, _hostKey,
            _toggle, _status, _hint
        ]);

        _role.Items.AddRange(["Receive mouse (right-hand PC)", "Control mouse (PC with the mouse)"]);
        _role.SelectedIndexChanged += (_, _) => UpdateRole();
        _role.SelectedIndex = 0;
        _receiverKey.Text = MousePeer.NewKey();

        string ips = string.Join(", ", Dns.GetHostAddresses(Dns.GetHostName())
            .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
            .Select(ip => ip.ToString()));
        _receiverIp.Text = "Local IPv4: " + (ips.Length == 0 ? "Check ipconfig" : ips) +
                           "\nListening port: " + MousePeer.Port;
        _status.Text = "Not connected.";
        _status.ForeColor = SystemColors.GrayText;
        _toggle.Click += (_, _) => Toggle();
        UpdateRole();
    }

    private void UpdateRole()
    {
        bool receiver = IsReceiver;
        _receiverIp.Visible = receiver;
        _receiverKeyLabel.Visible = receiver;
        _receiverKey.Visible = receiver;
        _hostLabel.Visible = !receiver;
        _host.Visible = !receiver;
        _hostKeyLabel.Visible = !receiver;
        _hostKey.Visible = !receiver;
        _toggle.Text = _runCts is not null ? "Disconnect / Stop" : receiver ? "Start receiver" : "Connect";
        _hint.Text = receiver
            ? "Leave this running. The other PC sends its mouse here.\nThe pointer returns when it crosses this screen's left edge."
            : "Move past the RIGHT edge of this PC's primary screen to enter the other PC.\nMove back past its LEFT edge to return.";
    }

    private void SetStatus(string message)
    {
        if (_shuttingDown || IsDisposed) return;
        _status.Text = message;
    }

    private void Ui(Action action)
    {
        if (!_shuttingDown && !IsDisposed && IsHandleCreated)
            BeginInvoke((MethodInvoker)(() => { if (!_shuttingDown && !IsDisposed) action(); }));
    }

    private void Toggle()
    {
        if (_runCts is not null)
        {
            Stop();
            return;
        }

        try
        {
            if (IsReceiver)
            {
                byte[] key = MousePeer.ParseKey(_receiverKey.Text);
                _listener = new TcpListener(IPAddress.Any, MousePeer.Port);
                _listener.Start();
                _runCts = new CancellationTokenSource();
                _role.Enabled = false;
                UpdateRole();
                SetStatus("Waiting for the mouse PC...");
                _ = RunReceiverAsync(key, _runCts);
            }
            else
            {
                byte[] key = MousePeer.ParseKey(_hostKey.Text);
                string host = _host.Text.Trim();
                if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("Enter the receiver's LAN IP.");
                _runCts = new CancellationTokenSource();
                _role.Enabled = false;
                UpdateRole();
                SetStatus("Connecting...");
                _ = RunControllerAsync(host, key, _runCts);
            }
        }
        catch (Exception ex)
        {
            Stop();
            SetStatus("Cannot start: " + ex.Message);
        }
    }

    private async Task RunControllerAsync(string host, byte[] key, CancellationTokenSource lifetime)
    {
        try
        {
            using MousePeer peer = await MousePeer.ConnectAsync(host, key, lifetime.Token);
            _peer = peer;
            Ui(() =>
            {
                try
                {
                    _capture = new MouseCapture(Handle, OnMouseHook);
                    SetStatus("Connected. Push the mouse through the right edge.");
                }
                catch (Exception ex)
                {
                    SetStatus("Mouse capture failed: " + ex.Message);
                    lifetime.Cancel();
                }
            });

            await peer.ReadLoopAsync(message =>
            {
                if (message.Kind == "return")
                    Ui(() => ReturnToThisPc(message.Ratio));
            }, lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Ui(() => SetStatus("Connection ended: " + ex.Message));
        }
        finally
        {
            Ui(() =>
            {
                if (_runCts != lifetime) return;
                Stop();
                SetStatus("Disconnected. Check the IP/key and reconnect.");
            });
        }
    }

    private async Task RunReceiverAsync(byte[] key, CancellationTokenSource lifetime)
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                TcpClient tcp = await _listener!.AcceptTcpClientAsync(lifetime.Token);
                MousePeer peer;
                try
                {
                    // A stalled or unauthenticated client must not monopolize the receiver.
                    using CancellationTokenSource authTimeout =
                        CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                    authTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                    peer = await MousePeer.AcceptAsync(tcp, key, authTimeout.Token);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
                {
                    Ui(() => SetStatus("Pairing failed. Waiting for a valid mouse PC..."));
                    continue;
                }

                using (peer)
                {
                    _peer = peer;
                    bool active = false;
                    Ui(() => SetStatus("Paired. Waiting for mouse to cross from the other PC."));

                    try
                    {
                        await peer.ReadLoopAsync(packet =>
                        {
                            Rectangle area = Screen.PrimaryScreen!.Bounds;
                            switch (packet.Kind)
                            {
                                case "enter":
                                    if (!double.IsFinite(packet.Ratio)) break;
                                    active = true;
                                    NativeMouse.SetCursorPos(area.Left + Math.Min(5, area.Width - 1),
                                        RatioToY(area, packet.Ratio));
                                    break;
                                case "move" when active:
                                    if (Math.Abs((long)packet.Dx) > 10000 || Math.Abs((long)packet.Dy) > 10000) break;
                                    if (!NativeMouse.GetCursorPos(out var pos)) break;
                                    long nextX = (long)pos.X + packet.Dx;
                                    long nextY = (long)pos.Y + packet.Dy;
                                    int x = (int)Math.Clamp(nextX, area.Left, area.Right - 1);
                                    int y = (int)Math.Clamp(nextY, area.Top, area.Bottom - 1);
                                    NativeMouse.SetCursorPos(x, y);
                                    if (packet.Dx < 0 && nextX < area.Left)
                                    {
                                        active = false;
                                        ReleaseButtons();
                                        double ratio = (double)(y - area.Top) / Math.Max(1, area.Height - 1);
                                        peer.Queue(new MousePacket("return", Ratio: ratio));
                                    }
                                    break;
                                case "button" when active:
                                    if (packet.Flags is NativeMouse.LEFTDOWN or NativeMouse.LEFTUP
                                        or NativeMouse.RIGHTDOWN or NativeMouse.RIGHTUP
                                        or NativeMouse.MIDDLEDOWN or NativeMouse.MIDDLEUP
                                        or NativeMouse.XDOWN or NativeMouse.XUP)
                                        NativeMouse.Inject(packet.Flags, packet.Data);
                                    break;
                                case "wheel" when active:
                                    if (packet.Flags is NativeMouse.WHEEL or NativeMouse.HWHEEL &&
                                        packet.Data is >= -12000 and <= 12000)
                                        NativeMouse.Inject(packet.Flags, packet.Data);
                                    break;
                            }
                        }, lifetime.Token);
                    }
                    catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
                    catch (Exception ex)
                    {
                        Ui(() => SetStatus("Mouse PC disconnected: " + ex.Message));
                    }
                    finally
                    {
                        if (active) ReleaseButtons();
                        _peer = null;
                        Ui(() => SetStatus("Waiting for the mouse PC to reconnect..."));
                    }
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Ui(() => { Stop(); SetStatus("Receiver error: " + ex.Message); });
        }
    }

    private static int RatioToY(Rectangle area, double ratio) =>
        area.Top + (int)Math.Round(Math.Clamp(ratio, 0, 1) * Math.Max(0, area.Height - 1));

    private static void ReleaseButtons()
    {
        NativeMouse.Inject(NativeMouse.LEFTUP);
        NativeMouse.Inject(NativeMouse.RIGHTUP);
        NativeMouse.Inject(NativeMouse.MIDDLEUP);
        NativeMouse.Inject(NativeMouse.XUP, 1);
        NativeMouse.Inject(NativeMouse.XUP, 2);
    }

    private bool OnMouseHook(uint message, NativeMouse.HookInfo info)
    {
        if (!_remote) return false;

        uint flag = message switch
        {
            NativeMouse.WM_LBUTTONDOWN => NativeMouse.LEFTDOWN,
            NativeMouse.WM_LBUTTONUP => NativeMouse.LEFTUP,
            NativeMouse.WM_RBUTTONDOWN => NativeMouse.RIGHTDOWN,
            NativeMouse.WM_RBUTTONUP => NativeMouse.RIGHTUP,
            NativeMouse.WM_MBUTTONDOWN => NativeMouse.MIDDLEDOWN,
            NativeMouse.WM_MBUTTONUP => NativeMouse.MIDDLEUP,
            NativeMouse.WM_XBUTTONDOWN => NativeMouse.XDOWN,
            NativeMouse.WM_XBUTTONUP => NativeMouse.XUP,
            NativeMouse.WM_MOUSEWHEEL => NativeMouse.WHEEL,
            NativeMouse.WM_MOUSEHWHEEL => NativeMouse.HWHEEL,
            _ => 0
        };

        if (flag != 0)
        {
            int data = flag is NativeMouse.WHEEL or NativeMouse.HWHEEL
                ? (short)((info.MouseData >> 16) & 0xFFFF)
                : flag is NativeMouse.XDOWN or NativeMouse.XUP
                    ? (int)((info.MouseData >> 16) & 0xFFFF)
                    : 0;

            _peer?.Queue(new MousePacket(
                flag is NativeMouse.WHEEL or NativeMouse.HWHEEL ? "wheel" : "button",
                Data: data, Flags: flag));
        }

        // Physical clicks, wheel and mouse movements stay off this PC while
        // its mouse is working on the second PC. No keyboard hook is installed.
        return true;
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == NativeMouse.WM_INPUT && _capture is not null)
        {
            var delta = NativeMouse.ReadRelativeDelta(message.LParam);
            if (delta.HasValue) OnDelta(delta.Value.dx, delta.Value.dy);
        }
        base.WndProc(ref message);
    }

    private void OnDelta(int dx, int dy)
    {
        MousePeer? peer = _peer;
        if (peer is null || (dx == 0 && dy == 0)) return;

        if (_remote)
        {
            peer.Queue(new MousePacket("move", Dx: dx, Dy: dy));
            return;
        }

        if (dx <= 0 || !NativeMouse.GetCursorPos(out var pos)) return;
        Rectangle area = Screen.PrimaryScreen!.Bounds;
        if (pos.X < area.Right - 2 || pos.Y < area.Top || pos.Y >= area.Bottom) return;

        try
        {
            NativeMouse.HoldCursorAt(area.Right - 1, pos.Y);
            _remote = true;
            double ratio = (double)(pos.Y - area.Top) / Math.Max(1, area.Height - 1);
            if (!peer.Queue(new MousePacket("enter", Ratio: ratio)))
                ReturnToThisPc(ratio);
            else
                SetStatus("Mouse is on the other PC. Move left to bring it back.");
        }
        catch (Exception ex)
        {
            ReturnToThisPc(0.5);
            SetStatus("Cursor capture failed: " + ex.Message);
        }
    }

    private void ReturnToThisPc(double ratio)
    {
        if (!_remote) return;
        _remote = false;
        NativeMouse.ReleaseCursor();
        Rectangle area = Screen.PrimaryScreen!.Bounds;
        if (!double.IsFinite(ratio)) ratio = 0.5;
        NativeMouse.SetCursorPos(Math.Max(area.Left, area.Right - 25), RatioToY(area, ratio));
        SetStatus("Mouse returned. Push through the right edge to switch again.");
    }

    private void Stop()
    {
        _runCts?.Cancel();
        _runCts = null;
        _listener?.Stop();
        _listener = null;
        _peer?.Dispose();
        _peer = null;
        _remote = false;
        _capture?.Dispose();
        _capture = null;
        NativeMouse.ReleaseCursor();
        _role.Enabled = true;
        UpdateRole();
        SetStatus("Stopped.");
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _shuttingDown = true;
        Stop();
        base.OnFormClosing(e);
    }
}
