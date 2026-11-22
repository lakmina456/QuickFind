using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace QuickFind.Services;

public sealed class HotkeyService : IDisposable
{
    const int HotkeyId = 0x4F4D; // 'OM'
    const uint ModAlt = 0x0001;
    const uint ModControl = 0x0002;
    const uint VkSpace = 0x20;

    [DllImport("user32.dll")]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    readonly Window _window;
    readonly Action _onHotkey;
    HwndSource? _source;
    bool _registered;

    public HotkeyService(Window window, Action onHotkey)
    {
        _window = window;
        _onHotkey = onHotkey;
    }

    public void Start()
    {
        var helper = new WindowInteropHelper(_window);
        helper.EnsureHandle();
        _source = HwndSource.FromHwnd(helper.Handle);
        _source?.AddHook(WndProc);
        Register();
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled) Register();
        else Unregister();
    }

    void Register()
    {
        if (_registered || _source is null) return;
        _registered = RegisterHotKey(_source.Handle, HotkeyId, ModControl | ModAlt, VkSpace);
    }

    void Unregister()
    {
        if (!_registered || _source is null) return;
        UnregisterHotKey(_source.Handle, HotkeyId);
        _registered = false;
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WmHotkey = 0x0312;
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            _onHotkey();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        _source?.RemoveHook(WndProc);
    }
}
