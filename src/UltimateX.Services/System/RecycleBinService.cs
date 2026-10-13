using System.Runtime.InteropServices;
using UltimateX.Core.Common;
using UltimateX.Core.Logging;
using UltimateX.Services.Common;
using UltimateX.Services.Native;

namespace UltimateX.Services.SystemTools;

public sealed record RecycleBinInfo(long ItemCount, long SizeBytes);

/// <summary>Papierkorb (§15): Inhalt abfragen, nach Bestätigung leeren. Nutzt die Shell-API (SHQueryRecycleBin/SHEmptyRecycleBin).</summary>
public sealed class RecycleBinService
{
    private const int S_OK = 0;
    private const int E_UNEXPECTED = unchecked((int)0x8000FFFF); // wird bei leerem Papierkorb geliefert

    public RecycleBinInfo? Query()
    {
        var info = new NativeMethods.SHQUERYRBINFO { cbSize = (uint)Marshal.SizeOf<NativeMethods.SHQUERYRBINFO>() };
        var hr = NativeMethods.SHQueryRecycleBin(null, ref info);
        return hr == S_OK ? new RecycleBinInfo(info.i64NumItems, info.i64Size) : null;
    }

    /// <summary>Darf nur nach expliziter Benutzerbestätigung in der UI aufgerufen werden.</summary>
    public OperationResult Empty(IntPtr ownerWindow)
    {
        var before = Query();
        var hr = NativeMethods.SHEmptyRecycleBin(ownerWindow, null,
            NativeMethods.SHERB_NOCONFIRMATION | NativeMethods.SHERB_NOSOUND);
        if (hr is S_OK or E_UNEXPECTED)
        {
            AppLog.Info("RecycleBin", "Papierkorb geleert",
                before is null ? null : $"{before.ItemCount} Elemente, {Format.Bytes(before.SizeBytes)}");
            return OperationResult.Ok("Papierkorb wurde geleert.");
        }
        return OperationResult.Fail($"Papierkorb konnte nicht geleert werden (0x{hr:X8}).");
    }
}
