using System.Runtime.InteropServices;

namespace UltimateX.Services.Diagnostics;

/// <summary>
/// Audio-Tests (v1.1 §Audio): Testton über die Standardausgabe (erst links, dann rechts) und eine Live-Pegelanzeige
/// des Standardmikrofons. Es wird NICHTS aufgezeichnet oder gespeichert – der Messpuffer wird fortlaufend überschrieben
/// und beim Beenden freigegeben. Nur über winmm.dll (Windows), keine Zusatzbibliothek.
/// </summary>
public static class AudioTest
{
    [DllImport("winmm.dll")] private static extern uint waveOutGetNumDevs();
    [DllImport("winmm.dll")] private static extern uint waveInGetNumDevs();
    [DllImport("winmm.dll", SetLastError = true)] private static extern bool PlaySound(byte[] pszSound, IntPtr hmod, uint fdwSound);

    private const uint SND_SYNC = 0x0000;
    private const uint SND_MEMORY = 0x0004;
    private const uint SND_NODEFAULT = 0x0002;

    public static int OutputDeviceCount => SafeCount(waveOutGetNumDevs);
    public static int InputDeviceCount => SafeCount(waveInGetNumDevs);

    private static int SafeCount(Func<uint> f)
    {
        try { return (int)Math.Min(f(), 64); }
        catch (DllNotFoundException) { return 0; }
        catch (EntryPointNotFoundException) { return 0; }
    }

    /// <summary>Spielt einen 440-Hz-Ton: 0,8 s links, 0,8 s rechts, 0,8 s beide (Lautstärke moderat). Blockiert ~2,4 s.</summary>
    public static bool PlayTestTone()
    {
        var wav = BuildStereoTone(440, 0.8, 0.25);
        try { return PlaySound(wav, IntPtr.Zero, SND_SYNC | SND_MEMORY | SND_NODEFAULT); }
        catch (DllNotFoundException) { return false; }
    }

    /// <summary>Erzeugt eine PCM-WAV-Datei (16 Bit, 44,1 kHz, Stereo) im Speicher.</summary>
    internal static byte[] BuildStereoTone(double hz, double segmentSeconds, double amplitude)
    {
        const int rate = 44100;
        var segment = (int)(rate * segmentSeconds);
        var frames = segment * 3;
        var dataBytes = frames * 4;
        using var ms = new MemoryStream(44 + dataBytes);
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8.ToArray());
        w.Write(36 + dataBytes);
        w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray());
        w.Write(16);
        w.Write((short)1);      // PCM
        w.Write((short)2);      // Stereo
        w.Write(rate);
        w.Write(rate * 4);
        w.Write((short)4);
        w.Write((short)16);
        w.Write("data"u8.ToArray());
        w.Write(dataBytes);
        var fade = rate / 50; // 20 ms Ein-/Ausblenden gegen Knacken
        for (var i = 0; i < frames; i++)
        {
            var seg = i / segment;
            var pos = i % segment;
            var env = Math.Min(1.0, Math.Min(pos, segment - pos) / (double)fade);
            var v = (short)(Math.Sin(2 * Math.PI * hz * i / rate) * amplitude * env * short.MaxValue);
            w.Write(seg is 0 or 2 ? v : (short)0); // links
            w.Write(seg is 1 or 2 ? v : (short)0); // rechts
        }
        w.Flush();
        return ms.ToArray();
    }
}

/// <summary>
/// Live-Pegel des Standardmikrofons (16 kHz, Mono). Puffer werden nur ausgewertet (Spitzenpegel) und sofort
/// wiederverwendet. Startet ausschließlich auf ausdrückliche Benutzeraktion.
/// </summary>
public sealed class MicLevelMeter : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct WAVEFORMATEX
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }

    [DllImport("winmm.dll")] private static extern int waveInOpen(out IntPtr hwi, uint uDeviceID, ref WAVEFORMATEX pwfx, IntPtr dwCallback, IntPtr dwInstance, uint fdwOpen);
    [DllImport("winmm.dll")] private static extern int waveInPrepareHeader(IntPtr hwi, IntPtr pwh, uint cbwh);
    [DllImport("winmm.dll")] private static extern int waveInUnprepareHeader(IntPtr hwi, IntPtr pwh, uint cbwh);
    [DllImport("winmm.dll")] private static extern int waveInAddBuffer(IntPtr hwi, IntPtr pwh, uint cbwh);
    [DllImport("winmm.dll")] private static extern int waveInStart(IntPtr hwi);
    [DllImport("winmm.dll")] private static extern int waveInReset(IntPtr hwi);
    [DllImport("winmm.dll")] private static extern int waveInClose(IntPtr hwi);

    private const uint WAVE_MAPPER = 0xFFFFFFFF;
    private const uint CALLBACK_NULL = 0;
    private const int HeaderSize = 48;          // WAVEHDR x64
    private const int OffData = 0, OffLength = 8, OffRecorded = 12, OffFlags = 24;
    private const uint WHDR_DONE = 1;
    private const int Buffers = 4;
    private const int BufferBytes = 3200;       // 100 ms bei 16 kHz/16 Bit

    private IntPtr _hwi;
    private readonly IntPtr[] _headers = new IntPtr[Buffers];
    private readonly IntPtr[] _data = new IntPtr[Buffers];
    private CancellationTokenSource? _cts;
    private Task? _loop;

    /// <summary>Spitzenpegel 0–1 des letzten Puffers.</summary>
    public double Level { get; private set; }

    /// <summary>Höchster Pegel seit Start (zeigt, ob überhaupt Signal ankam).</summary>
    public double PeakSinceStart { get; private set; }

    public bool IsRunning => _loop is not null;

    /// <summary>Öffnet das Standardmikrofon. Liefert eine Fehlermeldung statt einer Exception.</summary>
    public string? Start()
    {
        if (IsRunning) return null;
        var fmt = new WAVEFORMATEX { wFormatTag = 1, nChannels = 1, nSamplesPerSec = 16000, nAvgBytesPerSec = 32000, nBlockAlign = 2, wBitsPerSample = 16 };
        int rc;
        try { rc = waveInOpen(out _hwi, WAVE_MAPPER, ref fmt, IntPtr.Zero, IntPtr.Zero, CALLBACK_NULL); }
        catch (DllNotFoundException) { return "Audio-Schnittstelle nicht verfügbar."; }
        if (rc != 0)
        {
            _hwi = IntPtr.Zero;
            return rc == 2 /* MMSYSERR_BADDEVICEID */ ? "Kein Mikrofon gefunden."
                : rc == 4 /* MMSYSERR_ALLOCATED */ ? "Das Mikrofon wird gerade von einer anderen Anwendung exklusiv verwendet."
                : $"Mikrofon konnte nicht geöffnet werden (Fehler {rc}). Mikrofonzugriff für Desktop-Apps in den Windows-Datenschutzeinstellungen prüfen.";
        }
        for (var i = 0; i < Buffers; i++)
        {
            _data[i] = Marshal.AllocHGlobal(BufferBytes);
            _headers[i] = Marshal.AllocHGlobal(HeaderSize);
            ResetHeader(i);
            _ = waveInPrepareHeader(_hwi, _headers[i], HeaderSize);
            _ = waveInAddBuffer(_hwi, _headers[i], HeaderSize);
        }
        _ = waveInStart(_hwi);
        PeakSinceStart = 0;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loop = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                for (var i = 0; i < Buffers; i++)
                {
                    var flags = (uint)Marshal.ReadInt32(_headers[i], OffFlags);
                    if ((flags & WHDR_DONE) == 0) continue;
                    var recorded = Math.Min(Marshal.ReadInt32(_headers[i], OffRecorded), BufferBytes);
                    var peak = 0;
                    for (var o = 0; o + 1 < recorded; o += 2)
                    {
                        var s = Math.Abs((int)Marshal.ReadInt16(_data[i], o));
                        if (s > peak) peak = s;
                    }
                    Level = peak / 32768.0;
                    if (Level > PeakSinceStart) PeakSinceStart = Level;
                    _ = waveInUnprepareHeader(_hwi, _headers[i], HeaderSize);
                    ResetHeader(i);
                    _ = waveInPrepareHeader(_hwi, _headers[i], HeaderSize);
                    _ = waveInAddBuffer(_hwi, _headers[i], HeaderSize);
                }
                try { await Task.Delay(30, token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }, token);
        return null;
    }

    private void ResetHeader(int i)
    {
        for (var o = 0; o < HeaderSize; o += 4) Marshal.WriteInt32(_headers[i], o, 0);
        Marshal.WriteIntPtr(_headers[i], OffData, _data[i]);
        Marshal.WriteInt32(_headers[i], OffLength, BufferBytes);
        // Puffer leeren, damit nach dem Beenden keine Audiodaten im Speicher verbleiben.
        for (var o = 0; o < BufferBytes; o += 4) Marshal.WriteInt32(_data[i], o, 0);
    }

    public void Stop()
    {
        if (_loop is null) return;
        _cts?.Cancel();
        try { _loop.Wait(1000); }
        catch (AggregateException) { }
        if (_hwi != IntPtr.Zero)
        {
            _ = waveInReset(_hwi);
            for (var i = 0; i < Buffers; i++)
            {
                if (_headers[i] != IntPtr.Zero) _ = waveInUnprepareHeader(_hwi, _headers[i], HeaderSize);
            }
            _ = waveInClose(_hwi);
            _hwi = IntPtr.Zero;
        }
        for (var i = 0; i < Buffers; i++)
        {
            if (_data[i] != IntPtr.Zero)
            {
                for (var o = 0; o < BufferBytes; o += 4) Marshal.WriteInt32(_data[i], o, 0);
                Marshal.FreeHGlobal(_data[i]);
                _data[i] = IntPtr.Zero;
            }
            if (_headers[i] != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_headers[i]);
                _headers[i] = IntPtr.Zero;
            }
        }
        _cts?.Dispose();
        _cts = null;
        _loop = null;
        Level = 0;
    }

    public void Dispose() => Stop();
}
