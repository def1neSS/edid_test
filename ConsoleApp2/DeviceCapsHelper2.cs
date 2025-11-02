using System;
using System.Runtime.InteropServices;

public static class DeviceCapsHelper2
{
    [DllImport("gdi32.dll")]
    private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    private const int HORZSIZE = 4;
    private const int VERTSIZE = 6;
    private const int HORZRES = 8;
    private const int VERTRES = 10;
    private const int LOGPIXELSX = 88;
    private const int LOGPIXELSY = 90;

    public class AccurateDPIInfo
    {
        public int PhysicalWidthMM { get; set; }
        public int PhysicalHeightMM { get; set; }
        public int WidthPixels { get; set; }
        public int HeightPixels { get; set; }
        public double RealDPI_X { get; set; }
        public double RealDPI_Y { get; set; }
        public double WindowsDPI_X { get; set; }
        public double WindowsDPI_Y { get; set; }
        public double ScaleFactor { get; set; }
        public double DiagonalInches { get; set; }
    }

    public static AccurateDPIInfo GetAccurateDPI()
    {
        var info = new AccurateDPIInfo();
        IntPtr hdc = GetDC(IntPtr.Zero);

        try
        {
            // Получаем физические размеры и разрешение
            info.PhysicalWidthMM = GetDeviceCaps(hdc, HORZSIZE);
            info.PhysicalHeightMM = GetDeviceCaps(hdc, VERTSIZE);
            info.WidthPixels = GetDeviceCaps(hdc, HORZRES);
            info.HeightPixels = GetDeviceCaps(hdc, VERTRES);
            info.WindowsDPI_X = GetDeviceCaps(hdc, LOGPIXELSX);
            info.WindowsDPI_Y = GetDeviceCaps(hdc, LOGPIXELSY);

            // Рассчитываем РЕАЛЬНЫЙ DPI на основе физических размеров
            if (info.PhysicalWidthMM > 0 && info.PhysicalHeightMM > 0)
            {
                double widthInches = info.PhysicalWidthMM / 25.4;
                double heightInches = info.PhysicalHeightMM / 25.4;

                info.RealDPI_X = Math.Round(info.WidthPixels / widthInches, 1);
                info.RealDPI_Y = Math.Round(info.HeightPixels / heightInches, 1);

                // Диагональ в дюймах
                double diagonalMM = Math.Sqrt(info.PhysicalWidthMM * info.PhysicalWidthMM +
                                            info.PhysicalHeightMM * info.PhysicalHeightMM);
                info.DiagonalInches = Math.Round(diagonalMM / 25.4, 1);
            }

            // Коэффициент масштабирования Windows
            info.ScaleFactor = Math.Round(info.WindowsDPI_X / 96.0, 2);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, hdc);
        }

        return info;
    }

    public static void PrintAccurateDPIInfo()
    {
        var info = GetAccurateDPI();

        Console.WriteLine("=== ACCURATE DPI INFORMATION ===");
        Console.WriteLine($"Physical Size: {info.PhysicalWidthMM} × {info.PhysicalHeightMM} mm");
        Console.WriteLine($"Resolution: {info.WidthPixels} × {info.HeightPixels} pixels");
        Console.WriteLine($"Diagonal: {info.DiagonalInches:F1} inches");
        Console.WriteLine();

        Console.WriteLine("DPI COMPARISON:");
        Console.WriteLine($"Real DPI: {info.RealDPI_X:F1} × {info.RealDPI_Y:F1}");
        Console.WriteLine($"Windows DPI: {info.WindowsDPI_X:F1} × {info.WindowsDPI_Y:F1}");
        Console.WriteLine($"Scale Factor: {info.ScaleFactor:F2}x");
        Console.WriteLine();

        Console.WriteLine("ANALYSIS:");
        if (Math.Abs(info.RealDPI_X - info.WindowsDPI_X) > 1)
        {
            double difference = info.WindowsDPI_X - info.RealDPI_X;
            Console.WriteLine($"⚠ Windows is using virtual DPI ({difference:+#;-#} difference)");
            Console.WriteLine($"⚠ Real DPI is {info.RealDPI_X:F1}, but Windows reports {info.WindowsDPI_X:F1}");
            Console.WriteLine($"⚠ This affects: Font sizes, UI scaling, bitmap rendering");
        }
        else
        {
            Console.WriteLine($"✓ DPI values are consistent");
        }

        // Расчет размера пикселя
        double pixelSizeMM = 25.4 / info.RealDPI_X;
        Console.WriteLine($"Pixel Size: {pixelSizeMM:F3} mm");
    }

    // Метод для получения масштаба 1:1 (без виртуального DPI)
    public static double GetTrueScaleFactor()
    {
        var info = GetAccurateDPI();
        return info.RealDPI_X / 96.0; // Относительно стандартного 96 DPI
    }
}