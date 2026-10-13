using System.Runtime.InteropServices;
using System.Text;
using UltimateX.Core.Common;

namespace UltimateX.Services.Network;

public sealed record WlanInfo(
    string InterfaceName,
    bool Connected,
    string? Ssid,
    string? Bssid,
    int? SignalQuality,
    int? RssiDbm,
    string? Band,
    int? Channel,
    string? Standard,
    double? RxMbps,
    double? TxMbps,
    string? Security,
    string? Note);

/// <summary>
/// WLAN-Informationen (§26) über die Native Wi-Fi API (wlanapi.dll).
/// Strukturen werden über feste Offsets gelesen (dokumentierte Layouts aus wlanapi.h, x64).
/// Hinweis: Ab Windows 11 24H2 liefert Windows SSID/BSSID nur, wenn Apps der Standortzugriff erlaubt ist.
/// </summary>
public sealed class WlanService
{
    private const int ERROR_SUCCESS = 0;
    private const int ERROR_ACCESS_DENIED = 5;
    private const uint wlan_intf_opcode_current_connection = 7;
    private const uint wlan_intf_opcode_channel_number = 8;
    private const int WlanInterfaceInfoSize = 532;
    private const int WlanBssEntrySize = 360;

    [DllImport("wlanapi.dll")] private static extern int WlanOpenHandle(uint dwClientVersion, IntPtr pReserved, out uint pdwNegotiatedVersion, out IntPtr phClientHandle);
    [DllImport("wlanapi.dll")] private static extern int WlanCloseHandle(IntPtr hClientHandle, IntPtr pReserved);
    [DllImport("wlanapi.dll")] private static extern int WlanEnumInterfaces(IntPtr hClientHandle, IntPtr pReserved, out IntPtr ppInterfaceList);
    [DllImport("wlanapi.dll")] private static extern void WlanFreeMemory(IntPtr pMemory);
    [DllImport("wlanapi.dll")] private static extern int WlanQueryInterface(IntPtr hClientHandle, ref Guid pInterfaceGuid, uint opCode, IntPtr pReserved, out uint pdwDataSize, out IntPtr ppData, IntPtr pWlanOpcodeValueType);
    [DllImport("wlanapi.dll")] private static extern int WlanGetNetworkBssList(IntPtr hClientHandle, ref Guid pInterfaceGuid, IntPtr pDot11Ssid, uint dot11BssType, [MarshalAs(UnmanagedType.Bool)] bool bSecurityEnabled, IntPtr pReserved, out IntPtr ppWlanBssList);

    public (IReadOnlyList<WlanInfo> Interfaces, DataState State, string? Message) GetInfo()
    {
        var list = new List<WlanInfo>();
        IntPtr client;
        try
        {
            if (WlanOpenHandle(2, IntPtr.Zero, out _, out client) != ERROR_SUCCESS)
                return (list, DataState.NotAvailable, "WLAN-Dienst (WLAN AutoConfig) nicht verfügbar.");
        }
        catch (DllNotFoundException)
        {
            return (list, DataState.NotAvailable, "Kein WLAN-Subsystem vorhanden.");
        }

        try
        {
            if (WlanEnumInterfaces(client, IntPtr.Zero, out var ifList) != ERROR_SUCCESS) return (list, DataState.NotAvailable, "Keine WLAN-Adapter.");
            try
            {
                int count = Marshal.ReadInt32(ifList, 0);
                for (int i = 0; i < count; i++)
                {
                    var item = ifList + 8 + i * WlanInterfaceInfoSize;
                    var guid = Marshal.PtrToStructure<Guid>(item);
                    var desc = Marshal.PtrToStringUni(item + 16, 256).TrimEnd('\0');
                    var state = Marshal.ReadInt32(item, 16 + 512); // 1 = connected
                    list.Add(state == 1 ? ReadConnection(client, guid, desc) : new WlanInfo(desc, false, null, null, null, null, null, null, null, null, null, null, "Nicht verbunden"));
                }
            }
            finally { WlanFreeMemory(ifList); }
        }
        finally { WlanCloseHandle(client, IntPtr.Zero); }

        return (list, list.Count == 0 ? DataState.NotAvailable : DataState.Ok, list.Count == 0 ? "Kein WLAN-Adapter gefunden." : null);
    }

    private static WlanInfo ReadConnection(IntPtr client, Guid guid, string desc)
    {
        var rc = WlanQueryInterface(client, ref guid, wlan_intf_opcode_current_connection, IntPtr.Zero, out _, out var data, IntPtr.Zero);
        if (rc == ERROR_ACCESS_DENIED)
            return new WlanInfo(desc, true, null, null, null, null, null, null, null, null, null, null,
                "Verbunden – Details erfordern unter Windows 11 die Freigabe „Standortzugriff für Apps“ (Einstellungen → Datenschutz → Standort).");
        if (rc != ERROR_SUCCESS) return new WlanInfo(desc, true, null, null, null, null, null, null, null, null, null, null, $"Details nicht verfügbar (Code {rc}).");

        string? ssid, bssid;
        int quality, phy, auth, ssidLen;
        uint rx, tx;
        byte[] bssidBytes;
        try
        {
            ssidLen = Math.Clamp(Marshal.ReadInt32(data, 520), 0, 32);
            var ssidBytes = new byte[ssidLen];
            Marshal.Copy(data + 524, ssidBytes, 0, ssidLen);
            ssid = Encoding.UTF8.GetString(ssidBytes);
            bssidBytes = new byte[6];
            Marshal.Copy(data + 560, bssidBytes, 0, 6);
            bssid = string.Join(':', bssidBytes.Select(b => b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture)));
            phy = Marshal.ReadInt32(data, 568);
            quality = Marshal.ReadInt32(data, 576);
            rx = (uint)Marshal.ReadInt32(data, 580);
            tx = (uint)Marshal.ReadInt32(data, 584);
            auth = Marshal.ReadInt32(data, 596);
        }
        finally { WlanFreeMemory(data); }

        int? channel = null;
        if (WlanQueryInterface(client, ref guid, wlan_intf_opcode_channel_number, IntPtr.Zero, out _, out var chData, IntPtr.Zero) == ERROR_SUCCESS)
        {
            channel = Marshal.ReadInt32(chData);
            WlanFreeMemory(chData);
        }

        var (freqKHz, rssi) = FindBss(client, guid, bssidBytes);
        string? band = freqKHz switch
        {
            null => channel is >= 1 and <= 14 ? "2,4 GHz" : null,
            < 3_000_000 => "2,4 GHz",
            < 5_925_000 => "5 GHz",
            _ => "6 GHz",
        };

        var standard = phy switch
        {
            11 => "Wi-Fi 7 (802.11be)",
            10 => band == "6 GHz" ? "Wi-Fi 6E (802.11ax)" : "Wi-Fi 6 (802.11ax)",
            8 => "Wi-Fi 5 (802.11ac)",
            7 => "Wi-Fi 4 (802.11n)",
            6 => "802.11g",
            5 => "802.11b",
            4 => "802.11a",
            _ => "unbekannt",
        };
        var security = auth switch
        {
            1 => "Offen (unverschlüsselt)",
            2 => "WEP (unsicher)",
            3 or 4 => "WPA (veraltet)",
            6 => "WPA2-Enterprise",
            7 => "WPA2-Personal",
            8 or 11 => "WPA3-Enterprise",
            9 => "WPA3-Personal",
            10 => "OWE (Enhanced Open)",
            _ => $"Typ {auth}",
        };

        return new WlanInfo(desc, true, ssid, bssid, quality, rssi, band, channel, standard, rx / 1000.0, tx / 1000.0, security, null);
    }

    private static (int? FreqKHz, int? Rssi) FindBss(IntPtr client, Guid guid, byte[] bssid)
    {
        if (WlanGetNetworkBssList(client, ref guid, IntPtr.Zero, 1, false, IntPtr.Zero, out var list) != ERROR_SUCCESS) return (null, null);
        try
        {
            int count = Marshal.ReadInt32(list, 4);
            var mac = new byte[6];
            for (int i = 0; i < count; i++)
            {
                var entry = list + 8 + i * WlanBssEntrySize;
                Marshal.Copy(entry + 40, mac, 0, 6);
                if (!mac.AsSpan().SequenceEqual(bssid)) continue;
                return (Marshal.ReadInt32(entry, 92), Marshal.ReadInt32(entry, 56));
            }
        }
        finally { WlanFreeMemory(list); }
        return (null, null);
    }
}
