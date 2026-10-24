using System.ComponentModel;
using System.Net;
using System.Net.Sockets;

namespace MouseWithOutBorders;

internal sealed class MainWindow : Form
{
    private static readonly Color Background = DarkTheme.Background;
    private static readonly Color Surface = DarkTheme.Surface;
    private static readonly Color Foreground = DarkTheme.Text;
    private static readonly Color Muted = DarkTheme.Muted;
    private static readonly Color Accent = DarkTheme.Accent;
    private static readonly uint InstallerExitMessage =
        NativeMouse.RegisterWindowMessage("MouseWithOutBorders.ExitForUpdate.79DBA1BD");

    private readonly AppSettings _settings;
    private readonly Action<AppSettings> _saveSettings;
    private readonly Action<bool> _configureStartup;
    private readonly Func<TcpListener> _listenerFactory;
    private readonly IAppUpdater _updater;
    private readonly Func<Action<string>, IKeyboardRemappingService> _keyboardFactory;
    private IKeyboardRemappingService? _keyboard;
    private readonly bool _launchedAtStartup;
    private readonly bool _resumeSharing;
    private readonly Action<bool> _restartElevated;
    private readonly RadioButton _controllerRole = new DarkRoleButton()
    {
        Name = "ControllerRole", Left = 28, Top = 119, Width = 232, Height = 50,
        Appearance = Appearance.Button, TextAlign = ContentAlignment.MiddleCenter,
        Text = "Connect to other PC\nEnter the other PC's code", TabIndex = 0
    };
    private readonly RadioButton _receiverRole = new DarkRoleButton()
    {
        Name = "ReceiverRole", Left = 276, Top = 119, Width = 232, Height = 50,
        Appearance = Appearance.Button, TextAlign = ContentAlignment.MiddleCenter,
        Text = "Allow the connection\nShow this PC's code", TabIndex = 1
    };
    private readonly Label _receiverIp = new() { Left = 28, Top = 180, Width = 480, Height = 42 };
    private readonly Label _receiverKeyLabel = new() { Left = 28, Top = 231, Width = 480, Text = "THIS PC'S PAIRING CODE (8 CHARACTERS)" };
    private readonly TextBox _receiverKey = new() { Name = "LocalPairingCode", Left = 28, Top = 258, Width = 366, ReadOnly = true };
    private readonly Button _copy = new DarkButton() { Left = 402, Top = 255, Width = 106, Height = 34, Text = "Copy code" };
    private readonly Label _receiverSteps = new()
    {
        Left = 28, Top = 302, Width = 480, Height = 46,
        Text = "Click Start receiving. On the other PC, choose 'Connect to other PC' and enter this IP and code. Both PCs can use their mice."
    };
    private readonly Button _enterCode = new DarkButton()
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
    private readonly ComboBox _edge = new DarkComboBox() { Name = "ScreenEdge", Left = 28, Top = 345, Width = 480, DropDownStyle = ComboBoxStyle.DropDownList };

    private readonly Button _toggle = new DarkButton() { Name = "ToggleConnection", Left = 28, Top = 403, Width = 480, Height = 48 };
    private readonly CheckBox _autoConnect = new DarkCheckBox() { Name = "AutoConnect", Left = 28, Top = 468, Width = 480, Height = 25, Text = "Connect / listen automatically when opened" };
    private readonly CheckBox _startup = new DarkCheckBox() { Name = "StartWithWindows", Left = 28, Top = 502, Width = 480, Height = 25, Text = "Start with Windows" };
    private readonly CheckBox _minimizeTray = new DarkCheckBox() { Name = "MinimizeToTray", Left = 28, Top = 536, Width = 480, Height = 25, Text = "Keep running in the notification area when closed" };
    private readonly Label _status = new() { Name = "ConnectionStatus", Left = 28, Top = 585, Width = 480, Height = 45 };
    private readonly Label _help = new() { Left = 28, Top = 635, Width = 480, Height = 46 };
    private readonly LinkLabel _logLink = new() { Left = 28, Top = 802, Width = 170, Text = "Open diagnostic log", LinkColor = Accent, ActiveLinkColor = Foreground };
    private readonly Button _keys = new DarkButton { Name = "OpenKeyRemapping", Left = 28, Top = 701, Width = 194, Height = 36, Text = "Reasignar teclas..." };
    private readonly Label _keyStatus = new() { Name = "KeyRemappingStatus", Left = 210, Top = 797, Width = 298, Height = 38 };
    private readonly Button _update = new DarkButton()
    {
        Name = "CheckForUpdates", Left = 232, Top = 701, Width = 276, Height = 36,
        Text = "Check && install updates"
    };
    private readonly Label _updateStatus = new()
    {
        Name = "UpdateStatus", Left = 28, Top = 746, Width = 480, Height = 44
    };
    private readonly Button _elevate = new DarkButton
    {
        Name = "RestartAsAdministrator", Left = 28, Top = 846, Width = 480, Height = 36,
        Text = "Restart as administrator..."
    };
    private readonly Label _privilegeStatus = new()
    {
        Name = "PrivilegeStatus", Left = 28, Top = 890, Width = 480, Height = 56
    };

    private readonly NotifyIcon _tray;
    private readonly Icon _appIcon = AppBranding.LoadIcon(SystemInformation.IconSize);
    private readonly Icon _trayIcon = AppBranding.LoadIcon(SystemInformation.SmallIconSize);
    private readonly PictureBox _brand = new() { Name = "AppLogo", Left = 28, Top = 24, Width = 48, Height = 48, SizeMode = PictureBoxSizeMode.Zoom };
    private CancellationTokenSource? _runCts;
    private CancellationTokenSource? _updateCts;
    private TcpListener? _listener;
    private volatile MousePeer? _peer;
    private MouseCapture? _capture;
    private MouseSharingSession? _session;
    private bool _shuttingDown;
    private bool _initializing = true;
    private bool _exitRequested;
    private bool _editingKeys;

    private bool IsReceiver => _receiverRole.Checked;
    private bool ExitRight => _edge.SelectedIndex != 1;

    internal MainWindow(bool launchedAtStartup = false, bool resumeSharing = false)
        : this(SettingsStore.Load(), SettingsStore.Save, launchedAtStartup, resumeSharing: resumeSharing,
            configureStartup: WindowsStartup.SetEnabled) { }

    internal MainWindow(AppSettings settings, Action<AppSettings> saveSettings, bool launchedAtStartup = false,
        Func<TcpListener>? listenerFactory = null, IAppUpdater? updater = null,
        Func<Action<string>, IKeyboardRemappingService>? keyboardFactory = null,
        bool resumeSharing = false, bool? elevated = null, Action<bool>? restartElevated = null,
        Action<bool>? configureStartup = null)
    {
        _settings = settings;
        _saveSettings = saveSettings;
        // The production constructor supplies the registry writer; UI tests stay isolated.
        _configureStartup = configureStartup ?? (_ => { });
        _listenerFactory = listenerFactory ?? (() => new TcpListener(IPAddress.Any, MousePeer.Port));
        _updater = updater ?? new GitHubAppUpdater();
        _keyboardFactory = keyboardFactory ?? (error => new KeyboardRemappingService(error));
        _launchedAtStartup = launchedAtStartup;
        _resumeSharing = resumeSharing;
        _restartElevated = restartElevated ?? WindowsElevation.Restart;
        bool isElevated = elevated ?? WindowsElevation.IsElevated();
        Text = "Mouse, with out borders";
        Icon = _appIcon;
        using (Icon logo = AppBranding.LoadIcon(new Size(64, 64))) _brand.Image = logo.ToBitmap();
        ClientSize = new Size(536, 956);
        AutoScroll = true;
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
            Left = 88, Top = 20, Width = 420, Height = 42,
            Font = new Font("Segoe UI Semibold", 19, FontStyle.Bold), ForeColor = Foreground
        };
        Label subtitle = new()
        {
            Text = "All your mice and touchpads. Two Windows computers.",
            Left = 88, Top = 65, Width = 420, Height = 30, ForeColor = Muted
        };
        Label modeLabel = new() { Text = "PAIR THESE TWO PCS", Left = 28, Top = 96, Width = 480 };
        Label footer = new()
        {
            Text = "Private LAN only  •  TCP 42187  •  No keyboard sharing",
            Left = 28, Top = 681, Width = 480, Height = 20, ForeColor = Muted,
            Font = new Font("Segoe UI", 9)
        };

        Controls.AddRange([
            _brand, title, subtitle, modeLabel, _controllerRole, _receiverRole,
            _receiverIp, _receiverKeyLabel, _receiverKey, _copy, _receiverSteps, _enterCode,
            _hostLabel, _host, _hostKeyLabel, _hostKey, _edgeLabel, _edge,
            _toggle, _autoConnect, _startup, _minimizeTray, _status, _help, footer, _logLink,
            _update, _updateStatus, _keys, _keyStatus, _elevate, _privilegeStatus
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
            check.UseVisualStyleBackColor = false;
        }

        _toggle.FlatStyle = FlatStyle.Flat;
        _toggle.FlatAppearance.BorderSize = 0;
        _toggle.BackColor = Accent;
        _toggle.ForeColor = Color.FromArgb(13, 27, 42);
        _toggle.Font = new Font("Segoe UI Semibold", 11, FontStyle.Bold);
        foreach (Button button in new[] { _copy, _enterCode, _update, _keys, _elevate })
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
        string? startupError = null;
        try
        {
            _configureStartup(_settings.StartWithWindows);
            _startup.Checked = _settings.StartWithWindows;
        }
        catch (Exception ex)
        {
            startupError = "Cannot change Windows startup: " + ex.Message;
            SettingsStore.Log(startupError);
        }

        try
        {
            string ips = string.Join(", ", Dns.GetHostAddresses(Dns.GetHostName())
                .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                .Select(ip => ip.ToString()));
            _receiverIp.Text = "Local IP: " + (ips.Length == 0 ? "See ipconfig in Windows" : ips) +
                               "\nListen port: " + MousePeer.Port;
        }
        catch (SocketException) { _receiverIp.Text = "Local IP: See ipconfig in Windows"; }

        ContextMenuStrip trayMenu = new()
        {
            BackColor = Surface, ForeColor = Foreground,
            Renderer = new DarkMenuRenderer(), ShowImageMargin = false,
            Font = new Font("Segoe UI", 10)
        };
        trayMenu.Items.Add("Open", null, (_, _) => ShowWindow());
        trayMenu.Items.Add("Stop mouse sharing", null, (_, _) => Stop());
        trayMenu.Items.Add("Reasignar teclas...", null, (_, _) => EditKeyMappings());
        trayMenu.Items.Add("Restart as administrator...", null, (_, _) => RestartAsAdministrator()).Enabled = !isElevated;
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Exit", null, (_, _) => ExitApplication());
        _tray = new NotifyIcon
        {
            Icon = _trayIcon,
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
            try
            {
                _configureStartup(_startup.Checked);
                _settings.StartWithWindows = _startup.Checked;
                SavePreferences();
            }
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
            SetStatus("Code copied. On the other PC, choose 'Connect to other PC' and enter it there.");
        };
        _toggle.Click += (_, _) => Toggle();
        _update.Click += async (_, _) => await UpdateApplicationAsync();
        _keys.Click += (_, _) => EditKeyMappings();
        _elevate.Enabled = !isElevated;
        _elevate.Text = isElevated ? "Running as administrator" : "Restart as administrator...";
        _elevate.Click += (_, _) => RestartAsAdministrator();
        _privilegeStatus.ForeColor = Muted;
        _privilegeStatus.Font = new Font("Segoe UI", 9);
        _privilegeStatus.Text = isElevated
            ? "Administrator mode is active for this session. UAC prompts and the locked screen still require a local device."
            : "Task Manager not responding to the remote mouse? Restart MWB as administrator on that PC and approve UAC with its local mouse.";
        _keyStatus.ForeColor = Muted;
        _keyStatus.Font = new Font("Segoe UI", 9);
        _keyStatus.Text = "Reasignación de teclas desactivada.";
        _updateStatus.ForeColor = Muted;
        _updateStatus.Font = new Font("Segoe UI", 9);
        _updateStatus.Text = $"Version {_updater.CurrentVersion} • Manual updates from GitHub.";
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
        _status.Text = startupError ?? "Not connected.";
        ShowMode();
    }

    private async Task UpdateApplicationAsync()
    {
        if (_updateCts is not null || _shuttingDown) return;
        using CancellationTokenSource lifetime = new(TimeSpan.FromMinutes(15));
        _updateCts = lifetime;
        _update.Enabled = false;
        _updateStatus.Text = "Checking GitHub for updates...";
        try
        {
            AppRelease? release = await _updater.CheckAsync(lifetime.Token);
            lifetime.Token.ThrowIfCancellationRequested();
            if (release is null)
            {
                _updateStatus.Text = $"Version {_updater.CurrentVersion} is up to date.";
                return;
            }
            _updateStatus.Text = $"Downloading {release.Version}... 0%";
            Progress<int> progress = new(percent =>
            {
                if (!_shuttingDown && _updateCts == lifetime)
                    _updateStatus.Text = $"Downloading {release.Version}... {percent}% • Verifying SHA-256";
            });
            SavePreferences();
            await _updater.InstallAsync(release, progress, lifetime.Token);
            // Exit only after Windows successfully starts the verified installer.
            if (!_shuttingDown) ExitApplication();
        }
        catch (OperationCanceledException)
        {
            if (!_shuttingDown) _updateStatus.Text = "Update timed out. Check your connection and try again.";
        }
        catch (Exception ex)
        {
            SettingsStore.Log("Manual update: " + ex.Message);
            if (!_shuttingDown) _updateStatus.Text = "Update failed: " + ex.Message;
        }
        finally
        {
            _updateCts = null;
            if (!_shuttingDown) _update.Enabled = true;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Height = Math.Min(Height, Screen.FromControl(this).WorkingArea.Height - 32);
        BeginInvoke((MethodInvoker)(() =>
        {
            ApplyKeyMappings();
            if (_launchedAtStartup) Hide();
            if ((_autoConnect.Checked || _resumeSharing) && _runCts is null && HasConnectionDetails()) Toggle();
        }));
    }

    private bool HasConnectionDetails()
    {
        if (IsReceiver) return true;
        if (string.IsNullOrWhiteSpace(_host.Text)) return false;
        try { MousePeer.ParseKey(_hostKey.Text); return true; }
        catch (ArgumentException) { return false; }
    }

    private void ApplyKeyMappings()
    {
        if (_shuttingDown) return;
        try
        {
            _settings.KeyMappings ??= [];
            KeyCatalog.Validate(_settings.KeyMappings);
            if (_settings.KeyRemappingEnabled && _settings.KeyMappings.Count != 0)
                _keyboard ??= _keyboardFactory(error => Ui(() =>
                {
                    IKeyboardRemappingService? failed = _keyboard;
                    _keyboard = null;
                    failed?.Dispose();
                    _settings.KeyRemappingEnabled = false;
                    _keyStatus.Text = "Reasignación pausada: " + error;
                    SettingsStore.Log("Key remapping failed: " + error);
                }));
            _keyboard?.Configure(_settings.KeyRemappingEnabled, _settings.KeyMappings);
            _keyStatus.Text = _settings.KeyRemappingEnabled && _settings.KeyMappings.Count != 0
                ? $"{_settings.KeyMappings.Count} reasignación(es) activas en este PC."
                : "Reasignación de teclas desactivada.";
        }
        catch (Exception ex) { _keyStatus.Text = "No se pudo activar: " + ex.Message; }
    }

    private void EditKeyMappings()
    {
        if (_editingKeys || _shuttingDown) return;
        _editingKeys = true;
        bool previousEnabled = _settings.KeyRemappingEnabled;
        List<KeyMapping> previousMappings = _settings.KeyMappings?.ToList() ?? [];
        string? errorMessage = null;
        try
        {
            _keyboard?.Configure(false, []);
            using KeyRemappingDialog dialog = new(previousEnabled, previousMappings);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            _settings.KeyMappings = dialog.Mappings.ToList();
            _settings.KeyRemappingEnabled = dialog.RemappingEnabled;
            try { _saveSettings(_settings); }
            catch
            {
                _settings.KeyMappings = previousMappings;
                _settings.KeyRemappingEnabled = previousEnabled;
                throw;
            }
        }
        catch (Exception ex) { errorMessage = "No se pudo guardar: " + ex.Message; }
        finally
        {
            _editingKeys = false;
            ApplyKeyMappings();
            if (errorMessage is not null) _keyStatus.Text = errorMessage;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int enabled = 1;
        DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int));
    }
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

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
        _help.Text = "Once paired, every mouse and touchpad on either PC can cross the shared edge. The last device moved takes control.";
    }

    private bool SavePreferences()
    {
        if (_initializing) return false;
        _settings.Role = IsReceiver ? "Receiver" : "Controller";
        _settings.Host = _host.Text.Trim();
        _settings.RemoteKey = MousePeer.NormalizeKey(_hostKey.Text);
        _settings.PairingKey = _receiverKey.Text.Trim();
        _settings.ExitEdge = ExitRight ? "Right" : "Left";
        _settings.AutoConnect = _autoConnect.Checked;
        _settings.MinimizeToTray = _minimizeTray.Checked;
        try { _saveSettings(_settings); return true; }
        catch (Exception ex)
        {
            SettingsStore.Log("Save settings: " + ex.Message);
            SetStatus("Could not save settings: " + ex.Message);
            return false;
        }
    }

    private void RestartAsAdministrator()
    {
        if (_shuttingDown || _updateCts is not null || !_elevate.Enabled) return;
        if (!SavePreferences()) return;
        try
        {
            _restartElevated(_runCts is not null);
            ExitApplication();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            _privilegeStatus.Text = "Administrator restart cancelled. MWB is still running with its current connection.";
        }
        catch (Exception ex)
        {
            _privilegeStatus.Text = "Could not restart as administrator: " + ex.Message;
            SettingsStore.Log("Administrator restart: " + ex.Message);
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
                SetStatus("Listening. Waiting for the other PC...");
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
            string? inputErrorStatus = null;
            try
            {
                activePeer = await MousePeer.ConnectAsync(host, key, lifetime.Token).ConfigureAwait(false);
                connectedOnce = true;
                _peer = activePeer;
                await RunSharingAsync(activePeer, initiator: true, lifetime).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                SettingsStore.Log("Controller connection: " + ex.Message);
                if (ex is Win32Exception and not SocketException) inputErrorStatus = "Mouse input failed: " + ex.Message;
                if (!connectedOnce || ex is NotSupportedException)
                {
                    string message = "Could not connect: " + ex.Message;
                    if (!_settings.AutoConnect || ex is NotSupportedException)
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
                            SetStatus("Other PC not ready. Retrying connection...");
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
                    SetStatus(inputErrorStatus ?? (connectedOnce
                        ? "Connection lost. Trying to reconnect..."
                        : "Other PC not ready. Retrying connection..."));
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
                    string disconnectedStatus = "Waiting for the other PC to reconnect...";
                    try
                    {
                        await RunSharingAsync(peer, initiator: false, lifetime).ConfigureAwait(false);
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

    private async Task RunSharingAsync(MousePeer peer, bool initiator, CancellationTokenSource lifetime)
    {
        MouseSharingSession? session = null;
        Exception? inputFailure = null;
        Ui(() =>
        {
            if (_runCts != lifetime || !ReferenceEquals(_peer, peer)) return;
            try
            {
                CloseSharingSession();
                session = new MouseSharingSession(initiator, ExitRight, new WindowsMouseDesktop(),
                    peer.Queue, ex => SettingsStore.Log("Release mouse button: " + ex.Message), SetStatus);
                _session = session;
                _capture = new MouseCapture(OnMouseHook);
                session.Start();
            }
            catch (Exception ex)
            {
                Volatile.Write(ref inputFailure, ex);
                CloseSharingSession();
                SetStatus("Mouse capture error: " + ex.Message);
                peer.Dispose();
            }
        });
        try
        {
            await peer.ReadLoopAsync(packet => Ui(() =>
            {
                if (session is null || !ReferenceEquals(_session, session)) return;
                try { session.Receive(packet); }
                catch (Exception ex)
                {
                    Volatile.Write(ref inputFailure, ex);
                    CloseSharingSession();
                    SetStatus("Mouse input failed: " + ex.Message);
                    peer.Dispose();
                }
            }), lifetime.Token).ConfigureAwait(false);
        }
        catch (Exception) when (Volatile.Read(ref inputFailure) is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(Volatile.Read(ref inputFailure)!);
            throw;
        }
        finally
        {
            Ui(() =>
            {
                if (session is not null && ReferenceEquals(_session, session)) CloseSharingSession();
            });
        }
    }

    private void CloseSharingSession()
    {
        MouseSharingSession? session = _session;
        _session = null;
        try { session?.Dispose(); }
        catch (Exception ex) { SettingsStore.Log("Release local mouse: " + ex.Message); }
        _capture?.Dispose();
        _capture = null;
    }

    private static Rectangle GetDesktopBounds()
    {
        Rectangle result = Screen.AllScreens[0].Bounds;
        foreach (Screen screen in Screen.AllScreens.Skip(1))
            result = Rectangle.Union(result, screen.Bounds);
        return result;
    }

    private sealed class WindowsMouseDesktop : IMouseDesktop
    {
        private HiddenPointerWindow? _hiddenPointer;
        public void SetPointerHidden(bool hidden)
        {
            if (!hidden)
            {
                _hiddenPointer?.Dispose();
                _hiddenPointer = null;
            }
            else if (_hiddenPointer is null)
            {
                Rectangle bounds = (Screen.PrimaryScreen ?? Screen.AllScreens[0]).Bounds;
                Point anchor = new(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
                _hiddenPointer = new HiddenPointerWindow(anchor);
                _hiddenPointer.HidePointer();
                MoveCursor(anchor);
            }
        }
        public Rectangle Bounds => GetDesktopBounds();
        public Point Cursor => NativeMouse.GetCursorPos(out var position)
            ? new Point(position.X, position.Y) : throw new Win32Exception("Cannot read the mouse position.");
        public void MoveCursor(Point position) => NativeMouse.MoveCursor(position, Bounds);
        public void Inject(uint flags, int data) => NativeMouse.Inject(flags, data);
    }

    private bool OnMouseHook(uint message, NativeMouse.HookInfo info)
    {
        MouseSharingSession? session = _session;
        if (session is null) return false;
        try
        {
            if (message == NativeMouse.WM_MOUSEMOVE)
            {
                if (!NativeMouse.GetCursorPos(out var previous)) return session.IsRemote;
                return session.PhysicalMove(new Point(previous.X, previous.Y), new Point(info.Position.X, info.Position.Y));
            }
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
            if (flag == 0) return session.IsRemote;
            int data = flag is NativeMouse.WHEEL or NativeMouse.HWHEEL
                ? (short)((info.MouseData >> 16) & 0xFFFF)
                : flag is NativeMouse.XDOWN or NativeMouse.XUP ? (int)((info.MouseData >> 16) & 0xFFFF) : 0;
            return flag is NativeMouse.WHEEL or NativeMouse.HWHEEL
                ? session.PhysicalWheel(flag, data) : session.PhysicalButton(flag, data);
        }
        catch (Exception ex)
        {
            CloseSharingSession();
            _peer?.Dispose();
            SetStatus("Mouse sharing stopped: " + ex.Message);
            return false;
        }
    }

    protected override void WndProc(ref Message message)
    {
        if (InstallerExitMessage != 0 && (uint)message.Msg == InstallerExitMessage)
        {
            ExitApplication();
            return;
        }
        base.WndProc(ref message);
    }

    private void Stop()
    {
        _runCts?.Cancel();
        _runCts = null;
        _listener?.Stop();
        _listener = null;
        CloseSharingSession();
        _peer?.Dispose();
        _peer = null;
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
        _keyboard?.Dispose();
        _keyboard = null;
        _updateCts?.Cancel();
        Stop();
        _tray.Visible = false;
        _tray.Dispose();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_iconsDisposed)
        {
            _keyboard?.Dispose();
            _keyboard = null;
            _iconsDisposed = true;
            _tray?.Dispose();
            _brand.Image?.Dispose();
            _appIcon.Dispose();
            _trayIcon.Dispose();
        }
        base.Dispose(disposing);
    }
    private bool _iconsDisposed;
}
