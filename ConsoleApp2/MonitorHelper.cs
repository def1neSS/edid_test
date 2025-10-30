using Microsoft.Win32;
using System.Runtime.InteropServices;

public static class MonitorHelper {
    // MONITORINFOEX для получения имени устройства (szDevice)
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct MONITORINFOEX {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    // Rect для прямоугольника экрана
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT {
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
    public struct DISPLAY_DEVICE {
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

    // Возвращает сырые данные EDID для указанного монитора
    public static byte[] GetRawEDID(IntPtr hMonitor) {
        // Попробуем получить EDID через реестр, сопоставив hMonitor -> szDevice -> Display DeviceID -> ключ в HKLM\...\Enum\DISPLAY
        byte[]? edid = TryGetEdidFromHMonitor(hMonitor);
        if (edid != null && edid.Length > 0)
            return edid;

        // Если не удалось — возвращаем пустой массив (не делаем P/Invoke в неизвестную DLL)
        return Array.Empty<byte>();
    }

    // Получаем EDID по HMONITOR через промежуточный шаг: GetMonitorInfoEx -> EnumDisplayDevices -> реестр
    private static byte[]? TryGetEdidFromHMonitor(IntPtr hMonitor) {
        MONITORINFOEX mi = new MONITORINFOEX();
        mi.cbSize = Marshal.SizeOf<MONITORINFOEX>();
        if (!GetMonitorInfo(hMonitor, ref mi))
            return null;
        string deviceName = mi.szDevice;
        Console.WriteLine($"Monitor Device Name: {deviceName}");

        for (uint i = 0; ; i++) {
            DISPLAY_DEVICE dd = new DISPLAY_DEVICE();
            dd.cb = Marshal.SizeOf<DISPLAY_DEVICE>();
            bool ok = EnumDisplayDevices(deviceName, i, ref dd, 0);
            if (!ok)
                break;

            // Ищем устройство типа MONITOR в DeviceID
            if (!string.IsNullOrEmpty(dd.DeviceID) && dd.DeviceID.StartsWith("MONITOR", StringComparison.OrdinalIgnoreCase)) {
                byte[]? edid = TryGetEdidFromRegistryByDeviceId(dd.DeviceID);
                if (edid != null && edid.Length > 0)
                    return edid;
            }
        }

        for (uint dev = 0; ; dev++) {
            DISPLAY_DEVICE ddAll = new DISPLAY_DEVICE();
            ddAll.cb = Marshal.SizeOf<DISPLAY_DEVICE>();
            if (!EnumDisplayDevices(null, dev, ref ddAll, 0))
                break;
            if (!string.IsNullOrEmpty(ddAll.DeviceID) && ddAll.DeviceID.StartsWith("MONITOR", StringComparison.OrdinalIgnoreCase)) {
                byte[]? edid = TryGetEdidFromRegistryByDeviceId(ddAll.DeviceID);
                if (edid != null && edid.Length > 0)
                    return edid;
            }
        }

        return null;
    }

    // Поиск EDID в реестре по DeviceID типа "MONITOR\XXXX\..."
    private static byte[]? TryGetEdidFromRegistryByDeviceId(string deviceId) {
        // deviceId формат: "MONITOR\<HardwareId>\<InstanceId>".
        // Нам интересен второй токен (HardwareId) обычно совпадает с ключом в HKLM\...\Enum\DISPLAY\<HardwareId>\...
        string[] parts = deviceId.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return null;
        string hwId = parts[1]; // e.g. "DELA0C1"
        Console.WriteLine($"Looking for EDID with Hardware ID: {hwId}");
        using RegistryKey? displayKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\DISPLAY");
        if (displayKey == null)
            return null;

        foreach (string monitorKeyName in displayKey.GetSubKeyNames()) {
            // compare monitorKeyName with hwId (some systems may have slightly different formatting)
            if (!monitorKeyName.Equals(hwId, StringComparison.OrdinalIgnoreCase) && !monitorKeyName.StartsWith(hwId, StringComparison.OrdinalIgnoreCase))
                continue;

            using RegistryKey? monitorKey = displayKey.OpenSubKey(monitorKeyName);
            if (monitorKey == null)
                continue;

            foreach (string instance in monitorKey.GetSubKeyNames()) {
                using RegistryKey? instanceKey = monitorKey.OpenSubKey(instance);
                if (instanceKey == null)
                    continue;

                // Попытка получить EDID из Device Parameters
                using RegistryKey? devParams = instanceKey.OpenSubKey("Device Parameters");
                if (devParams == null)
                    continue;

                object? edidObj = devParams.GetValue("EDID");
                if (edidObj is byte[] edidBytes && edidBytes.Length >= 128) {
                    return edidBytes;
                }
            }
        }

        // Если прямое совпадение по hwId не дало результатов: попытка перебрать все и сравнить HardwareID внутри
        foreach (string monitorKeyName in displayKey.GetSubKeyNames()) {
            using RegistryKey? monitorKey = displayKey.OpenSubKey(monitorKeyName);
            if (monitorKey == null)
                continue;

            foreach (string instance in monitorKey.GetSubKeyNames()) {
                using RegistryKey? instanceKey = monitorKey.OpenSubKey(instance);
                if (instanceKey == null)
                    continue;

                // HardwareID может быть MULTI_SZ
                object? hwObj = instanceKey.GetValue("HardwareID");
                if (hwObj is string[] hwArr) {
                    foreach (string h in hwArr) {
                        // сравниваем полный идентификатор без учёта регистра и без GUID-частей
                        if (deviceId.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0 || h.IndexOf(parts.Length > 1 ? parts[1] : deviceId, StringComparison.OrdinalIgnoreCase) >= 0) {
                            using RegistryKey? devParams = instanceKey.OpenSubKey("Device Parameters");
                            if (devParams == null)
                                continue;
                            object? edidObj = devParams.GetValue("EDID");
                            if (edidObj is byte[] edidBytes && edidBytes.Length >= 128) {
                                return edidBytes;
                            }
                        }
                    }
                }
            }
        }

        return null;
    }

    // Главный метод для перечисления всех мониторов и вывода их EDID
    public static void ListMonitorsAndTheirEDIDs() {
        EnumDisplayMonitors(
            IntPtr.Zero,
            IntPtr.Zero,
            new MonitorEnumProc(delegate (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData) { ParseEDID(GetRawEDID(hMonitor)); return true; }),
            IntPtr.Zero);
    }

    // Базовый парсер EDID
    public static void ParseEDID(byte[] rawEdid) {
        Console.WriteLine(rawEdid);
        for (int i = 0; i < rawEdid.Length; i++) {
            Console.Write($"{rawEdid[i]:X2} ");
            if ((i + 1) % 16 == 0)
                Console.WriteLine();
        }
        if (rawEdid.Length >= 128) {
            if (!(rawEdid[0] == 0x00 && rawEdid[1] == 0xFF && rawEdid[2] == 0xFF && rawEdid[3] == 0xFF))
                return;

            var widthMM = (ushort)(rawEdid[0x15]);
            var heightMM = (ushort)(rawEdid[0x16]);


            Console.WriteLine($"Width: {widthMM}sm, Height: {heightMM}sm");
        }
    }


}