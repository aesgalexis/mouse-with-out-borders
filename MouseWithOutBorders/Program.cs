namespace MouseWithOutBorders;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using Mutex singleton = new(true, @"Local\MouseWithOutBorders-79DBA1BD", out bool created);
        if (!created)
        {
            MessageBox.Show("Mouse, with out borders is already running.\nCheck the Windows notification area.",
                "Mouse, with out borders", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainWindow(args.Contains("--autostart", StringComparer.OrdinalIgnoreCase)));
    }
}
