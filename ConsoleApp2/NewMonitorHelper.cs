using System;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Runtime.InteropServices;

public static class NewMonitorHelper
{
    // MONITORINFOEX для получения имени устройства (szDevice)
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

    // Rect для прямоугольника экрана
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    // Delegate для обратного вызова при перечислении мониторов
    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    // Функция импорта для EnumDisplayMonitors
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    // GetMonitorInfoEx - чтобы получить szDevice
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    // EnumDisplayDevices для получения DeviceID (hardware id)
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

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    // Для получения DPI из Windows
    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("gdi32.dll")]
    public static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    public const int LOGPIXELSX = 88;
    public const int LOGPIXELSY = 90;

    // Возвращает сырые данные EDID для указанного монитора
    public static byte[] GetRawEDID(IntPtr hMonitor)
    {
        byte[]? edid = TryGetEdidFromHMonitor(hMonitor);
        if (edid != null && edid.Length > 0)
            return edid;

        return Array.Empty<byte>();
    }

    // Получаем EDID по HMONITOR через промежуточный шаг: GetMonitorInfoEx -> EnumDisplayDevices -> реестр
    private static byte[]? TryGetEdidFromHMonitor(IntPtr hMonitor)
    {
        MONITORINFOEX mi = new MONITORINFOEX();
        mi.cbSize = Marshal.SizeOf<MONITORINFOEX>();
        if (!GetMonitorInfo(hMonitor, ref mi))
            return null;
        string deviceName = mi.szDevice;

        for (uint i = 0; ; i++)
        {
            DISPLAY_DEVICE dd = new DISPLAY_DEVICE();
            dd.cb = Marshal.SizeOf<DISPLAY_DEVICE>();
            bool ok = EnumDisplayDevices(deviceName, i, ref dd, 0);
            if (!ok)
                break;

            if (!string.IsNullOrEmpty(dd.DeviceID) && dd.DeviceID.StartsWith("MONITOR", StringComparison.OrdinalIgnoreCase))
            {
                byte[]? edid = TryGetEdidFromRegistryByDeviceId(dd.DeviceID);
                if (edid != null && edid.Length > 0)
                    return edid;
            }
        }

        for (uint dev = 0; ; dev++)
        {
            DISPLAY_DEVICE ddAll = new DISPLAY_DEVICE();
            ddAll.cb = Marshal.SizeOf<DISPLAY_DEVICE>();
            if (!EnumDisplayDevices(null, dev, ref ddAll, 0))
                break;
            if (!string.IsNullOrEmpty(ddAll.DeviceID) && ddAll.DeviceID.StartsWith("MONITOR", StringComparison.OrdinalIgnoreCase))
            {
                byte[]? edid = TryGetEdidFromRegistryByDeviceId(ddAll.DeviceID);
                if (edid != null && edid.Length > 0)
                    return edid;
            }
        }

        return null;
    }

    // Поиск EDID в реестре по DeviceID типа "MONITOR\XXXX\..."
    private static byte[]? TryGetEdidFromRegistryByDeviceId(string deviceId)
    {
        string[] parts = deviceId.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return null;
        string hwId = parts[1];

        using RegistryKey? displayKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\DISPLAY");
        if (displayKey == null)
            return null;

        foreach (string monitorKeyName in displayKey.GetSubKeyNames())
        {
            if (!monitorKeyName.Equals(hwId, StringComparison.OrdinalIgnoreCase) && !monitorKeyName.StartsWith(hwId, StringComparison.OrdinalIgnoreCase))
                continue;

            using RegistryKey? monitorKey = displayKey.OpenSubKey(monitorKeyName);
            if (monitorKey == null)
                continue;

            foreach (string instance in monitorKey.GetSubKeyNames())
            {
                using RegistryKey? instanceKey = monitorKey.OpenSubKey(instance);
                if (instanceKey == null)
                    continue;

                using RegistryKey? devParams = instanceKey.OpenSubKey("Device Parameters");
                if (devParams == null)
                    continue;

                object? edidObj = devParams.GetValue("EDID");
                if (edidObj is byte[] edidBytes && edidBytes.Length >= 128)
                {
                    return edidBytes;
                }
            }
        }

        foreach (string monitorKeyName in displayKey.GetSubKeyNames())
        {
            using RegistryKey? monitorKey = displayKey.OpenSubKey(monitorKeyName);
            if (monitorKey == null)
                continue;

            foreach (string instance in monitorKey.GetSubKeyNames())
            {
                using RegistryKey? instanceKey = monitorKey.OpenSubKey(instance);
                if (instanceKey == null)
                    continue;

                object? hwObj = instanceKey.GetValue("HardwareID");
                if (hwObj is string[] hwArr)
                {
                    foreach (string h in hwArr)
                    {
                        if (deviceId.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0 || h.IndexOf(parts.Length > 1 ? parts[1] : deviceId, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            using RegistryKey? devParams = instanceKey.OpenSubKey("Device Parameters");
                            if (devParams == null)
                                continue;
                            object? edidObj = devParams.GetValue("EDID");
                            if (edidObj is byte[] edidBytes && edidBytes.Length >= 128)
                            {
                                return edidBytes;
                            }
                        }
                    }
                }
            }
        }

        return null;
    }

    // Главный метод для перечисления всех мониторов и вывода их EDID и DPI
    public static void ListMonitorsAndTheirEDIDs()
    {
        EnumDisplayMonitors(
            IntPtr.Zero,
            IntPtr.Zero,
            new MonitorEnumProc(delegate (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData)
            {
                ParseEDIDAndDPI(GetRawEDID(hMonitor), hMonitor);
                return true;
            }),
            IntPtr.Zero);
    }

    // Парсер EDID + расчет DPI
    public static void ParseEDIDAndDPI(byte[] rawEdid, IntPtr hMonitor)
    {
        Console.WriteLine("=== Monitor Information ===");

        // Выводим сырой EDID
        if (rawEdid.Length >= 128)
        {
            for (int i = 0; i < Math.Min(rawEdid.Length, 128); i++)
            {
                Console.Write($"{rawEdid[i]:X2} ");
                if ((i + 1) % 16 == 0)
                    Console.WriteLine();
            }
            Console.WriteLine();

            if (rawEdid[0] == 0x00 && rawEdid[1] == 0xFF && rawEdid[2] == 0xFF && rawEdid[3] == 0xFF)
            {
                // Размеры из EDID (в сантиметрах)
                var widthCM = (ushort)(rawEdid[0x15]);
                var heightCM = (ushort)(rawEdid[0x16]);

                var widthMM = widthCM * 10;
                var heightMM = heightCM * 10;

                Console.WriteLine($"Physical size from EDID: {widthMM}mm x {heightMM}mm");

                // Получаем информацию о мониторе для разрешения
                MONITORINFOEX mi = new MONITORINFOEX();
                mi.cbSize = Marshal.SizeOf<MONITORINFOEX>();
                if (GetMonitorInfo(hMonitor, ref mi))
                {
                    int widthPixels = mi.rcMonitor.right - mi.rcMonitor.left;
                    int heightPixels = mi.rcMonitor.bottom - mi.rcMonitor.top;

                    // Расчет истинного DPI
                    if (widthMM > 0 && heightMM > 0)
                    {
                        float widthInches = widthMM / 25.4f;
                        float heightInches = heightMM / 25.4f;

                        float trueDPI_X = widthPixels / widthInches;
                        float trueDPI_Y = heightPixels / heightInches;

                        // DPI из Windows (с учетом масштабирования)
                        IntPtr hdc = GetDC(IntPtr.Zero);
                        float windowsDPI_X = GetDeviceCaps(hdc, LOGPIXELSX);
                        float windowsDPI_Y = GetDeviceCaps(hdc, LOGPIXELSY);
                        ReleaseDC(IntPtr.Zero, hdc);

                        Console.WriteLine($"Resolution: {widthPixels} x {heightPixels}");
                        Console.WriteLine($"True DPI: {trueDPI_X:F1} x {trueDPI_Y:F1}");
                        Console.WriteLine($"Windows DPI: {windowsDPI_X:F1} x {windowsDPI_Y:F1}");
                        Console.WriteLine($"Scale factor: {windowsDPI_X / 96.0f:F2}x");
                    }
                }
            }
        }
        else
        {
            Console.WriteLine("No valid EDID data found");
        }
        Console.WriteLine();
    }

    // Альтернативный метод для получения DPI всех экранов через Screen
    public static void GetAllScreensDPI()
    {
        Console.WriteLine("=== All Screens DPI Information ===");

        foreach (Screen screen in Screen.AllScreens)
        {
            Console.WriteLine($"Screen: {screen.DeviceName}");
            Console.WriteLine($"Bounds: {screen.Bounds.Width} x {screen.Bounds.Height}");

            // DPI из Windows
            IntPtr hdc = GetDC(IntPtr.Zero);
            float dpiX = GetDeviceCaps(hdc, LOGPIXELSX);
            float dpiY = GetDeviceCaps(hdc, LOGPIXELSY);
            ReleaseDC(IntPtr.Zero, hdc);

            Console.WriteLine($"Windows DPI: {dpiX:F1} x {dpiY:F1}");
            Console.WriteLine($"Scale factor: {dpiX / 96.0f:F2}x");
            Console.WriteLine();
        }
    }
}