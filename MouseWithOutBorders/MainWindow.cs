using System.ComponentModel;
using System.Net;
using System.Net.Sockets;

namespace MouseWithOutBorders;

internal sealed class MainWindow : Form
{
    private static readonly Color Background = Color.FromArgb(17, 23, 33);
    private static readonly Color Surface = Color.FromArgb(27, 36, 49);
    private static readonly Color Foreground = Color.FromArgb(235, 241, 249);
    private static readonly Color Muted = Color.FromArgb(159, 176, 194);
    private static readonly Color Accent = Color.FromArgb(90, 162, 255);

    private readonly AppSettings _settings;
    private readonly Action<AppSettings> _saveSettings;
    private readonly Func<TcpListener> _listenerFactory;
    private readonly bool _launchedAtStartup;
    private readonly RadioButton _controllerRole = new()
    {
        Name = "ControllerRole", Left = 28, Top = 119, Width = 232, Height = 50,
        Appearance = Appearance.Button, TextAlign = ContentAlignment.MiddleCenter,
        Text = "I have the mouse\nEnter the other PC's code", TabIndex = 0
    };
    private readonly RadioButton _receiverRole = new()
    {
        Name = "ReceiverRole", Left = 276, Top = 119, Width = 232, Height = 50,
        Appearance = Appearance.Button, TextAlign = ContentAlignment.MiddleCenter,
        Text = "This PC receives the mouse\nShow this PC's code", TabIndex = 1
    };
    private readonly Label _receiverIp = new() { Left = 28, Top = 180, Width = 480, Height = 42 };
    private readonly Label _receiverKeyLabel = new() { Left = 28, Top = 231, Width = 480, Text = "THIS PC'S PAIRING CODE (8 CHARACTERS)" };
    private readonly TextBox _receiverKey = new() { Name = "LocalPairingCode", Left = 28, Top = 258, Width = 366, ReadOnly = true };
    private readonly Button _copy = new() { Left = 402, Top = 255, Width = 106, Height = 34, Text = "Copy code" };
    private readonly Label _receiverSteps = new()
    {
        Left = 28, Top = 302, Width = 480, Height = 46,
        Text = "Click Start receiving. On the mouse PC, choose 'I have the mouse' and enter this PC's local IP and code."
    };
    private readonly Button _enterCode = new()
    {
        Name = "EnterOtherPcCode", Left = 28, Top = 354, Width = 480, Height = 36,
        Text = "Have the other PC's code? Enter it here"
    };

    private readonly Label _hostLabel = new() { Left = 28, Top = 178, Width = 480, Text = "RECEIVING PC'S LOCAL IP" };
    private readonly TextBox _host = new() { Name = "ReceiverHost", Left = 28, Top = 205, Width = 480, PlaceholderText = "e.g. 192.168.1.25", TabIndex = 2 };
    private readonly Label _hostKeyLabel = new() { Left = 28, Top = 245, Width = 480, Text = "ENTER THE RECEIVING PC'S PAIRING CODE" };
    private readonly TextBox _hostKey = new()
    {
        Name = "RemotePairingCode", Left = 28, Top = 272, Width = 480,
        PlaceholderText = "8 characters shown on the other PC", CharacterCasing = CharacterCasing.Upper,
        AccessibleName = "Receiving PC's pairing code", TabIndex = 3
    };
    private readonly Label _edgeLabel = new() { Left = 28, Top = 318, Width = 480, Text = "WHICH EDGE LEADS TO THE OTHER PC?" };
    private readonly ComboBox _edge = new() { Left = 28, Top = 345, Width = 480, DropDownStyle = ComboBoxStyle.DropDownList };

    private readonly Button _toggle = new() { Name = "ToggleConnection", Left = 28, Top = 403, Width = 480, Height = 48 };
    private readonly CheckBox _autoConnect = new() { Left = 28, Top = 468, Width = 480, Height = 25, Text = "Connect / listen automatically when opened" };
    private readonly CheckBox _startup = new() { Left = 28, Top = 502, Width = 480, Height = 25, Text = "Start with Windows" };
    private readonly CheckBox _minimizeTray = new() { Left = 28, Top = 536, Width = 480, Height = 25, Text = "Keep running in the notification area when closed" };
    private readonly Label _status = new() { Name = "ConnectionStatus", Left = 28, Top = 585, Width = 480, Height = 45 };
    private readonly Label _help = new() { Left = 28, Top = 635, Width = 480, Height = 46 };
    private readonly LinkLabel _logLink = new() { Left = 28, Top = 705, Width = 170, Text = "Open diagnostic log", LinkColor = Accent, ActiveLinkColor = Foreground };

    private readonly NotifyIcon _tray;
    private CancellationTokenSource? _runCts;
    private TcpListener? _listener;
    private volatile MousePeer? _peer;
    private MouseCapture? _capture;
    private bool _remote;
    private bool _shuttingDown;
    private bool _initializing = true;
    private bool _exitRequested;

    private bool IsReceiver => _receiverRole.Checked;
    private bool ExitRight => _edge.SelectedIndex != 1;

    internal MainWindow(bool launchedAtStartup = false)
        : this(SettingsStore.Load(), SettingsStore.Save, launchedAtStartup) { }

    internal MainWindow(AppSettings settings, Action<AppSettings> saveSettings, bool launchedAtStartup = false,
        Func<TcpListener>? listenerFactory = null)
    {
        _settings = settings;
        _saveSettings = saveSettings;
        _listenerFactory = listenerFactory ?? (() => new TcpListener(IPAddress.Any, MousePeer.Port));
        _launchedAtStartup = launchedAtStartup;
        Text = "Mouse, with out borders";
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        ClientSize = new Size(536, 734);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Background;
        ForeColor = Foreground;
        Font = new Font("Segoe UI", 10);
        AutoScaleMode = AutoScaleMode.Font;

        Label title = new()
        {
            Text = "Mouse, with out borders",
            Left = 28, Top = 20, Width = 480, Height = 42,
            Font = new Font("Segoe UI Semibold", 21, FontStyle.Bold), ForeColor = Foreground
        };
        Label subtitle = new()
        {
            Text = "One mouse. Two Windows computers. Nothing else.",
            Left = 28, Top = 65, Width = 480, Height = 30, ForeColor = Muted
        };
        Label modeLabel = new() { Text = "CHOOSE THIS PC'S ROLE", Left = 28, Top = 96, Width = 480 };
        Label footer = new()
        {
            Text = "Private LAN only  •  TCP 42187  •  No keyboard sharing",
            Left = 28, Top = 681, Width = 480, Height = 20, ForeColor = Muted,
            Font = new Font("Segoe UI", 9)
        };

        Controls.AddRange([
            title, subtitle, modeLabel, _controllerRole, _receiverRole,
            _receiverIp, _receiverKeyLabel, _receiverKey, _copy, _receiverSteps, _enterCode,
            _hostLabel, _host, _hostKeyLabel, _hostKey, _edgeLabel, _edge,
            _toggle, _autoConnect, _startup, _minimizeTray, _status, _help, footer, _logLink
        ]);

        foreach (Label label in Controls.OfType<Label>())
        {
            label.BackColor = Background;
            if (label != title && label != subtitle && label != footer && label != _receiverIp && label != _receiverSteps && label != _status && label != _help)
            {
                label.ForeColor = Muted;
                label.Font = new Font("Segoe UI Semibold", 9, FontStyle.Bold);
            }
        }

        foreach (TextBox text in Controls.OfType<TextBox>())
        {
            text.BackColor = Surface;
            text.ForeColor = Foreground;
            text.BorderStyle = BorderStyle.FixedSingle;
        }
        _receiverKey.Font = new Font("Consolas", 16, FontStyle.Bold);
        _hostKey.Font = new Font("Consolas", 14);
        foreach (ComboBox combo in Controls.OfType<ComboBox>())
        {
            combo.BackColor = Surface;
            combo.ForeColor = Foreground;
            combo.FlatStyle = FlatStyle.Flat;
        }
        foreach (CheckBox check in Controls.OfType<CheckBox>())
        {
            check.ForeColor = Foreground;
            check.BackColor = Background;
        }

        _toggle.FlatStyle = FlatStyle.Flat;
        _toggle.FlatAppearance.BorderSize = 0;
        _toggle.BackColor = Accent;
        _toggle.ForeColor = Color.FromArgb(13, 27, 42);
        _toggle.Font = new Font("Segoe UI Semibold", 11, FontStyle.Bold);
        foreach (Button button in new[] { _copy, _enterCode })
        {
            button.FlatStyle = FlatStyle.Flat;
            button.BackColor = Surface;
            button.ForeColor = Foreground;
            button.FlatAppearance.BorderColor = Muted;
        }
        foreach (RadioButton role in new[] { _controllerRole, _receiverRole })
        {
            role.FlatStyle = FlatStyle.Flat;
            role.FlatAppearance.BorderColor = Muted;
            role.FlatAppearance.CheckedBackColor = Color.FromArgb(36, 72, 111);
            role.BackColor = Surface;
            role.ForeColor = Foreground;
            role.Font = new Font("Segoe UI", 9);
        }

        _edge.Items.AddRange(["Right edge → other PC on right", "Left edge → other PC on left"]);
        _controllerRole.Checked = _settings.Role == "Controller";
        _receiverRole.Checked = !_controllerRole.Checked;
        _edge.SelectedIndex = _settings.ExitEdge == "Left" ? 1 : 0;
        _host.Text = _settings.Host;
        _hostKey.Text = _settings.RemoteKey;
        _receiverKey.Text = _settings.PairingKey;
        _autoConnect.Checked = _settings.AutoConnect;
        _minimizeTray.Checked = _settings.MinimizeToTray;
        try { _startup.Checked = WindowsStartup.IsEnabled(); }
        catch (Exception ex) { SettingsStore.Log("Read startup preference: " + ex.Message); }

        try
        {
            string ips = string.Join(", ", Dns.GetHostAddresses(Dns.GetHostName())
                .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                .Select(ip => ip.ToString()));
            _receiverIp.Text = "Local IP: " + (ips.Length == 0 ? "See ipconfig in Windows" : ips) +
                               "\nListen port: " + MousePeer.Port;
        }
        catch (SocketException) { _receiverIp.Text = "Local IP: See ipconfig in Windows"; }

        ContextMenuStrip trayMenu = new();
        trayMenu.Items.Add("Open", null, (_, _) => ShowWindow());
        trayMenu.Items.Add("Stop mouse sharing", null, (_, _) => Stop());
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Exit", null, (_, _) => ExitApplication());
        _tray = new NotifyIcon
        {
            Icon = Icon,
            Text = "Mouse, with out borders",
            ContextMenuStrip = trayMenu,
            Visible = true
        };
        _tray.DoubleClick += (_, _) => ShowWindow();

        _controllerRole.CheckedChanged += (_, _) =>
        {
            if (!_initializing && _controllerRole.Checked) { ShowMode(); SavePreferences(); }
        };
        _receiverRole.CheckedChanged += (_, _) =>
        {
            if (!_initializing && _receiverRole.Checked) { ShowMode(); SavePreferences(); }
        };
        _enterCode.Click += (_, _) =>
        {
            if (_runCts is not null) Stop();
            _controllerRole.Checked = true;
            if (string.IsNullOrWhiteSpace(_host.Text)) _host.Focus();
            else _hostKey.Focus();
        };
        _edge.SelectedIndexChanged += (_, _) => { if (!_initializing) SavePreferences(); };
        _host.Leave += (_, _) => SavePreferences();
        _hostKey.Leave += (_, _) => SavePreferences();
        _autoConnect.CheckedChanged += (_, _) => { if (!_initializing) SavePreferences(); };
        _minimizeTray.CheckedChanged += (_, _) => { if (!_initializing) SavePreferences(); };
        _startup.CheckedChanged += (_, _) =>
        {
            if (_initializing) return;
            try { WindowsStartup.SetEnabled(_startup.Checked); }
            catch (Exception ex)
            {
                SettingsStore.Log("Configure startup: " + ex.Message);
                SetStatus("Cannot change Windows startup: " + ex.Message);
                _initializing = true;
                _startup.Checked = !_startup.Checked;
                _initializing = false;
            }
        };
        _copy.Click += (_, _) =>
        {
            Clipboard.SetText(_receiverKey.Text);
            SetStatus("Code copied. On the mouse PC, choose 'I have the mouse' and enter it there.");
        };
        _toggle.Click += (_, _) => Toggle();
        _logLink.LinkClicked += (_, _) =>
        {
            SettingsStore.Log("Diagnostics opened.");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = SettingsStore.LogPath, UseShellExecute = true
            });
        };

        _initializing = false;
        _status.ForeColor = Accent;
        _status.Text = "Not connected.";
        ShowMode();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        BeginInvoke((MethodInvoker)(() =>
        {
            if (_launchedAtStartup) Hide();
            if (_autoConnect.Checked && _runCts is null) Toggle();
        }));
    }

    private void ShowMode()
    {
        bool receiver = IsReceiver;
        _receiverIp.Visible = receiver;
        _receiverKeyLabel.Visible = receiver;
        _receiverKey.Visible = receiver;
        _copy.Visible = receiver;
        _receiverSteps.Visible = receiver;
        _enterCode.Visible = receiver;
        _hostLabel.Visible = !receiver;
        _host.Visible = !receiver;
        _hostKeyLabel.Visible = !receiver;
        _hostKey.Visible = !receiver;
        _edgeLabel.Visible = !receiver;
        _edge.Visible = !receiver;
        _toggle.Text = _runCts is null ? (receiver ? "Start receiving" : "Connect mouse") : "Disconnect / Stop";
        _help.ForeColor = Muted;
        _help.Text = receiver
            ? "Both PCs show a code? Choose 'I have the mouse' on the PC with your physical mouse."
            : "On the other PC, choose 'This PC receives the mouse' and click Start receiving before connecting.";
    }

    private void SavePreferences()
    {
        if (_initializing) return;
        _settings.Role = IsReceiver ? "Receiver" : "Controller";
        _settings.Host = _host.Text.Trim();
        _settings.RemoteKey = MousePeer.NormalizeKey(_hostKey.Text);
        _settings.PairingKey = _receiverKey.Text.Trim();
        _settings.ExitEdge = ExitRight ? "Right" : "Left";
        _settings.AutoConnect = _autoConnect.Checked;
        _settings.MinimizeToTray = _minimizeTray.Checked;
        try { _saveSettings(_settings); }
        catch (Exception ex)
        {
            SettingsStore.Log("Save settings: " + ex.Message);
            SetStatus("Could not save settings: " + ex.Message);
        }
    }

    private void SetStatus(string message)
    {
        if (_shuttingDown || IsDisposed) return;
        _status.Text = message;
        SettingsStore.Log(message);
    }

    private void Ui(Action action)
    {
        if (_shuttingDown || IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke((MethodInvoker)(() => { if (!_shuttingDown && !IsDisposed) action(); }));
        }
        catch (InvalidOperationException) { }
    }

    private void ShowWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void Toggle()
    {
        if (_runCts is not null) { Stop(); return; }
        try
        {
            SavePreferences();
            byte[] key = MousePeer.ParseKey(IsReceiver ? _receiverKey.Text : _hostKey.Text);
            CancellationTokenSource lifetime = new();
            if (IsReceiver)
            {
                _listener = _listenerFactory();
                _listener.Start();
                _runCts = lifetime;
                _controllerRole.Enabled = false;
                _receiverRole.Enabled = false;
                ShowMode();
                SetStatus("Receiver started. Waiting for the mouse PC...");
                _ = RunReceiverAsync(key, lifetime);
            }
            else
            {
                string host = _host.Text.Trim();
                if (host.Length == 0) throw new ArgumentException("Enter the receiver PC's LAN IP.");
                _runCts = lifetime;
                _controllerRole.Enabled = false;
                _receiverRole.Enabled = false;
                _host.Enabled = false;
                _hostKey.Enabled = false;
                _edge.Enabled = false;
                ShowMode();
                SetStatus("Connecting to " + host + "...");
                _ = RunControllerAsync(host, key, lifetime);
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
        bool connectedOnce = false;
        while (!lifetime.IsCancellationRequested)
        {
            MousePeer? activePeer = null;
            try
            {
                activePeer = await MousePeer.ConnectAsync(host, key, lifetime.Token).ConfigureAwait(false);
                connectedOnce = true;
                _peer = activePeer;
                MousePeer installedPeer = activePeer;
                Ui(() =>
                {
                    if (_runCts != lifetime || !ReferenceEquals(_peer, installedPeer)) return;
                    try
                    {
                        _capture?.Dispose();
                        _capture = new MouseCapture(Handle, OnMouseHook);
                        SetStatus("Connected. Move the mouse beyond the " +
                                  (ExitRight ? "right" : "left") + " edge.");
                    }
                    catch (Exception ex)
                    {
                        SetStatus("Mouse capture error: " + ex.Message);
                        lifetime.Cancel();
                    }
                });

                await activePeer.ReadLoopAsync(packet =>
                {
                    if (packet.Kind == "return")
                        Ui(() => ReturnToThisPc(packet.Ratio));
                }, lifetime.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                SettingsStore.Log("Controller connection: " + ex.Message);
                if (!connectedOnce)
                {
                    string message = "Could not connect: " + ex.Message;
                    if (!_settings.AutoConnect)
                    {
                        Ui(() =>
                        {
                            if (_runCts != lifetime) return;
                            Stop();
                            SetStatus(message);
                        });
                        break;
                    }
                    Ui(() =>
                    {
                        if (_runCts == lifetime)
                            SetStatus("Receiver not ready. Retrying connection...");
                    });
                }
            }
            finally
            {
                if (activePeer is not null)
                {
                    if (ReferenceEquals(_peer, activePeer)) _peer = null;
                    activePeer.Dispose();
                }
                Ui(() =>
                {
                    if (_runCts != lifetime) return;
                    ReturnToThisPc(0.5);
                    _capture?.Dispose();
                    _capture = null;
                    SetStatus("Connection lost. Trying to reconnect...");
                });
            }

            if (lifetime.IsCancellationRequested) break;
            try { await Task.Delay(TimeSpan.FromSeconds(2), lifetime.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunReceiverAsync(byte[] key, CancellationTokenSource lifetime)
    {
        TcpListener listener = _listener!;
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                TcpClient tcp = await listener.AcceptTcpClientAsync(lifetime.Token).ConfigureAwait(false);
                MousePeer peer;
                try
                {
                    using CancellationTokenSource authTimeout =
                        CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                    authTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                    peer = await MousePeer.AcceptAsync(tcp, key, authTimeout.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
                {
                    Ui(() => SetStatus("Pairing failed. Waiting for a valid connection."));
                    continue;
                }

                using (peer)
                {
                    if (lifetime.IsCancellationRequested) break;
                    _peer = peer;
                    bool active = false;
                    bool entersFromLeft = true;
                    string disconnectedStatus = "Waiting for the mouse PC to reconnect...";
                    Ui(() => SetStatus("PC connected. Waiting for its mouse to cross over."));
                    try
                    {
                        await peer.ReadLoopAsync(packet =>
                        {
                            Rectangle area = DesktopBounds();
                            switch (packet.Kind)
                            {
                                case "enter":
                                    if (!double.IsFinite(packet.Ratio)) break;
                                    entersFromLeft = packet.Data >= 0;
                                    active = true;
                                    NativeMouse.SetCursorPos(entersFromLeft
                                        ? Math.Min(area.Right - 1, area.Left + 5)
                                        : Math.Max(area.Left, area.Right - 6),
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
                                    if ((entersFromLeft && packet.Dx < 0 && nextX < area.Left) ||
                                        (!entersFromLeft && packet.Dx > 0 && nextX >= area.Right))
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
                                    if ((packet.Flags is NativeMouse.WHEEL or NativeMouse.HWHEEL) &&
                                        packet.Data is >= -12000 and <= 12000)
                                        NativeMouse.Inject(packet.Flags, packet.Data);
                                    break;
                            }
                        }, lifetime.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
                    catch (Win32Exception ex)
                    {
                        // Closing this peer releases the controller's cursor.
                        disconnectedStatus = "Mouse input failed: " + ex.Message;
                        SettingsStore.Log(disconnectedStatus);
                    }
                    catch (Exception ex)
                    {
                        SettingsStore.Log("Receiver connection: " + ex.Message);
                    }
                    finally
                    {
                        // Never leave a remote mouse button held after disconnect.
                        if (active) ReleaseButtons();
                        if (ReferenceEquals(_peer, peer)) _peer = null;
                        Ui(() => { if (_runCts == lifetime) SetStatus(disconnectedStatus); });
                    }
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Ui(() =>
            {
                if (_runCts != lifetime) return;
                Stop();
                SetStatus("Receiver error: " + ex.Message);
            });
        }
    }

    private static Rectangle DesktopBounds()
    {
        Rectangle result = Screen.AllScreens[0].Bounds;
        foreach (Screen screen in Screen.AllScreens.Skip(1))
            result = Rectangle.Union(result, screen.Bounds);
        return result;
    }

    private static int RatioToY(Rectangle area, double ratio) =>
        area.Top + (int)Math.Round(Math.Clamp(ratio, 0, 1) * Math.Max(0, area.Height - 1));

    private static void ReleaseButtons() =>
        NativeMouse.ReleaseButtons(ex => SettingsStore.Log("Release mouse button: " + ex.Message));

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
        return true; // no local physical mouse actions while controlling remote PC
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
        if (ExitRight ? dx <= 0 : dx >= 0) return;
        if (!NativeMouse.GetCursorPos(out var pos)) return;
        Rectangle area = DesktopBounds();
        if (ExitRight ? pos.X < area.Right - 2 : pos.X > area.Left + 1) return;
        if (pos.Y < area.Top || pos.Y >= area.Bottom) return;

        try
        {
            NativeMouse.HoldCursorAt(pos.X, pos.Y);
            _remote = true;
            double ratio = (double)(pos.Y - area.Top) / Math.Max(1, area.Height - 1);
            if (!peer.Queue(new MousePacket("enter", Data: ExitRight ? 1 : -1, Ratio: ratio)))
                ReturnToThisPc(ratio);
            else
                SetStatus("Mouse is on the other PC. Move back across the edge to return.");
        }
        catch (Exception ex)
        {
            ReturnToThisPc(0.5);
            SetStatus("Mouse capture failed: " + ex.Message);
        }
    }

    private void ReturnToThisPc(double ratio)
    {
        if (!_remote) return;
        _remote = false;
        NativeMouse.ReleaseCursor();
        Rectangle area = DesktopBounds();
        if (!double.IsFinite(ratio)) ratio = 0.5;
        int x = ExitRight
            ? Math.Max(area.Left, area.Right - 25)
            : Math.Min(area.Right - 1, area.Left + 24);
        NativeMouse.SetCursorPos(x, RatioToY(area, ratio));
        SetStatus("Mouse returned. Move across the edge to switch again.");
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
        _controllerRole.Enabled = true;
        _receiverRole.Enabled = true;
        _host.Enabled = true;
        _hostKey.Enabled = true;
        _edge.Enabled = true;
        ShowMode();
        SetStatus("Stopped.");
    }

    private void ExitApplication()
    {
        _exitRequested = true;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_exitRequested && !_shuttingDown &&
            e.CloseReason == CloseReason.UserClosing && _minimizeTray.Checked)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        _shuttingDown = true;
        Stop();
        _tray.Visible = false;
        _tray.Dispose();
        base.OnFormClosing(e);
    }
}
