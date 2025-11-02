using System;
using System.Collections.Generic;
using System.Management;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using Microsoft.Win32;

public static class NewMonitorHelper2
{
    // Структуры для Win32 API
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    // Win32 API импорты
    [DllImport("user32.dll")]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("gdi32.dll")]
    public static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    // Константы
    public const int LOGPIXELSX = 88;
    public const int LOGPIXELSY = 90;

    // Делегат
    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    // Класс для хранения информации о мониторе
    public class MonitorInfo
    {
        public string DeviceName { get; set; } = string.Empty;
        public string FriendlyName { get; set; } = string.Empty;
        public int WidthPixels { get; set; }
        public int HeightPixels { get; set; }
        public int WidthMM { get; set; }
        public int HeightMM { get; set; }
        public double DiagonalInches { get; set; }
        public double TrueDPI_X { get; set; }
        public double TrueDPI_Y { get; set; }
        public double WindowsDPI_X { get; set; }
        public double WindowsDPI_Y { get; set; }
        public double ScaleFactor { get; set; }
        public string SizeSource { get; set; } = "Unknown";
    }

    // Главный метод - получает точную информацию обо всех мониторах
    public static List<MonitorInfo> GetPreciseMonitorInfo()
    {
        var monitors = new List<MonitorInfo>();

        // Перечисляем мониторы через Win32 API
        EnumDisplayMonitors(
            IntPtr.Zero,
            IntPtr.Zero,
            new MonitorEnumProc((IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData) =>
            {
                var info = new MonitorInfo();

                // Получаем базовую информацию о мониторе
                MONITORINFOEX mi = new MONITORINFOEX();
                mi.cbSize = Marshal.SizeOf<MONITORINFOEX>();
                mi.szDevice = new string('\0', 32);

                if (GetMonitorInfo(hMonitor, ref mi))
                {
                    info.DeviceName = mi.szDevice.Trim('\0');
                    info.WidthPixels = mi.rcMonitor.right - mi.rcMonitor.left;
                    info.HeightPixels = mi.rcMonitor.bottom - mi.rcMonitor.top;

                    // Получаем Friendly Name
                    info.FriendlyName = GetFriendlyName(info.DeviceName);

                    // Пробуем разные методы получения физических размеров
                    var size = GetPhysicalSize(hMonitor, info.DeviceName);
                    info.WidthMM = size.Width;
                    info.HeightMM = size.Height;
                    info.SizeSource = size.Source;

                    // Рассчитываем дополнительные метрики
                    CalculateMetrics(info);

                    monitors.Add(info);
                }

                return true;
            }),
            IntPtr.Zero);

        return monitors;
    }

    // Получает Friendly Name монитора
    private static string GetFriendlyName(string deviceName)
    {
        try
        {
            DISPLAY_DEVICE dd = new DISPLAY_DEVICE();
            dd.cb = Marshal.SizeOf<DISPLAY_DEVICE>();

            if (EnumDisplayDevices(deviceName, 0, ref dd, 0))
            {
                return dd.DeviceString?.Trim() ?? "Unknown";
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting friendly name: {ex.Message}");
        }

        return "Unknown";
    }

    // Получает физические размеры всеми доступными методами
    private static (int Width, int Height, string Source) GetPhysicalSize(IntPtr hMonitor, string deviceName)
    {
        // 1. Пробуем WMI
        var wmiSize = GetSizeFromWMI(deviceName);
        if (wmiSize.Width > 0 && wmiSize.Height > 0)
        {
            return (wmiSize.Width, wmiSize.Height, "WMI");
        }

        // 2. Пробуем EDID из реестра
        var edidSize = GetSizeFromEDIDRegistry(hMonitor);
        if (edidSize.Width > 0 && edidSize.Height > 0)
        {
            return (edidSize.Width, edidSize.Height, "EDID");
        }

        // 3. Пробуем EDID напрямую
        var directEdidSize = GetSizeFromDirectEDID(hMonitor);
        if (directEdidSize.Width > 0 && directEdidSize.Height > 0)
        {
            return (directEdidSize.Width, directEdidSize.Height, "Direct EDID");
        }

        return (0, 0, "Not available");
    }

    // Получает размеры через WMI
    private static (int Width, int Height) GetSizeFromWMI(string deviceName)
    {
        try
        {
            using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DesktopMonitor"))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    var width = obj["ScreenWidth"] as uint?;
                    var height = obj["ScreenHeight"] as uint?;
                    var name = obj["Name"] as string;

                    if (width.HasValue && height.HasValue && width.Value > 0 && height.Value > 0)
                    {
                        Console.WriteLine($"WMI: {name} - {width}mm x {height}mm");
                        return ((int)width.Value, (int)height.Value);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WMI Error: {ex.Message}");
        }

        return (0, 0);
    }

    // Получает размеры через EDID из реестра
    private static (int Width, int Height) GetSizeFromEDIDRegistry(IntPtr hMonitor)
    {
        try
        {
            MONITORINFOEX mi = new MONITORINFOEX();
            mi.cbSize = Marshal.SizeOf<MONITORINFOEX>();
            mi.szDevice = new string('\0', 32);

            if (!GetMonitorInfo(hMonitor, ref mi))
                return (0, 0);

            string deviceName = mi.szDevice.Trim('\0');

            // Ищем EDID в реестре по deviceName
            for (uint i = 0; ; i++)
            {
                DISPLAY_DEVICE dd = new DISPLAY_DEVICE();
                dd.cb = Marshal.SizeOf<DISPLAY_DEVICE>();
                bool ok = EnumDisplayDevices(deviceName, i, ref dd, 0);
                if (!ok)
                    break;

                if (!string.IsNullOrEmpty(dd.DeviceID) && dd.DeviceID.StartsWith("MONITOR", StringComparison.OrdinalIgnoreCase))
                {
                    byte[] edid = GetEDIDFromRegistryByDeviceId(dd.DeviceID);
                    if (edid != null && edid.Length >= 128)
                    {
                        if (edid[0] == 0x00 && edid[1] == 0xFF && edid[2] == 0xFF && edid[3] == 0xFF)
                        {
                            int widthCM = edid[0x15];
                            int heightCM = edid[0x16];
                            return (widthCM * 10, heightCM * 10);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"EDID Registry Error: {ex.Message}");
        }

        return (0, 0);
    }

    // Получает EDID из реестра по DeviceID
    private static byte[] GetEDIDFromRegistryByDeviceId(string deviceId)
    {
        try
        {
            string[] parts = deviceId.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                return null;

            string hwId = parts[1];
            using RegistryKey displayKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\DISPLAY");
            if (displayKey == null)
                return null;

            foreach (string monitorKeyName in displayKey.GetSubKeyNames())
            {
                if (!monitorKeyName.Equals(hwId, StringComparison.OrdinalIgnoreCase) &&
                    !monitorKeyName.StartsWith(hwId, StringComparison.OrdinalIgnoreCase))
                    continue;

                using RegistryKey monitorKey = displayKey.OpenSubKey(monitorKeyName);
                if (monitorKey == null)
                    continue;

                foreach (string instance in monitorKey.GetSubKeyNames())
                {
                    using RegistryKey instanceKey = monitorKey.OpenSubKey(instance);
                    if (instanceKey == null)
                        continue;

                    using RegistryKey devParams = instanceKey.OpenSubKey("Device Parameters");
                    if (devParams == null)
                        continue;

                    object edidObj = devParams.GetValue("EDID");
                    if (edidObj is byte[] edidBytes && edidBytes.Length >= 128)
                    {
                        return edidBytes;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Registry EDID Error: {ex.Message}");
        }

        return null;
    }

    // Альтернативный метод получения EDID
    private static (int Width, int Height) GetSizeFromDirectEDID(IntPtr hMonitor)
    {
        // Этот метод можно расширить для прямого чтения EDID через DDC/CI
        // Пока возвращаем (0, 0) - это заглушка для будущей реализации
        return (0, 0);
    }

    // Рассчитывает все метрики
    private static void CalculateMetrics(MonitorInfo info)
    {
        if (info.WidthMM > 0 && info.HeightMM > 0)
        {
            // Истинный DPI
            double widthInches = info.WidthMM / 25.4;
            double heightInches = info.HeightMM / 25.4;

            info.TrueDPI_X = Math.Round(info.WidthPixels / widthInches, 1);
            info.TrueDPI_Y = Math.Round(info.HeightPixels / heightInches, 1);

            // Диагональ в дюймах
            double diagonalMM = Math.Sqrt(info.WidthMM * info.WidthMM + info.HeightMM * info.HeightMM);
            info.DiagonalInches = Math.Round(diagonalMM / 25.4, 1);
        }

        // DPI из Windows
        IntPtr hdc = GetDC(IntPtr.Zero);
        info.WindowsDPI_X = GetDeviceCaps(hdc, LOGPIXELSX);
        info.WindowsDPI_Y = GetDeviceCaps(hdc, LOGPIXELSY);
        ReleaseDC(IntPtr.Zero, hdc);

        info.ScaleFactor = Math.Round(info.WindowsDPI_X / 96.0, 2);
    }

    // Выводит информацию о всех мониторах
    public static void PrintMonitorInfo()
    {
        var monitors = GetPreciseMonitorInfo();

        Console.WriteLine("=== PRECISE MONITOR INFORMATION ===");
        Console.WriteLine();

        foreach (var monitor in monitors)
        {
            Console.WriteLine($"Monitor: {monitor.DeviceName}");
            Console.WriteLine($"Friendly Name: {monitor.FriendlyName}");
            Console.WriteLine($"Resolution: {monitor.WidthPixels} x {monitor.HeightPixels} pixels");

            if (monitor.WidthMM > 0 && monitor.HeightMM > 0)
            {
                Console.WriteLine($"Physical Size: {monitor.WidthMM} x {monitor.HeightMM} mm (Source: {monitor.SizeSource})");
                Console.WriteLine($"Diagonal: {monitor.DiagonalInches:F1} inches");
                Console.WriteLine($"True DPI: {monitor.TrueDPI_X:F1} x {monitor.TrueDPI_Y:F1}");
                Console.WriteLine($"Windows DPI: {monitor.WindowsDPI_X:F1} x {monitor.WindowsDPI_Y:F1}");
                Console.WriteLine($"Scale Factor: {monitor.ScaleFactor:F2}x");
                Console.WriteLine($"DPI Difference: {(monitor.WindowsDPI_X - monitor.TrueDPI_X):F1}");
            }
            else
            {
                Console.WriteLine($"Physical Size: Not available (Source: {monitor.SizeSource})");
            }
            Console.WriteLine("----------------------------------------");
        }
    }
}