using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using UltimateX.Core.Common;

namespace UltimateX.Services.Common;

/// <summary>Eine WMI-Ergebniszeile als Dictionary (Objekte werden sofort freigegeben).</summary>
public sealed class WmiRow(Dictionary<string, object?> values)
{
    public object? this[string key] => values.TryGetValue(key, out var v) ? v : null;

    public string? Str(string key) => this[key]?.ToString()?.Trim() is { Length: > 0 } s ? s : null;

    public ulong? U64(string key) => this[key] switch
    {
        null => null,
        ulong u => u,
        uint u => u,
        ushort u => u,
        byte u => u,
        long l when l >= 0 => (ulong)l,
        int i when i >= 0 => (ulong)i,
        string s when ulong.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var p) => p,
        _ => null,
    };

    public bool? Bool(string key) => this[key] is bool b ? b : null;

    /// <summary>CIM-DATETIME ("yyyymmddHHMMSS.mmmmmmsUUU") oder DateTime → DateTime.</summary>
    public DateTime? Date(string key)
    {
        var v = this[key];
        if (v is DateTime dt) return dt;
        if (v is string s && s.Length >= 14)
        {
            try { return ManagementDateTimeConverter.ToDateTime(s); }
            catch (ArgumentOutOfRangeException) { return null; }
            catch (FormatException) { return null; }
        }
        return null;
    }
}

public sealed record WmiResult(IReadOnlyList<WmiRow> Rows, DataState State, string? Error)
{
    public bool Ok => State == DataState.Ok;
}

/// <summary>
/// Kapselt WMI-Abfragen mit Timeout. Fehler werden NICHT geworfen, sondern als Status geliefert,
/// damit fehlende Rechte/Provider nie zum Absturz führen (§90).
/// WQL-Strings sind ausschließlich Konstanten im Code – niemals aus Benutzereingaben zusammengesetzt.
/// </summary>
public static class Wmi
{
    public const string Cimv2 = @"root\cimv2";
    public const string Storage = @"root\Microsoft\Windows\Storage";
    public const string Defender = @"root\Microsoft\Windows\Defender";
    public const string SecurityCenter2 = @"root\SecurityCenter2";
    public const string BitLocker = @"root\cimv2\Security\MicrosoftVolumeEncryption";

    public static WmiResult Query(string wql, string scope = Cimv2, int timeoutSeconds = 20)
    {
        var rows = new List<WmiRow>();
        try
        {
            var options = new System.Management.EnumerationOptions
            {
                Timeout = TimeSpan.FromSeconds(timeoutSeconds),
                ReturnImmediately = true,
                Rewindable = false,
            };
            using var searcher = new ManagementObjectSearcher(new ManagementScope(scope), new ObjectQuery(wql), options);
            using var collection = searcher.Get();
            foreach (var obj in collection)
            {
                using (obj)
                {
                    var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    foreach (var p in obj.Properties) dict[p.Name] = p.Value;
                    rows.Add(new WmiRow(dict));
                }
            }
            return new WmiResult(rows, DataState.Ok, null);
        }
        catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.AccessDenied)
        {
            return new WmiResult(rows, DataState.RequiresAdmin, ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return new WmiResult(rows, DataState.RequiresAdmin, ex.Message);
        }
        catch (Exception ex) when (ex is ManagementException or COMException or TimeoutException)
        {
            return new WmiResult(rows, DataState.NotAvailable, ex.Message);
        }
    }
}
