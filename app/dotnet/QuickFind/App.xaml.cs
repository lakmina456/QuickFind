using System.Windows;
using QuickFind.Services;

namespace QuickFind;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if (!SingleInstance.TryAcquire())
        {
            Shutdown();
            return;
        }
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SingleInstance.Release();
        base.OnExit(e);
    }
}
