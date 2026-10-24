using System.Runtime.InteropServices;
using System.Text.Json;

namespace MouseWithOutBorders;

internal static class KeyboardUiTests
{
    private sealed class FakeKeyboard : IKeyboardRemappingService
    {
        internal bool Enabled, Disposed;
        internal KeyMapping[] Mappings = [];
        public void Configure(bool enabled, IReadOnlyList<KeyMapping> mappings) { Enabled = enabled; Mappings = mappings.ToArray(); }
        public void Dispose() => Disposed = true;
    }
    private static T Find<T>(Control root, string name) where T : Control => root.Controls.Find(name, true).OfType<T>().Single();
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Choose(ComboBox combo, int code) => combo.SelectedItem = combo.Items.Cast<KeyChoice>().Single(key => key.Code == code);

    internal static void Run(string? screenshotDirectory)
    {
        AppSettings settings = new() { MinimizeToTray = false, AutoConnect = false };
        string? saved = null;
        FakeKeyboard keyboard = new();
        using (MainWindow window = new(settings, value => saved = JsonSerializer.Serialize(value),
            listenerFactory: () => new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0), keyboardFactory: _ => keyboard))
        {
            window.Location = new(-20000, -20000);
            window.StartPosition = FormStartPosition.Manual;
            window.ShowInTaskbar = false;
            window.Show(); Application.DoEvents();
            Assert(!keyboard.Enabled, "An existing user's keys must not change until they configure remapping.");
            Edit(window, dialog =>
            {
                ComboBox source = Find<ComboBox>(dialog, "SourceKey");
                ComboBox target = Find<ComboBox>(dialog, "TargetKey");
                Assert(source.DropDownStyle == ComboBoxStyle.DropDownList && target.DropDownStyle == ComboBoxStyle.DropDownList,
                    "Both keys must be selectable without typing Enter or Backspace.");
                Choose(source, 122); Choose(target, 8); Find<Button>(dialog, "AddKeyMapping").PerformClick();
                Choose(source, 123); Choose(target, 13); Find<Button>(dialog, "AddKeyMapping").PerformClick();
                ListView list = Find<ListView>(dialog, "KeyMappings");
                Assert(list.Items.Count == 2, "The selectors could not add both broken-key replacements.");
                Find<Button>(dialog, "AddKeyMapping").PerformClick();
                Assert(list.Items.Count == 2, "A duplicate source was accepted.");
                list.Items[0].Selected = true;
                Choose(source, 121);
                Find<Button>(dialog, "ReplaceKeyMapping").PerformClick();
                Assert(list.Items[0].Text == "F10", "Editing the selected mapping failed.");
                list.Items[0].Selected = true;
                Find<Button>(dialog, "RemoveKeyMapping").PerformClick();
                Assert(list.Items.Count == 1, "Removing the selected mapping failed.");
                Find<Button>(dialog, "BrokenKeyPreset").PerformClick();
                Assert(list.Items.Count == 2, "The mouse-only Enter/Backspace preset was not added.");
                if (screenshotDirectory is not null)
                {
                    Directory.CreateDirectory(screenshotDirectory);
                    using Bitmap bitmap = new(dialog.Width, dialog.Height);
                    dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    bitmap.Save(Path.Combine(screenshotDirectory, "key-remapping.png"));
                }
                Find<Button>(dialog, "SaveKeyMappings").PerformClick();
            });
            Assert(keyboard.Enabled && keyboard.Mappings.SequenceEqual(new[] { new KeyMapping(122, 8), new KeyMapping(123, 13) }),
                "Saving did not activate the selected mappings.");
            Assert(saved is not null && JsonSerializer.Deserialize<AppSettings>(saved)!.KeyMappings.SequenceEqual(keyboard.Mappings),
                "Mappings were not saved for the next launch.");
            string before = saved!;
            Edit(window, dialog =>
            {
                Assert(!keyboard.Enabled, "Opening the editor did not pause remapping.");
                Find<CheckBox>(dialog, "EnableKeyRemapping").Checked = false;
                Find<Button>(dialog, "CancelKeyMappings").PerformClick();
            });
            Assert(keyboard.Enabled && saved == before, "Cancel changed settings or left remapping paused.");
            Find<Button>(window, "ToggleConnection").PerformClick();
            Find<Button>(window, "ToggleConnection").PerformClick();
            Assert(keyboard.Enabled && !keyboard.Disposed, "Stopping mouse sharing disabled local key remapping.");
            window.Close();
            Assert(keyboard.Disposed, "Exiting MWB did not stop keyboard remapping.");
        }
        AppSettings restored = JsonSerializer.Deserialize<AppSettings>(saved!)!;
        FakeKeyboard restarted = new();
        using (MainWindow window = new(restored, _ => { }, keyboardFactory: _ => restarted))
        {
            window.ShowInTaskbar = false;
            window.StartPosition = FormStartPosition.Manual;
            window.Location = new(-20000, -20000);
            window.Show(); Application.DoEvents();
            Assert(restarted.Enabled && restarted.Mappings.Count() == 2, "Saved mappings were not applied on startup.");
            window.Close();
        }
        Console.WriteLine("PASS: mouse-only source/target selectors, add/edit/remove, duplicate rejection, preset, save/cancel, startup and independence from mouse sharing.");
    }
    private static void Edit(MainWindow owner, Action<KeyRemappingDialog> action)
    {
        Exception? error = null;
        using System.Windows.Forms.Timer timer = new() { Interval = 30 };
        timer.Tick += (_, _) =>
        {
            KeyRemappingDialog? dialog = Application.OpenForms.OfType<KeyRemappingDialog>().FirstOrDefault();
            if (dialog is null) return;
            timer.Stop();
            try { action(dialog); }
            catch (Exception ex) { error = ex; dialog.Close(); }
        };
        timer.Start();
        Find<Button>(owner, "OpenKeyRemapping").PerformClick();
        if (error is not null) throw error;
    }

    internal static void RunNative()
    {
        List<(int key, bool down)> observed = [];
        List<string> errors = [];
        using KeyboardRemappingService service = new(error => errors.Add(error));
        service.Configure(true, [new(134, 13), new(135, 8)]);
        service.FlushAsync().GetAwaiter().GetResult();
        // Intercept every generated Enter/Backspace before it reaches a user's
        // window. Test source keys are the otherwise unused F23 and F24.
        NativeMouse.HookProcedure observer = (code, message, pointer) =>
        {
            if (code >= 0)
            {
                NativeKeyboard.HookInfo key = Marshal.PtrToStructure<NativeKeyboard.HookInfo>(pointer);
                if (key.ExtraInfo == NativeKeyboard.Marker && key.Key is 8 or 13)
                {
                    observed.Add(((int)key.Key, (key.Flags & 0x80) == 0));
                    return (IntPtr)1;
                }
            }
            return NativeMouse.CallNextHookEx(IntPtr.Zero, code, message, pointer);
        };
        IntPtr hook = NativeMouse.SetWindowsHookEx(13, observer, GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero) throw new Exception("Could not install the test-only keyboard output interceptor.");
        try
        {
            foreach (var input in new[] { (134, true), (134, false), (135, true), (135, true), (135, false) })
                NativeKeyboard.Inject(input.Item1, input.Item2, (count, keys, size) =>
                {
                    keys[0].Data.Keyboard.ExtraInfo = (IntPtr)0x544B4559;
                    return SendInput(count, keys, size);
                });
            DateTime deadline = DateTime.UtcNow.AddSeconds(3);
            while (observed.Count < 5 && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(5); }
            Assert(errors.Count == 0 && observed.SequenceEqual(new[] { (13, true), (13, false), (8, true), (8, true), (8, false) }),
                "Native keyboard capture/injection lost a transition, repeat or generated an input loop.");
        }
        finally { NativeMouse.UnhookWindowsHookEx(hook); GC.KeepAlive(observer); }
        Console.WriteLine("PASS: native keyboard hook remaps Enter/Backspace with repeat; output was intercepted and never typed into user windows.");
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, [In] NativeMouse.Input[] input, int size);
}
