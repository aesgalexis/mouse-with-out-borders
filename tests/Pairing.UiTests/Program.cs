namespace MouseWithOutBorders;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        AppSettings settings = new() { MinimizeToTray = false };
        int saves = 0;
        using MainWindow window = new(settings, _ => saves++, listenerFactory:
            () => new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0));
        window.ShowInTaskbar = false;
        window.StartPosition = FormStartPosition.Manual;
        window.Location = new Point(-20000, -20000);
        window.Show();
        Application.DoEvents();

        RadioButton controller = Find<RadioButton>(window, "ControllerRole");
        RadioButton receiver = Find<RadioButton>(window, "ReceiverRole");
        Button enterCode = Find<Button>(window, "EnterOtherPcCode");
        TextBox host = Find<TextBox>(window, "ReceiverHost");
        TextBox remoteCode = Find<TextBox>(window, "RemotePairingCode");
        TextBox localCode = Find<TextBox>(window, "LocalPairingCode");
        Button toggle = Find<Button>(window, "ToggleConnection");

        if (!controller.Visible || !receiver.Visible || !receiver.Checked || !enterCode.Visible ||
            !localCode.Visible || localCode.Text.Length != 8 || !localCode.ReadOnly || remoteCode.Visible)
            throw new Exception("Receiver must show both role choices, a short code and an obvious enter-code action.");
        SaveScreenshot(window, args, "receiver.png");

        enterCode.PerformClick();
        if (!controller.Checked || !host.Visible || !remoteCode.Visible || remoteCode.ReadOnly ||
            localCode.Visible || enterCode.Visible || settings.Role != "Controller" || !host.Focused)
            throw new Exception("Enter-code action did not reveal editable connection fields and focus the IP.");
        SaveScreenshot(window, args, "controller.png");

        host.Text = "127.0.0.1";
        remoteCode.Text = "abcd23xy";
        receiver.Checked = true;
        if (settings.RemoteKey != "ABCD23XY" || settings.Host != "127.0.0.1")
            throw new Exception("Switching roles lost the entered connection details.");
        enterCode.PerformClick();
        if (!remoteCode.Focused || remoteCode.Text != "ABCD23XY")
            throw new Exception("Returning to connect must preserve and focus the code when an IP is already entered.");

        receiver.Checked = true;
        toggle.PerformClick();
        if (controller.Enabled || receiver.Enabled || !enterCode.Enabled)
            throw new Exception("An active receiver must still offer the enter-code action. Status: " +
                Find<Label>(window, "ConnectionStatus").Text);
        enterCode.PerformClick();
        if (!controller.Checked || !controller.Enabled || !receiver.Enabled || !remoteCode.Visible ||
            toggle.Text != "Connect mouse")
            throw new Exception("Enter-code action must stop listening and switch to the connecting role.");

        window.Close();
        if (saves == 0) throw new Exception("Pairing preferences were not saved.");
        Console.WriteLine("PASS: receiver, enter-code action, editable code, role persistence and switching while listening.");
    }

    private static T Find<T>(Control parent, string name) where T : Control =>
        parent.Controls.Find(name, true).OfType<T>().Single();

    private static void SaveScreenshot(Form window, string[] args, string name)
    {
        if (args.Length != 2 || args[0] != "--screenshots") return;
        Directory.CreateDirectory(args[1]);
        using Bitmap bitmap = new(window.Width, window.Height);
        window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        bitmap.Save(Path.Combine(args[1], name));
    }
}
