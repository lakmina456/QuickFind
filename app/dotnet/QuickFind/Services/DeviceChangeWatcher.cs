using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace QuickFind.Services;

/// <summary>Listen for drive arrive/remove via WM_DEVICECHANGE.</summary>
public sealed class DeviceChangeWatcher : IDisposable
{
    const int WmDeviceChange = 0x0219;
    const int DbtDeviceArrival = 0x8000;
    const int DbtDeviceRemoveComplete = 0x8004;
    const int DbtDevNodesChanged = 0x0007;

    readonly Action _onChange;
    HwndSource? _source;
    DateTime _last;

    public DeviceChangeWatcher(Action onChange) => _onChange = onChange;

    public void Start(Window window)
    {
        var helper = new WindowInteropHelper(window);
        helper.EnsureHandle();
        _source = HwndSource.FromHwnd(helper.Handle);
        _source?.AddHook(WndProc);
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmDeviceChange) return IntPtr.Zero;
        var ev = wParam.ToInt32();
        if (ev is not (DbtDeviceArrival or DbtDeviceRemoveComplete or DbtDevNodesChanged))
            return IntPtr.Zero;
        if (DateTime.UtcNow - _last < TimeSpan.FromSeconds(3)) return IntPtr.Zero;
        _last = DateTime.UtcNow;
        try { _onChange(); } catch { }
        return IntPtr.Zero;
    }

    public void Dispose() => _source?.RemoveHook(WndProc);
}
