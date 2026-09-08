using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace ExCord.Services;

public sealed class GlobalHotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 9000;
    private readonly HwndSource _source;
    private bool _registered;

    public event EventHandler? HotkeyPressed;

    public GlobalHotkeyService()
    {
        var parameters = new HwndSourceParameters("ExCordHotkeyHost")
        {
            WindowStyle = unchecked((int)(WindowStyles.WS_POPUP | WindowStyles.WS_VISIBLE)),
            Width = 0,
            Height = 0,
            PositionX = -10000,
            PositionY = -10000,
            ExtendedWindowStyle = (int)WindowStyles.WS_EX_TOOLWINDOW,
            UsesPerPixelOpacity = false
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    public bool RegisterHotkey(ModifierKeys modifiers, Key key)
    {
        if (_registered)
        {
            UnregisterHotkey();
        }

        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        var modifierValue = (uint)modifiers;

        var success = NativeMethods.RegisterHotKey(_source.Handle, HotkeyId, modifierValue, (uint)virtualKey);
        if (!success)
        {
            return false;
        }

        _registered = true;
        return true;
    }

    public void UnregisterHotkey()
    {
        if (!_registered)
        {
            return;
        }

        NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
        _registered = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterHotkey();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }

    private static class WindowStyles
    {
        public const int WS_POPUP = unchecked((int)0x80000000);
        public const int WS_VISIBLE = 0x10000000;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
    }
}
