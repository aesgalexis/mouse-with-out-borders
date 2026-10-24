using System.ComponentModel;
using System.Net;
using System.Net.Sockets;

namespace MouseWithOutBorders;

internal static class ElevationUiTests
{
    internal static void Run()
    {
        AppSettings settings = new() { MinimizeToTray = true, AutoConnect = false };
        int saves = 0;
        int launches = 0;
        bool fail = true;
        using (MainWindow window = new(settings, _ => saves++, elevated: false,
            listenerFactory: () => new TcpListener(IPAddress.Loopback, 0),
            restartElevated: resume =>
            {
                launches++;
                Assert(resume, "An active listener must resume after elevation even with AutoConnect off.");
                Assert(saves > 0, "Save the current pairing preferences before restarting.");
                if (fail) throw new Win32Exception(1223);
            }))
        {
            Show(window);
            Button restart = Find<Button>(window, "RestartAsAdministrator");
            Label privilege = Find<Label>(window, "PrivilegeStatus");
            Assert(restart.Enabled && privilege.Text.Contains("Task Manager"),
                "Standard mode must explain the restriction and offer an explicit restart.");
            Find<Button>(window, "ToggleConnection").PerformClick();
            restart.PerformClick();
            Assert(launches == 1 && !window.IsDisposed && privilege.Text.Contains("cancelled") &&
                !Find<RadioButton>(window, "ReceiverRole").Enabled,
                "Cancelling UAC must keep the original app and listener running.");
            fail = false;
            restart.PerformClick();
            Assert(launches == 2 && window.IsDisposed && !settings.AutoConnect,
                "Successful restart must exit even in tray mode without changing AutoConnect.");
        }

        using (MainWindow window = new(new AppSettings { MinimizeToTray = false, AutoConnect = false }, _ => { },
            elevated: true, restartElevated: _ => throw new Exception("Already elevated.")))
        {
            Show(window);
            Assert(!Find<Button>(window, "RestartAsAdministrator").Enabled &&
                Find<Label>(window, "PrivilegeStatus").Text.Contains("active"),
                "Administrator mode must be visible and prevent another elevation request.");
            window.Close();
        }

        using (MainWindow window = new(new AppSettings { MinimizeToTray = false, AutoConnect = false }, _ => { },
            elevated: false, restartElevated: _ => throw new IOException("Launch failed.")))
        {
            Show(window);
            Find<Button>(window, "RestartAsAdministrator").PerformClick();
            Assert(!window.IsDisposed && Find<Label>(window, "PrivilegeStatus").Text.Contains("Launch failed"),
                "Launch failures must preserve the app and offer retry.");
            window.Close();
        }

        using (MainWindow window = new(new AppSettings { MinimizeToTray = false, AutoConnect = false },
            _ => throw new IOException("Save failed."), elevated: false,
            restartElevated: _ => throw new Exception("Must not restart after save failure.")))
        {
            Show(window);
            Find<Button>(window, "RestartAsAdministrator").PerformClick();
            Assert(!window.IsDisposed && Find<Label>(window, "ConnectionStatus").Text.Contains("Save failed"),
                "A failed settings save must prevent restarting.");
            window.Close();
        }

        using (MainWindow window = new(new AppSettings { MinimizeToTray = false, AutoConnect = false }, _ => { },
            listenerFactory: () => new TcpListener(IPAddress.Loopback, 0), resumeSharing: true, elevated: true))
        {
            Show(window);
            Assert(!Find<RadioButton>(window, "ReceiverRole").Enabled,
                "A restarted process must reopen the listener without changing AutoConnect.");
            window.Close();
        }
        WindowsElevation.WaitForPreviousInstance([]);
        WindowsElevation.WaitForPreviousInstance(["--restart-from", int.MaxValue.ToString()]);
        Console.WriteLine("PASS: explicit administrator restart, UAC cancellation, launch/save failures, tray exit and session resumption.");
    }

    private static void Show(MainWindow window)
    {
        window.ShowInTaskbar = false;
        window.StartPosition = FormStartPosition.Manual;
        window.Location = new Point(-20000, -20000);
        window.Show();
        Application.DoEvents();
    }

    private static T Find<T>(Control parent, string name) where T : Control =>
        parent.Controls.Find(name, true).OfType<T>().Single();

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
