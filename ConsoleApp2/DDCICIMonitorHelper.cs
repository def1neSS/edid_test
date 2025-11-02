using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.ComponentModel;

public static class DDCICIMonitorHelper
{
    // Структуры и константы для DDC/CI
    private const uint PHYSICAL_MONITOR_DESCRIPTION_SIZE = 128;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = (int)PHYSICAL_MONITOR_DESCRIPTION_SIZE)]
        public string szPhysicalMonitorDescription;
    }

    // WinAPI функции
    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, ref uint pdwNumberOfPhysicalMonitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint dwPhysicalMonitorArraySize, [Out] PHYSICAL_MONITOR[] pPhysicalMonitorArray);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool DestroyPhysicalMonitor(IntPtr hMonitor);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool DestroyPhysicalMonitors(uint dwPhysicalMonitorArraySize, [In] PHYSICAL_MONITOR[] pPhysicalMonitorArray);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetMonitorCapabilities(IntPtr hMonitor, ref uint pdwMonitorCapabilities, ref uint pdwSupportedColorTemperatures);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr hMonitor, byte bVCPCode, ref LPMC_VCP_CODE_TYPE pvct, ref uint pdwCurrentValue, ref uint pdwMaximumValue);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool CapabilitiesRequestAndCapabilitiesReply(IntPtr hMonitor, [Out] byte[] pszASCIICapabilitiesString, uint dwCapabilitiesStringLengthInCharacters);

    // VCP Codes для размеров
    private const byte VCP_CODE_HORIZONTAL_SIZE = 0xE0;   // Image size: width
    private const byte VCP_CODE_VERTICAL_SIZE = 0xE1;     // Image size: height

    [StructLayout(LayoutKind.Sequential)]
    public struct LPMC_VCP_CODE_TYPE
    {
        public byte bVCPCode;
        public byte bVCPCodeType;
    }

    // Монитор информация
    public class MonitorExactSize
    {
        public string Description { get; set; } = string.Empty;
        public int WidthMM { get; set; }
        public int HeightMM { get; set; }
        public bool IsExact { get; set; }
        public string Method { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
        public uint Capabilities { get; set; }
    }

    // Главный метод - получает точные размеры через DDC/CI
    public static List<MonitorExactSize> GetExactMonitorSizes()
    {
        var results = new List<MonitorExactSize>();

        // Перечисляем все мониторы
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (IntPtr hMonitor, IntPtr hdcMonitor, ref NativeMethods.RECT lprcMonitor, IntPtr dwData) =>
            {
                var size = GetExactSizeFromMonitor(hMonitor);
                results.Add(size);
                return true;
            }, IntPtr.Zero);

        return results;
    }

    private static MonitorExactSize GetExactSizeFromMonitor(IntPtr hMonitor)
    {
        var result = new MonitorExactSize();
        uint numberOfMonitors = 0;
        PHYSICAL_MONITOR[] physicalMonitors = null;

        try
        {
            // Получаем количество физических мониторов
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, ref numberOfMonitors))
            {
                int error = Marshal.GetLastWin32Error();
                result.Error = $"Failed to get number of physical monitors: {new Win32Exception(error).Message} (Error code: {error})";
                return result;
            }

            if (numberOfMonitors == 0)
            {
                result.Error = "No physical monitors found";
                return result;
            }

            // Получаем физические мониторы
            physicalMonitors = new PHYSICAL_MONITOR[numberOfMonitors];
            if (!GetPhysicalMonitorsFromHMONITOR(hMonitor, numberOfMonitors, physicalMonitors))
            {
                int error = Marshal.GetLastWin32Error();
                result.Error = $"Failed to get physical monitors: {new Win32Exception(error).Message} (Error code: {error})";
                return result;
            }

            // Для каждого физического монитора пытаемся получить размеры
            foreach (var physicalMonitor in physicalMonitors)
            {
                result.Description = physicalMonitor.szPhysicalMonitorDescription;

                // Получаем возможности монитора
                uint capabilities = 0, colorTemps = 0;
                bool hasCapabilities = GetMonitorCapabilities(physicalMonitor.hPhysicalMonitor, ref capabilities, ref colorTemps);
                result.Capabilities = capabilities;

                if (hasCapabilities)
                {
                    Console.WriteLine($"Monitor capabilities: 0x{capabilities:X8}");

                    // Проверяем поддержку VCP
                    if ((capabilities & 0x00000002) != 0) // MC_CAPS_BRIGHTNESS
                    {
                        Console.WriteLine("Monitor supports VCP controls");

                        // Пытаемся получить размеры через VCP коды
                        if (TryGetSizeViaVCP(physicalMonitor.hPhysicalMonitor, out int width, out int height))
                        {
                            result.WidthMM = width;
                            result.HeightMM = height;
                            result.IsExact = true;
                            result.Method = "VCP Codes (DDC/CI)";
                            break;
                        }
                        else
                        {
                            result.Method = "VCP supported but size codes not available";
                        }
                    }
                    else
                    {
                        result.Method = "Monitor does not support VCP controls";
                    }

                    // Пробуем получить Capabilities String
                    TryGetCapabilitiesString(physicalMonitor.hPhysicalMonitor, result);
                }
                else
                {
                    int error = Marshal.GetLastWin32Error();
                    result.Method = $"Cannot get monitor capabilities: {new Win32Exception(error).Message}";
                }
            }
        }
        catch (Exception ex)
        {
            result.Error = $"Exception: {ex.Message}";
        }
        finally
        {
            // Очищаем ресурсы
            if (physicalMonitors != null)
            {
                foreach (var monitor in physicalMonitors)
                {
                    if (monitor.hPhysicalMonitor != IntPtr.Zero)
                    {
                        DestroyPhysicalMonitor(monitor.hPhysicalMonitor);
                    }
                }
            }
        }

        return result;
    }

    // Получение размеров через VCP коды
    private static bool TryGetSizeViaVCP(IntPtr hPhysicalMonitor, out int width, out int height)
    {
        width = 0;
        height = 0;

        try
        {
            LPMC_VCP_CODE_TYPE vcpType = new LPMC_VCP_CODE_TYPE();
            uint currentValue = 0, maxValue = 0;

            // Пытаемся получить ширину через VCP код 0xE0
            bool gotWidth = GetVCPFeatureAndVCPFeatureReply(hPhysicalMonitor, VCP_CODE_HORIZONTAL_SIZE,
                ref vcpType, ref currentValue, ref maxValue);

            if (gotWidth && currentValue > 0)
            {
                width = (int)currentValue;
                Console.WriteLine($"VCP Width: {width}mm");
            }

            // Пытаемся получить высоту через VCP код 0xE1
            bool gotHeight = GetVCPFeatureAndVCPFeatureReply(hPhysicalMonitor, VCP_CODE_VERTICAL_SIZE,
                ref vcpType, ref currentValue, ref maxValue);

            if (gotHeight && currentValue > 0)
            {
                height = (int)currentValue;
                Console.WriteLine($"VCP Height: {height}mm");
            }

            return gotWidth && gotHeight && width > 0 && height > 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"VCP Failed: {ex.Message}");
            return false;
        }
    }

    // Получение capabilities string
    private static void TryGetCapabilitiesString(IntPtr hPhysicalMonitor, MonitorExactSize result)
    {
        try
        {
            byte[] capabilitiesString = new byte[256];
            if (CapabilitiesRequestAndCapabilitiesReply(hPhysicalMonitor, capabilitiesString, 256))
            {
                string caps = System.Text.Encoding.ASCII.GetString(capabilitiesString).TrimEnd('\0');
                if (!string.IsNullOrEmpty(caps))
                {
                    Console.WriteLine($"Capabilities string: {caps}");

                    // Парсим capabilities string для поиска размеров
                    if (TryParseCapabilitiesString(caps, out int width, out int height))
                    {
                        result.WidthMM = width;
                        result.HeightMM = height;
                        result.IsExact = true;
                        result.Method = "Capabilities String";
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Capabilities string failed: {ex.Message}");
        }
    }

    // Парсинг capabilities string
    private static bool TryParseCapabilitiesString(string caps, out int width, out int height)
    {
        width = 0;
        height = 0;

        try
        {
            // Ищем паттерны вроде "max H: 530 mm, max V: 300 mm"
            var lines = caps.Split('\n');
            foreach (var line in lines)
            {
                if (line.Contains("mm"))
                {
                    Console.WriteLine($"Parsing line: {line}");
                    // Здесь можно добавить логику парсинга специфичных форматов
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Capabilities parsing failed: {ex.Message}");
        }

        return false;
    }

    // Вывод результатов
    public static void PrintExactSizes()
    {
        Console.WriteLine("=== DDC/CI EXACT MONITOR SIZES ===");
        Console.WriteLine("⚠ Running as Administrator is required!");
        Console.WriteLine();

        var sizes = GetExactMonitorSizes();

        if (sizes.Count == 0)
        {
            Console.WriteLine("No monitors found");
            return;
        }

        foreach (var size in sizes)
        {
            Console.WriteLine($"Monitor: {size.Description}");
            Console.WriteLine($"Capabilities: 0x{size.Capabilities:X8}");

            if (size.WidthMM > 0 && size.HeightMM > 0)
            {
                Console.WriteLine($"Physical Size: {size.WidthMM} x {size.HeightMM} mm ✓ EXACT");
                Console.WriteLine($"Method: {size.Method}");

                // Рассчитываем диагональ
                double diagonalMM = Math.Sqrt(size.WidthMM * size.WidthMM + size.HeightMM * size.HeightMM);
                double diagonalInches = Math.Round(diagonalMM / 25.4, 1);
                Console.WriteLine($"Diagonal: {diagonalInches:F1} inches");
            }
            else
            {
                Console.WriteLine($"Physical Size: ❌ UNAVAILABLE");
                Console.WriteLine($"Method: {size.Method}");
                if (!string.IsNullOrEmpty(size.Error))
                {
                    Console.WriteLine($"Error: {size.Error}");
                }
            }
            Console.WriteLine("----------------------------------------");
        }

        Console.WriteLine("\nTroubleshooting:");
        Console.WriteLine("• Run as Administrator");
        Console.WriteLine("• Use DisplayPort or DVI-D cable (DDC/CI works best)");
        Console.WriteLine("• Check if monitor supports DDC/CI in OSD settings");
        Console.WriteLine("• Generic PnP Monitor usually doesn't support DDC/CI");
    }
}

// Вспомогательный класс с Native методами
public static class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);
}