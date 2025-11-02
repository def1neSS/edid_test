using System;
using System.Collections.Generic;
using System.Management;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Diagnostics;
using System.Drawing;

public static class NewMonitorHelper3
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
        public string SizeSource { get; set; } = "Not available";
        public bool IsExactSize { get; set; } = false;
    }

    // Главный метод - получает точную информацию обо всех мониторах
    public static List<MonitorInfo> GetPreciseMonitorInfo()
    {
        var monitors = new List<MonitorInfo>();

        // Сначала получаем все точные размеры из WMI
        var wmiSizes = GetAllWMISizes();

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

                    // Пробуем получить ТОЧНЫЕ размеры из WMI
                    var exactSize = GetExactSizeFromWMI(wmiSizes, info.FriendlyName, info.DeviceName);
                    if (exactSize.Width > 0 && exactSize.Height > 0)
                    {
                        info.WidthMM = exactSize.Width;
                        info.HeightMM = exactSize.Height;
                        info.SizeSource = "WMI (EXACT millimeters)";
                        info.IsExactSize = true;
                    }
                    else
                    {
                        // Если точные размеры недоступны - получаем приблизительные из EDID
                        var approxSize = GetApproximateSizeFromEDID(hMonitor);
                        info.WidthMM = approxSize.Width;
                        info.HeightMM = approxSize.Height;
                        info.SizeSource = "EDID (APPROXIMATE - rounded centimeters)";
                        info.IsExactSize = false;
                    }

                    // Рассчитываем дополнительные метрики
                    CalculateMetrics(info);

                    monitors.Add(info);
                }

                return true;
            }),
            IntPtr.Zero);

        return monitors;
    }


    // Ищет точные размеры WMI для конкретного монитора
    private static (int Width, int Height) GetExactSizeFromWMI(List<WMIMonitorSize> wmiSizes, string friendlyName, string deviceName)
    {
        if (wmiSizes.Count == 0) return (0, 0);

        // Пробуем найти по PNP Device ID
        foreach (var size in wmiSizes)
        {
            if (!string.IsNullOrEmpty(size.PNPDeviceID) &&
                deviceName.Contains(size.PNPDeviceID))
            {
                Console.WriteLine($"Matched by PNP ID: {size.PNPDeviceID}");
                return (size.Width, size.Height);
            }
        }

        // Пробуем найти по имени
        foreach (var size in wmiSizes)
        {
            if (!string.IsNullOrEmpty(size.Name) &&
                !string.IsNullOrEmpty(friendlyName) &&
                (friendlyName.Contains(size.Name) || size.Name.Contains(friendlyName)))
            {
                Console.WriteLine($"Matched by name: {size.Name}");
                return (size.Width, size.Height);
            }
        }

        // Если мониторов несколько - возвращаем первый (лучше чем ничего)
        if (wmiSizes.Count > 0)
        {
            Console.WriteLine($"Using first available WMI size: {wmiSizes[0].Name}");
            return (wmiSizes[0].Width, wmiSizes[0].Height);
        }

        return (0, 0);
    }

    // Получает приблизительные размеры через EDID (округленные)
    private static (int Width, int Height) GetApproximateSizeFromEDID(IntPtr hMonitor)
    {
        try
        {
            byte[] edid = GetEDIDFromRegistry(hMonitor);
            if (edid != null && edid.Length >= 128)
            {
                if (edid[0] == 0x00 && edid[1] == 0xFF && edid[2] == 0xFF && edid[3] == 0xFF)
                {
                    // ВНИМАНИЕ: EDID хранит размеры в ЦЕЛЫХ САНТИМЕТРАХ!
                    int widthCM = edid[0x15];
                    int heightCM = edid[0x16];

                    Console.WriteLine($"EDID APPROXIMATE: {widthCM}cm x {heightCM}cm");
                    return (widthCM * 10, heightCM * 10);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"EDID Error: {ex.Message}");
        }

        return (0, 0);
    }

    // Остальные методы без изменений
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

    private static byte[] GetEDIDFromRegistry(IntPtr hMonitor)
    {
        try
        {
            MONITORINFOEX mi = new MONITORINFOEX();
            mi.cbSize = Marshal.SizeOf<MONITORINFOEX>();
            mi.szDevice = new string('\0', 32);

            if (!GetMonitorInfo(hMonitor, ref mi))
                return null;

            string deviceName = mi.szDevice.Trim('\0');

            for (uint i = 0; ; i++)
            {
                DISPLAY_DEVICE dd = new DISPLAY_DEVICE();
                dd.cb = Marshal.SizeOf<DISPLAY_DEVICE>();
                bool ok = EnumDisplayDevices(deviceName, i, ref dd, 0);
                if (!ok)
                    break;

                if (!string.IsNullOrEmpty(dd.DeviceID) && dd.DeviceID.StartsWith("MONITOR", StringComparison.OrdinalIgnoreCase))
                {
                    return GetEDIDFromRegistryByDeviceId(dd.DeviceID);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"EDID Registry Error: {ex.Message}");
        }

        return null;
    }

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

    private static void CalculateMetrics(MonitorInfo info)
    {
        if (info.WidthMM > 0 && info.HeightMM > 0)
        {
            double widthInches = info.WidthMM / 25.4;
            double heightInches = info.HeightMM / 25.4;

            info.TrueDPI_X = Math.Round(info.WidthPixels / widthInches, 1);
            info.TrueDPI_Y = Math.Round(info.HeightPixels / heightInches, 1);

            double diagonalMM = Math.Sqrt(info.WidthMM * info.WidthMM + info.HeightMM * info.HeightMM);
            info.DiagonalInches = Math.Round(diagonalMM / 25.4, 1);
        }

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
                if (monitor.IsExactSize)
                {
                    Console.WriteLine($"Physical Size: {monitor.WidthMM} x {monitor.HeightMM} mm ✓ EXACT");
                    Console.WriteLine($"Diagonal: {monitor.DiagonalInches:F1} inches");
                    Console.WriteLine($"True DPI: {monitor.TrueDPI_X:F1} x {monitor.TrueDPI_Y:F1}");
                    Console.WriteLine($"Windows DPI: {monitor.WindowsDPI_X:F1} x {monitor.WindowsDPI_Y:F1}");
                    Console.WriteLine($"Scale Factor: {monitor.ScaleFactor:F2}x");
                    Console.WriteLine($"DPI Difference: {(monitor.WindowsDPI_X - monitor.TrueDPI_X):F1}");
                }
                else
                {
                    Console.WriteLine($"Physical Size: {monitor.WidthMM} x {monitor.HeightMM} mm ⚠ APPROXIMATE");
                    Console.WriteLine($"⚠ WARNING: EDID provides only rounded centimeter values");
                    Console.WriteLine($"⚠ True DPI calculation may be inaccurate");
                }
            }
            else
            {
                Console.WriteLine($"Physical Size: ❌ UNAVAILABLE");
                Console.WriteLine($"❌ Cannot calculate true DPI - physical size unknown");
            }
            Console.WriteLine($"Data Source: {monitor.SizeSource}");
            Console.WriteLine("----------------------------------------");
        }
    }


    // Получает все доступные размеры из WMI с детальной диагностикой
    private static List<WMIMonitorSize> GetAllWMISizes()
    {
        var sizes = new List<WMIMonitorSize>();

        try
        {
            Console.WriteLine("=== WMI DIAGNOSTICS ===");

            using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DesktopMonitor"))
            {
                var collection = searcher.Get();
                Console.WriteLine($"Found {collection.Count} monitor(s) in WMI");

                int monitorIndex = 0;
                foreach (ManagementObject obj in collection)
                {
                    monitorIndex++;
                    Console.WriteLine($"\n--- Monitor #{monitorIndex} ---");

                    // Выводим все свойства для диагностики
                    foreach (var property in obj.Properties)
                    {
                        if (property.Value != null)
                        {
                            Console.WriteLine($"  {property.Name}: {property.Value}");
                        }
                    }

                    var width = obj["ScreenWidth"] as uint?;
                    var height = obj["ScreenHeight"] as uint?;
                    var name = obj["Name"] as string;
                    var pnpId = obj["PNPDeviceID"] as string;
                    var status = obj["Status"] as string;

                    Console.WriteLine($"  ScreenWidth: {width}mm");
                    Console.WriteLine($"  ScreenHeight: {height}mm");
                    Console.WriteLine($"  Status: {status}");

                    if (width.HasValue && height.HasValue && width.Value > 0 && height.Value > 0)
                    {
                        sizes.Add(new WMIMonitorSize
                        {
                            Width = (int)width.Value,
                            Height = (int)height.Value,
                            Name = name ?? "Unknown",
                            PNPDeviceID = pnpId ?? "Unknown"
                        });

                        Console.WriteLine($"  ✅ ADDED: {width}mm x {height}mm");
                    }
                    else
                    {
                        Console.WriteLine($"  ❌ SKIPPED: Invalid or zero size");
                    }
                }
            }

            if (sizes.Count == 0)
            {
                Console.WriteLine("WMI: No valid monitor sizes found");

                // Пробуем альтернативный WMI запрос
                TryAlternativeWMIQuery();
            }
            else
            {
                Console.WriteLine($"WMI: Successfully got {sizes.Count} monitor size(s)");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WMI Error: {ex.Message}");
            Console.WriteLine($"WMI StackTrace: {ex.StackTrace}");
        }

        return sizes;
    }

    // Альтернативный WMI запрос
    private static void TryAlternativeWMIQuery()
    {
        try
        {
            Console.WriteLine("\n=== TRYING ALTERNATIVE WMI QUERIES ===");

            // Запрос 1: MonitorID
            using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE PNPClass = 'Monitor'"))
            {
                var collection = searcher.Get();
                Console.WriteLine($"Win32_PnPEntity (Monitor): {collection.Count} devices");

                foreach (ManagementObject obj in collection)
                {
                    var name = obj["Name"] as string;
                    var status = obj["Status"] as string;
                    Console.WriteLine($"  Monitor: {name}, Status: {status}");
                }
            }

            // Запрос 2: DisplayConfiguration
            using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DisplayConfiguration"))
            {
                var collection = searcher.Get();
                Console.WriteLine($"Win32_DisplayConfiguration: {collection.Count} configurations");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Alternative WMI Error: {ex.Message}");
        }
    }


    public static List<PointF> GetDesktopMonitors()
    {
        List<PointF> screenSizeList = new List<PointF>();

        //////////////////////////////////////////////////////////////////////////

        try
        {
            ManagementObjectSearcher searcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorID");

            foreach (ManagementObject queryObj in searcher.Get())
            {
                Console.WriteLine("-----------------------------------------------");
                Console.WriteLine("WmiMonitorID instance");
                Console.WriteLine("----------------");
                //   Console.WriteLine("Active: {0}", queryObj["Active"]);
                Console.WriteLine("InstanceName: {0}", queryObj["InstanceName"]);
                //   dynamic snid = queryObj["SerialNumberID"];
                //   Debug.WriteLine("SerialNumberID: (length) {0}", snid.Length);
                Console.WriteLine("YearOfManufacture: {0}", queryObj["YearOfManufacture"]);

                /*
                foreach (PropertyData data in queryObj.Properties)
                {
                    Debug.WriteLine(data.Value.ToString());
                }
                */

                dynamic code = queryObj["ProductCodeID"];
                string pcid = "";
                for (int i = 0; i < code.Length; i++)
                {
                    pcid = pcid + Char.ConvertFromUtf32(code[i]);
                    //pcid = pcid +code[i].ToString("X4");
                }
                Console.WriteLine("ProductCodeID: " + pcid);


                int xSize = 0;
                int ySize = 0;
                string PNP = queryObj["InstanceName"].ToString();
                PNP = PNP.Substring(0, PNP.Length - 2);  // remove _0
                if (PNP != null && PNP.Length > 0)
                {
                    string displayKey = "SYSTEM\\CurrentControlSet\\Enum\\";
                    string strSubDevice = displayKey + PNP + "\\" + "Device Parameters\\";
                    // e.g.
                    // SYSTEM\CurrentControlSet\Enum\DISPLAY\LEN40A0\4&1144c54c&0&UID67568640\Device Parameters
                    // SYSTEM\CurrentControlSet\Enum\DISPLAY\LGD0335\4&1144c54c&0&12345678&00&02\Device Parameters
                    //
                    Console.WriteLine("Register Path: " + strSubDevice);

                    RegistryKey regKey = Registry.LocalMachine.OpenSubKey(strSubDevice, false);
                    if (regKey != null)
                    {
                        if (regKey.GetValueKind("edid") == RegistryValueKind.Binary)
                        {
                            Console.WriteLine("read edid");

                            byte[] edid = (byte[])regKey.GetValue("edid");

                            const int edid_x_size_in_mm = 21;
                            const int edid_y_size_in_mm = 22;
                            xSize = ((int)edid[edid_x_size_in_mm] * 10);
                            ySize = ((int)edid[edid_y_size_in_mm] * 10);
                            Console.WriteLine("Screen size cx=" + xSize.ToString() + ", cy=" + ySize.ToString());
                        }
                        regKey.Close();
                    }
                }

                Console.WriteLine("-----------------------------------------------");

                PointF pt = new PointF();
                pt.X = (float)xSize;
                pt.Y = (float)ySize;

                screenSizeList.Add(pt);
            }
        }
        catch (ManagementException e)
        {
            Console.WriteLine("An error occurred while querying for WMI data: " + e.Message);
        }

        return screenSizeList;
    }
}

// Вспомогательный класс для WMI данных
public class WMIMonitorSize
{
    public int Width { get; set; }
    public int Height { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PNPDeviceID { get; set; } = string.Empty;
}

