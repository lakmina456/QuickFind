using System.Diagnostics;
using System.Windows;

namespace QuickFind.Services;

public static class Elevation
{
    public static void RelaunchAsAdmin()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return;
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = true,
            Verb = "runas"
        };
        try
        {
            Process.Start(psi);
            Application.Current?.Shutdown();
        }
        catch
        {
            // User cancelled UAC
        }
    }
}
