using System.Runtime.InteropServices;

namespace ExCord.Models;

public static class DisplayCoordinateHelper
{
    private const int LogPixelsX = 88;
    private const int LogPixelsY = 90;

    public static double GetDpiForScreen(Screen? screen)
    {
        if (screen is null)
        {
            return 96d;
        }

        var deviceContext = CreateDC(null, screen.DeviceName, null, IntPtr.Zero);
        if (deviceContext == IntPtr.Zero)
        {
            return 96d;
        }

        try
        {
            var dpi = GetDeviceCaps(deviceContext, LogPixelsX);
            if (dpi <= 0)
            {
                return 96d;
            }

            return dpi;
        }
        finally
        {
            DeleteDC(deviceContext);
        }
    }

    public static bool IsRectangleOnAnyScreen(double left, double top, double width, double height)
    {
        foreach (var screen in Screen.AllScreens)
        {
            var screenLeft = screen.Bounds.Left;
            var screenTop = screen.Bounds.Top;
            var screenRight = screen.Bounds.Right;
            var screenBottom = screen.Bounds.Bottom;

            var right = left + width;
            var bottom = top + height;

            if (right >= screenLeft && left <= screenRight && bottom >= screenTop && top <= screenBottom)
            {
                return true;
            }
        }

        return false;
    }

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateDC(string? lpszDriver, string? lpszDevice, string? lpszOutput, IntPtr lpInitData);

    [DllImport("gdi32.dll")]
    private static extern int GetDeviceCaps(IntPtr hdc, int index);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);
}
