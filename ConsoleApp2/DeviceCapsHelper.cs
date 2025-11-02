using System;
using System.Runtime.InteropServices;

public static class DeviceCapsHelper
{
    [DllImport("gdi32.dll")]
    private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    // GetDeviceCaps constants
    private const int HORZSIZE = 4;      // Horizontal size in millimeters
    private const int VERTSIZE = 6;      // Vertical size in millimeters  
    private const int HORZRES = 8;       // Horizontal width in pixels
    private const int VERTRES = 10;      // Vertical height in pixels
    private const int LOGPIXELSX = 88;   // Logical pixels/inch in X
    private const int LOGPIXELSY = 90;   // Logical pixels/inch in Y
    private const int DESKTOPHORZRES = 118; // Horizontal desktop width in pixels
    private const int DESKTOPVERTRES = 117; // Vertical desktop height in pixels

    public static void PrintDeviceCapsInfo()
    {
        IntPtr hdc = GetDC(IntPtr.Zero);

        try
        {
            Console.WriteLine("=== GetDeviceCaps Monitor Information ===");

            int horzSize = GetDeviceCaps(hdc, HORZSIZE);
            int vertSize = GetDeviceCaps(hdc, VERTSIZE);
            int horzRes = GetDeviceCaps(hdc, HORZRES);
            int vertRes = GetDeviceCaps(hdc, VERTRES);
            int dpiX = GetDeviceCaps(hdc, LOGPIXELSX);
            int dpiY = GetDeviceCaps(hdc, LOGPIXELSY);

            Console.WriteLine($"Physical Size: {horzSize} x {vertSize} mm");
            Console.WriteLine($"Resolution: {horzRes} x {vertRes} pixels");
            Console.WriteLine($"DPI: {dpiX} x {dpiY}");

            // Расчет DPI на основе физических размеров
            if (horzSize > 0 && vertSize > 0)
            {
                double calculatedDPIX = (horzRes / (double)horzSize) * 25.4;
                double calculatedDPIY = (vertRes / (double)vertSize) * 25.4;
                Console.WriteLine($"Calculated DPI: {calculatedDPIX:F1} x {calculatedDPIY:F1}");
            }

            // Проверка масштабирования
            int desktopHorzRes = GetDeviceCaps(hdc, DESKTOPHORZRES);
            int desktopVertRes = GetDeviceCaps(hdc, DESKTOPVERTRES);

            if (desktopHorzRes != horzRes || desktopVertRes != vertRes)
            {
                double scaleX = (double)desktopHorzRes / horzRes;
                double scaleY = (double)desktopVertRes / vertRes;
                Console.WriteLine($"Scaling detected: {scaleX:F2}x {scaleY:F2}x");
            }
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, hdc);
        }
    }

    public static (int widthMM, int heightMM) GetPhysicalSize()
    {
        IntPtr hdc = GetDC(IntPtr.Zero);
        try
        {
            return (GetDeviceCaps(hdc, HORZSIZE), GetDeviceCaps(hdc, VERTSIZE));
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, hdc);
        }
    }
}