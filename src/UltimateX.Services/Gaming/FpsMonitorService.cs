using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using UltimateX.Core.Analysis;
using UltimateX.Core.Logging;

namespace UltimateX.Services.Gaming;

public enum FpsMonitorState
{
    /// <summary>Nicht gestartet.</summary>
    Stopped,
    /// <summary>ETW-Sitzung läuft.</summary>
    Running,
    /// <summary>Weder Administrator noch Mitglied der Gruppe „Leistungsprotokollbenutzer“ → FPS = N/A.</summary>
    NoPermission,
    /// <summary>Sitzung konnte nicht gestartet werden (Details in <see cref="FpsMonitorService.LastError"/>).</summary>
    Failed,
}

/// <summary>Bildrate des Vordergrundprozesses. <see cref="Frames"/> ist leer, wenn der Prozess keine Frames ausgibt.</summary>
public sealed record FpsReading(FpsMonitorState State, int? ProcessId, string? ProcessName, FrameSnapshot Frames);

/// <summary>
/// FPS-Messung OHNE Injection (v1.3 In-Game-Overlay): Echtzeit-ETW-Sitzung „UltimateX-FPS“ auf die Present-Ereignisse
/// der Windows-Grafik-Laufzeit – Microsoft-Windows-DXGI (Present/PresentMultiplaneOverlay, Start) und Microsoft-Windows-D3D9
/// (Present, Start). Das ist dasselbe Verfahren wie bei Intel PresentMon; es wird kein Code in Spiele geladen und
/// keine Spieldatei angefasst (Anti-Cheat-neutral).
///
/// Frames werden dem Vordergrundprozess zugeordnet (GetForegroundWindow → GetWindowThreadProcessId); die Statistik
/// (FPS, Frametime, 1 % Low über 10 s) liefert <see cref="FrameStats"/> aus UltimateX.Core.
///
/// Rechte: Eine ETW-Sitzung benötigt Administratorrechte ODER Mitgliedschaft in „Leistungsprotokollbenutzer“
/// (S-1-5-32-559). Die App ändert Gruppenmitgliedschaften nie selbst. Ohne Rechte → <see cref="FpsMonitorState.NoPermission"/>.
///
/// Höchstens eine Sitzung (Name fest; eine verwaiste Sitzung gleichen Namens nach einem Absturz wird beim Start ersetzt).
/// Microsoft-Windows-DxgKrnl wird bewusst nicht aktiviert: Für reine FPS genügen die Laufzeit-Ereignisse; DxgKrnl würde
/// die Ereignismenge vervielfachen (Overhead) und nur für Anzeige-Latenzen gebraucht.
/// Grenzen: OpenGL- und manche Vulkan-Spiele ohne DXGI-Präsentation liefern keine Ereignisse → keine FPS (Zeile ausgeblendet).
/// </summary>
public sealed class FpsMonitorService : IDisposable
{
    public const string SessionName = "UltimateX-FPS";

    // Provider-GUIDs (Windows-Manifeste, vgl. PresentMon)
    internal static readonly Guid DxgiProvider = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    internal static readonly Guid D3D9Provider = new("783ACA0A-790E-4D7F-8451-AA850511C6B9");
    private const int DxgiPresentStart = 42;
    private const int DxgiPresentMpoStart = 55;
    private const int D3D9PresentStart = 1;

    private readonly object _gate = new();
    private readonly FrameStats _stats = new(windowSeconds: 10);
    private readonly int _ownPid = Environment.ProcessId;
    private readonly DateTime _origin = DateTime.Now;
    private TraceEventSession? _session;
    private Thread? _thread;
    private int _targetPid;
    private int _lastForegroundPid = -1;
    private string? _targetName;
    private bool _disposed;

    public FpsMonitorService()
    {
        // Sicherheitsnetz: ETW-Sitzungen überleben sonst das Prozessende, bis Windows sie aufräumt.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Stop();
    }

    public FpsMonitorState State { get; private set; } = FpsMonitorState.Stopped;

    public string? LastError { get; private set; }

    public bool IsRunning => State == FpsMonitorState.Running;

    /// <summary>Prüft (ohne etwas zu ändern), ob das aktuelle Konto ETW-Sitzungen starten darf.</summary>
    public static (bool Allowed, bool IsAdmin, bool IsPerformanceLogUser) CheckPermission()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            var p = new WindowsPrincipal(id);
            var admin = p.IsInRole(WindowsBuiltInRole.Administrator);
            var perfLog = p.IsInRole(new SecurityIdentifier("S-1-5-32-559"));
            return (admin || perfLog, admin, perfLog);
        }
        catch (Exception ex) when (ex is SystemException)
        {
            return (false, false, false);
        }
    }

    /// <summary>Startet die Sitzung (idempotent). Rückgabe: neuer Zustand.</summary>
    public FpsMonitorState Start()
    {
        lock (_gate)
        {
            if (_disposed) return State;
            if (_session is not null) return State;
            LastError = null;
            if (!CheckPermission().Allowed)
            {
                State = FpsMonitorState.NoPermission;
                return State;
            }

            TraceEventSession? session = null;
            try
            {
                session = new TraceEventSession(SessionName) { StopOnDispose = true, BufferSizeMB = 8 };
                session.EnableProvider(DxgiProvider, TraceEventLevel.Verbose);
                session.EnableProvider(D3D9Provider, TraceEventLevel.Verbose);
                session.Source.AllEvents += OnEvent;
                var source = session.Source;
                _thread = new Thread(() =>
                {
                    try { source.Process(); }
                    catch (Exception ex) when (ex is InvalidOperationException or COMException or ObjectDisposedException or UnauthorizedAccessException)
                    {
                        AppLog.Warn("FPS", "ETW-Verarbeitung beendet", ex.Message);
                    }
                })
                { IsBackground = true, Name = "UltimateX-FPS-ETW" };
                _session = session;
                _thread.Start();
                State = FpsMonitorState.Running;
                AppLog.Info("FPS", "FPS-Messung gestartet (ETW, DXGI/D3D9)");
            }
            catch (UnauthorizedAccessException ex)
            {
                session?.Dispose();
                _session = null;
                State = FpsMonitorState.NoPermission;
                LastError = ex.Message;
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                session?.Dispose();
                _session = null;
                State = FpsMonitorState.Failed;
                LastError = ex.Message;
                AppLog.Warn("FPS", "FPS-Messung konnte nicht gestartet werden", ex.Message);
            }
            return State;
        }
    }

    /// <summary>Beendet die Sitzung sauber (idempotent).</summary>
    public void Stop()
    {
        TraceEventSession? session;
        Thread? thread;
        lock (_gate)
        {
            session = _session;
            thread = _thread;
            _session = null;
            _thread = null;
            if (State == FpsMonitorState.Running) State = FpsMonitorState.Stopped;
        }
        if (session is null) return;
        try
        {
            session.Source.StopProcessing();
            session.Dispose(); // StopOnDispose → Windows beendet die Sitzung
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException or ObjectDisposedException)
        {
            AppLog.Warn("FPS", "ETW-Sitzung konnte nicht sauber beendet werden", ex.Message);
        }
        thread?.Join(TimeSpan.FromSeconds(2));
        _stats.Reset();
        AppLog.Info("FPS", "FPS-Messung beendet");
    }

    /// <summary>
    /// Aktueller Stand für den Vordergrundprozess (ca. 1× pro Sekunde aus der Oberfläche aufrufen).
    /// Wechselt der Vordergrundprozess, beginnt die Statistik neu.
    /// </summary>
    public FpsReading Read()
    {
        if (State != FpsMonitorState.Running) return new FpsReading(State, null, null, FrameSnapshot.Empty);

        var pid = ForegroundProcessId();
        if (pid != _lastForegroundPid)
        {
            _lastForegroundPid = pid;
            _stats.Reset();
            _targetName = pid > 0 ? ProcessName(pid) : null;
            Volatile.Write(ref _targetPid, pid == _ownPid ? 0 : pid);
        }
        if (pid <= 0 || pid == _ownPid) return new FpsReading(State, null, null, FrameSnapshot.Empty);
        return new FpsReading(State, pid, _targetName, _stats.Snapshot(Now()));
    }

    private double Now() => (DateTime.Now - _origin).TotalSeconds;

    private void OnEvent(TraceEvent data)
    {
        var id = (int)data.ID;
        var provider = data.ProviderGuid;
        var isPresent = (provider == DxgiProvider && (id == DxgiPresentStart || id == DxgiPresentMpoStart))
                        || (provider == D3D9Provider && id == D3D9PresentStart);
        if (!isPresent) return;
        var target = Volatile.Read(ref _targetPid);
        if (target == 0 || data.ProcessID != target) return;
        _stats.AddPresent((data.TimeStamp - _origin).TotalSeconds);
    }

    private static int ForegroundProcessId()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return 0;
        _ = GetWindowThreadProcessId(hwnd, out var pid);
        return (int)pid;
    }

    private static string? ProcessName(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    public void Dispose()
    {
        lock (_gate) _disposed = true;
        Stop();
    }
}
