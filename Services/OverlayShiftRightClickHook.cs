using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace ExCord.Services;

public sealed class OverlayShiftRightClickHook : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmRButtonDown = 0x0204;
    private const int WmRButtonUp = 0x0205;
    private const int VkShift = 0x10;

    private readonly Func<int, int, bool> _canHandle;
    private readonly Action<int, int> _handle;
    private readonly LowLevelMouseProc _proc;

    private IntPtr _hookHandle;
    private bool _suppressNextRightButtonUp;
    private bool _isDisposed;

    public OverlayShiftRightClickHook(Func<int, int, bool> canHandle, Action<int, int> handle)
    {
        _canHandle = canHandle;
        _handle = handle;
        _proc = HookProc;
    }

    public bool IsInstalled => _hookHandle != IntPtr.Zero;

    public void Install()
    {
        ThrowIfDisposed();

        if (IsInstalled)
        {
            return;
        }

        using var currentProcess = Process.GetCurrentProcess();
        using var currentModule = currentProcess.MainModule;
        var moduleHandle = currentModule is null ? IntPtr.Zero : GetModuleHandle(currentModule.ModuleName);

        _hookHandle = SetWindowsHookEx(WhMouseLl, _proc, moduleHandle, 0);
        if (_hookHandle == IntPtr.Zero)
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    public void Uninstall()
    {
        if (!IsInstalled)
        {
            _suppressNextRightButtonUp = false;
            return;
        }

        var hookHandle = _hookHandle;
        _hookHandle = IntPtr.Zero;
        _suppressNextRightButtonUp = false;

        if (!UnhookWindowsHookEx(hookHandle))
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        if (IsInstalled)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }

        _suppressNextRightButtonUp = false;
        GC.SuppressFinalize(this);
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
        {
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        var message = unchecked((int)(long)wParam);

        if (message == WmRButtonUp && _suppressNextRightButtonUp)
        {
            _suppressNextRightButtonUp = false;
            return new IntPtr(1);
        }

        if (message != WmRButtonDown || !IsShiftPressed())
        {
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        var data = Marshal.PtrToStructure<MsllHookStruct>(lParam);
        var x = data.pt.x;
        var y = data.pt.y;

        if (!_canHandle(x, y))
        {
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        _suppressNextRightButtonUp = true;

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            _handle(x, y);
        }
        else
        {
            dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => _handle(x, y)));
        }

        return new IntPtr(1);
    }

    private static bool IsShiftPressed()
    {
        return (GetAsyncKeyState(VkShift) & 0x8000) != 0;
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(OverlayShiftRightClickHook));
        }
    }

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MsllHookStruct
    {
        public Point pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
