using System.Runtime.InteropServices;

namespace UltimateX.Services.Hardware;

public sealed record MonitorInfo(
    string DeviceName,
    string FriendlyName,
    string Gpu,
    int Width,
    int Height,
    int RefreshHz,
    int ScalingPercent,
    int PositionX,
    int PositionY,
    bool IsPrimary,
    string Hdr);

/// <summary>
/// Multi-Monitor-Informationen (§21) über die Windows-Display-APIs:
/// EnumDisplayDevices/EnumDisplaySettings (Auflösung, Hz, Position, GPU), GetDpiForMonitor (Skalierung),
/// QueryDisplayConfig/DisplayConfigGetDeviceInfo (Monitorname, HDR-Status).
/// </summary>
public sealed class DisplayService
{
    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var configInfo = ReadDisplayConfig(); // GDI-Name → (Monitorname, HDR)
        var result = new List<MonitorInfo>();

        var adapter = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
        for (uint i = 0; EnumDisplayDevices(null, i, ref adapter, 0); i++)
        {
            try
            {
                if ((adapter.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) == 0) continue;
                var mode = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
                if (!EnumDisplaySettings(adapter.DeviceName, ENUM_CURRENT_SETTINGS, ref mode)) continue;

                var hmon = MonitorFromPoint(new POINT { X = mode.dmPositionX + 1, Y = mode.dmPositionY + 1 }, MONITOR_DEFAULTTONEAREST);
                int scaling = 100;
                try
                {
                    if (GetDpiForMonitor(hmon, 0 /* MDT_EFFECTIVE_DPI */, out var dpiX, out _) == 0 && dpiX > 0)
                        scaling = (int)Math.Round(dpiX * 100.0 / 96);
                }
                catch (DllNotFoundException) { }

                configInfo.TryGetValue(adapter.DeviceName, out var cfg);
                var friendly = cfg.Name;
                if (string.IsNullOrWhiteSpace(friendly))
                {
                    var mon = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
                    friendly = EnumDisplayDevices(adapter.DeviceName, 0, ref mon, 0) ? mon.DeviceString : "Monitor";
                }

                result.Add(new MonitorInfo(
                    adapter.DeviceName, friendly ?? "Monitor", adapter.DeviceString,
                    mode.dmPelsWidth, mode.dmPelsHeight, mode.dmDisplayFrequency, scaling,
                    mode.dmPositionX, mode.dmPositionY,
                    (adapter.StateFlags & DISPLAY_DEVICE_PRIMARY_DEVICE) != 0,
                    cfg.Hdr ?? "nicht ermittelbar"));
            }
            finally
            {
                adapter = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            }
        }
        return result.OrderBy(m => m.PositionX).ThenBy(m => m.PositionY).ToList();
    }

    // ---------------- QueryDisplayConfig (Offsets laut wingdi.h, x64) ----------------

    private const uint QDC_ONLY_ACTIVE_PATHS = 2;
    private const int PathInfoSize = 72;
    private const int ModeInfoSize = 64;
    private const int DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1;
    private const int DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2;
    private const int DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO = 9;

    private static Dictionary<string, (string? Name, string? Hdr)> ReadDisplayConfig()
    {
        var map = new Dictionary<string, (string? Name, string? Hdr)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out var pathCount, out var modeCount) != 0) return map;
            var paths = Marshal.AllocHGlobal((int)pathCount * PathInfoSize);
            var modes = Marshal.AllocHGlobal((int)Math.Max(1, modeCount) * ModeInfoSize);
            try
            {
                if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0) return map;
                for (int i = 0; i < pathCount; i++)
                {
                    var p = paths + i * PathInfoSize;
                    long srcAdapter = Marshal.ReadInt64(p, 0);
                    int srcId = Marshal.ReadInt32(p, 8);
                    long tgtAdapter = Marshal.ReadInt64(p, 20);
                    int tgtId = Marshal.ReadInt32(p, 28);

                    var gdiName = GetSourceName(srcAdapter, srcId);
                    if (gdiName is null) continue;
                    map[gdiName] = (GetTargetName(tgtAdapter, tgtId), GetHdr(tgtAdapter, tgtId));
                }
            }
            finally
            {
                Marshal.FreeHGlobal(paths);
                Marshal.FreeHGlobal(modes);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
        return map;
    }

    /// <summary>Allgemeiner Aufruf von DisplayConfigGetDeviceInfo mit Header {type,size,adapterId,id}.</summary>
    private static T? WithDeviceInfo<T>(int type, int size, long adapterId, int id, Func<IntPtr, T?> read)
    {
        var buf = Marshal.AllocHGlobal(size);
        try
        {
            for (int i = 0; i < size; i++) Marshal.WriteByte(buf, i, 0);
            Marshal.WriteInt32(buf, 0, type);
            Marshal.WriteInt32(buf, 4, size);
            Marshal.WriteInt64(buf, 8, adapterId);
            Marshal.WriteInt32(buf, 16, id);
            return DisplayConfigGetDeviceInfo(buf) == 0 ? read(buf) : default;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private static string? GetSourceName(long adapter, int id)
        => WithDeviceInfo(DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME, 20 + 64, adapter, id, b => Marshal.PtrToStringUni(b + 20)?.TrimEnd('\0'));

    private static string? GetTargetName(long adapter, int id)
        => WithDeviceInfo(DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME, 420, adapter, id, b =>
        {
            var name = Marshal.PtrToStringUni(b + 36)?.TrimEnd('\0');
            return string.IsNullOrWhiteSpace(name) ? null : name;
        });

    private static string? GetHdr(long adapter, int id)
        => WithDeviceInfo(DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO, 32, adapter, id, b =>
        {
            var flags = Marshal.ReadInt32(b, 20);
            bool supported = (flags & 0x1) != 0, enabled = (flags & 0x2) != 0;
            return enabled ? "HDR aktiv" : supported ? "HDR unterstützt, aus" : "Kein HDR";
        });

    // ---------------- P/Invoke ----------------

    private const int ENUM_CURRENT_SETTINGS = -1;
    private const int DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x1;
    private const int DISPLAY_DEVICE_PRIMARY_DEVICE = 0x4;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint numPathArrayElements, IntPtr pathArray, ref uint numModeInfoArrayElements, IntPtr modeInfoArray, IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(IntPtr requestPacket);
}
