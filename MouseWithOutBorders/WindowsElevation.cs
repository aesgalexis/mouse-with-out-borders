using System.Diagnostics;
using System.Security.Principal;

namespace MouseWithOutBorders;

internal static class WindowsElevation
{
    internal static bool IsElevated()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    internal static void Restart(bool resumeSharing)
    {
        string path = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot locate the application executable.");
        ProcessStartInfo start = new(path)
        {
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = AppContext.BaseDirectory
        };
        start.ArgumentList.Add("--restart-from");
        start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (resumeSharing) start.ArgumentList.Add("--resume-sharing");
        using Process child = Process.Start(start)
            ?? throw new InvalidOperationException("Windows did not start the elevated application.");
    }

    // The new process must wait before acquiring the singleton mutex or opening
    // the listener. The old process only exits after the UAC launch succeeds.
    internal static void WaitForPreviousInstance(string[] args)
    {
        int index = Array.IndexOf(args, "--restart-from");
        if (index < 0) return;
        if (index + 1 >= args.Length || !int.TryParse(args[index + 1], out int id) ||
            id <= 0 || id == Environment.ProcessId)
            throw new ArgumentException("Invalid restart process.");
        Process previous;
        try { previous = Process.GetProcessById(id); }
        catch (ArgumentException) { return; } // Already exited.
        using (previous)
        {
            if (!previous.WaitForExit(15000))
                throw new TimeoutException("The previous instance did not exit. Close it and try again.");
        }
    }
}
