using System.Globalization;
using Microsoft.Data.Sqlite;
using UltimateX.Core.Common;
using UltimateX.Core.Logging;

namespace UltimateX.Services.Data;

public sealed record HealthHistoryPoint(DateTime Time, int Score);

public sealed record BenchmarkRecord(DateTime Time, string Kind, double Score, string Details);

public sealed record KnownDevice(string Mac, string Ip, string? Name, string? Vendor, DateTime FirstSeen, DateTime LastSeen, string? Label);

public sealed record SpeedtestRecord(DateTime Time, double DownloadBps, double UploadBps, double PingMs, double JitterMs, double? PacketLossPercent, string Server);

/// <summary>
/// Lokale SQLite-Datenbank für Verlaufsdaten (§71, §103: Speedtest-Historie, PC-Health-Historie).
/// Regeln: ausschließlich parametrisierte Statements (keine SQL-Strings aus Eingaben),
/// Datei liegt im Benutzerprofil, eine beschädigte Datenbank wird beiseitegelegt und neu angelegt (§105).
/// </summary>
public sealed class HistoryDatabase
{
    // SQLite-Engine von Windows (winsqlite3.dll) verwenden – wird mit Windows Update gepatcht.
    static HistoryDatabase() => SQLitePCL.Batteries_V2.Init();

    private static readonly object Gate = new();
    private readonly string _path = Path.Combine(AppPaths.UserDataRoot, "history.db");
    private bool _initialized;

    private SqliteConnection Open()
    {
        Directory.CreateDirectory(AppPaths.UserDataRoot);
        var con = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString());
        con.Open();
        if (!_initialized)
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = """
                PRAGMA journal_mode=WAL;
                CREATE TABLE IF NOT EXISTS speedtest(
                    time TEXT NOT NULL, down REAL NOT NULL, up REAL NOT NULL, ping REAL NOT NULL,
                    jitter REAL NOT NULL, loss REAL NULL, server TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS health(time TEXT NOT NULL, score INTEGER NOT NULL);
                CREATE INDEX IF NOT EXISTS ix_speedtest_time ON speedtest(time);
                CREATE INDEX IF NOT EXISTS ix_health_time ON health(time);
                CREATE TABLE IF NOT EXISTS benchmark(time TEXT NOT NULL, kind TEXT NOT NULL, score REAL NOT NULL, details TEXT NOT NULL);
                CREATE INDEX IF NOT EXISTS ix_benchmark_time ON benchmark(time);
                CREATE TABLE IF NOT EXISTS netdevice(
                    mac TEXT PRIMARY KEY, ip TEXT NOT NULL, name TEXT NULL, vendor TEXT NULL,
                    first_seen TEXT NOT NULL, last_seen TEXT NOT NULL, label TEXT NULL);
                """;
            cmd.ExecuteNonQuery();
            _initialized = true;
        }
        return con;
    }

    private T Run<T>(Func<SqliteConnection, T> work, T fallback)
    {
        lock (Gate)
        {
            try
            {
                using var con = Open();
                return work(con);
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode is 11 /* CORRUPT */ or 26 /* NOTADB */)
            {
                AppLog.Warn("Database", "Verlaufsdatenbank beschädigt – wird neu angelegt", ex.Message);
                SqliteConnection.ClearAllPools();
                try { File.Move(_path, _path + $".broken-{DateTime.Now:yyyyMMddHHmmss}", true); } catch (IOException) { }
                _initialized = false;
                return fallback;
            }
            catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
            {
                AppLog.Warn("Database", "Datenbankzugriff fehlgeschlagen", ex.Message);
                return fallback;
            }
        }
    }

    private static string Ts(DateTime t) => t.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
    private static DateTime ParseTs(string s) => DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToLocalTime();

    public void AddSpeedtest(SpeedtestRecord r) => Run(con =>
    {
        using var cmd = con.CreateCommand();
        cmd.CommandText = "INSERT INTO speedtest(time, down, up, ping, jitter, loss, server) VALUES ($t, $d, $u, $p, $j, $l, $s)";
        cmd.Parameters.AddWithValue("$t", Ts(r.Time));
        cmd.Parameters.AddWithValue("$d", r.DownloadBps);
        cmd.Parameters.AddWithValue("$u", r.UploadBps);
        cmd.Parameters.AddWithValue("$p", r.PingMs);
        cmd.Parameters.AddWithValue("$j", r.JitterMs);
        cmd.Parameters.AddWithValue("$l", (object?)r.PacketLossPercent ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$s", r.Server);
        return cmd.ExecuteNonQuery();
    }, 0);

    public IReadOnlyList<SpeedtestRecord> GetSpeedtests(int days = 365) => Run<IReadOnlyList<SpeedtestRecord>>(con =>
    {
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT time, down, up, ping, jitter, loss, server FROM speedtest WHERE time >= $since ORDER BY time";
        cmd.Parameters.AddWithValue("$since", Ts(DateTime.Now.AddDays(-days)));
        using var rd = cmd.ExecuteReader();
        var list = new List<SpeedtestRecord>();
        while (rd.Read())
            list.Add(new SpeedtestRecord(ParseTs(rd.GetString(0)), rd.GetDouble(1), rd.GetDouble(2), rd.GetDouble(3), rd.GetDouble(4),
                rd.IsDBNull(5) ? null : rd.GetDouble(5), rd.GetString(6)));
        return list;
    }, []);

    /// <summary>Speichert höchstens einen Health-Score pro Stunde (Verlauf ohne Datenflut).</summary>
    public void AddHealthScore(int score) => Run(con =>
    {
        using var check = con.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM health WHERE time >= $since";
        check.Parameters.AddWithValue("$since", Ts(DateTime.Now.AddHours(-1)));
        if (Convert.ToInt64(check.ExecuteScalar(), CultureInfo.InvariantCulture) > 0) return 0;
        using var cmd = con.CreateCommand();
        cmd.CommandText = "INSERT INTO health(time, score) VALUES ($t, $s)";
        cmd.Parameters.AddWithValue("$t", Ts(DateTime.Now));
        cmd.Parameters.AddWithValue("$s", Math.Clamp(score, 0, 100));
        return cmd.ExecuteNonQuery();
    }, 0);

    public IReadOnlyList<HealthHistoryPoint> GetHealthHistory(int days = 30) => Run<IReadOnlyList<HealthHistoryPoint>>(con =>
    {
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT time, score FROM health WHERE time >= $since ORDER BY time";
        cmd.Parameters.AddWithValue("$since", Ts(DateTime.Now.AddDays(-days)));
        using var rd = cmd.ExecuteReader();
        var list = new List<HealthHistoryPoint>();
        while (rd.Read()) list.Add(new HealthHistoryPoint(ParseTs(rd.GetString(0)), rd.GetInt32(1)));
        return list;
    }, []);

    public void AddBenchmark(BenchmarkRecord r) => Run(con =>
    {
        using var cmd = con.CreateCommand();
        cmd.CommandText = "INSERT INTO benchmark(time, kind, score, details) VALUES ($t, $k, $s, $d)";
        cmd.Parameters.AddWithValue("$t", Ts(r.Time));
        cmd.Parameters.AddWithValue("$k", r.Kind);
        cmd.Parameters.AddWithValue("$s", r.Score);
        cmd.Parameters.AddWithValue("$d", r.Details);
        return cmd.ExecuteNonQuery();
    }, 0);

    public IReadOnlyList<BenchmarkRecord> GetBenchmarks(int max = 200) => Run<IReadOnlyList<BenchmarkRecord>>(con =>
    {
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT time, kind, score, details FROM benchmark ORDER BY time DESC LIMIT $m";
        cmd.Parameters.AddWithValue("$m", Math.Clamp(max, 1, 5000));
        using var rd = cmd.ExecuteReader();
        var list = new List<BenchmarkRecord>();
        while (rd.Read()) list.Add(new BenchmarkRecord(ParseTs(rd.GetString(0)), rd.GetString(1), rd.GetDouble(2), rd.GetString(3)));
        return list;
    }, []);

    /// <summary>Merkt sich Geräte im Heimnetz (für „neues Gerät erkannt“). Gibt true zurück, wenn das Gerät neu ist.</summary>
    public bool UpsertDevice(string mac, string ip, string? name, string? vendor) => Run(con =>
    {
        using var check = con.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM netdevice WHERE mac = $m";
        check.Parameters.AddWithValue("$m", mac);
        var exists = Convert.ToInt64(check.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
        using var cmd = con.CreateCommand();
        cmd.CommandText = exists
            ? "UPDATE netdevice SET ip = $ip, name = COALESCE($n, name), vendor = COALESCE($v, vendor), last_seen = $t WHERE mac = $m"
            : "INSERT INTO netdevice(mac, ip, name, vendor, first_seen, last_seen) VALUES ($m, $ip, $n, $v, $t, $t)";
        cmd.Parameters.AddWithValue("$m", mac);
        cmd.Parameters.AddWithValue("$ip", ip);
        cmd.Parameters.AddWithValue("$n", (object?)name ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$v", (object?)vendor ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$t", Ts(DateTime.Now));
        cmd.ExecuteNonQuery();
        return !exists;
    }, false);

    public int KnownDeviceCount() => Run(con =>
    {
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM netdevice";
        return (int)Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }, 0);

    public IReadOnlyList<KnownDevice> GetKnownDevices() => Run<IReadOnlyList<KnownDevice>>(con =>
    {
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT mac, ip, name, vendor, first_seen, last_seen, label FROM netdevice ORDER BY last_seen DESC";
        using var rd = cmd.ExecuteReader();
        var list = new List<KnownDevice>();
        while (rd.Read())
            list.Add(new KnownDevice(rd.GetString(0), rd.GetString(1), rd.IsDBNull(2) ? null : rd.GetString(2), rd.IsDBNull(3) ? null : rd.GetString(3),
                ParseTs(rd.GetString(4)), ParseTs(rd.GetString(5)), rd.IsDBNull(6) ? null : rd.GetString(6)));
        return list;
    }, []);

    public void SetDeviceLabel(string mac, string? label) => Run(con =>
    {
        using var cmd = con.CreateCommand();
        cmd.CommandText = "UPDATE netdevice SET label = $l WHERE mac = $m";
        cmd.Parameters.AddWithValue("$m", mac);
        cmd.Parameters.AddWithValue("$l", string.IsNullOrWhiteSpace(label) ? DBNull.Value : label.Trim()[..Math.Min(label.Trim().Length, 60)]);
        return cmd.ExecuteNonQuery();
    }, 0);

    public void ForgetDevices() => Run(con =>
    {
        using var cmd = con.CreateCommand();
        cmd.CommandText = "DELETE FROM netdevice";
        return cmd.ExecuteNonQuery();
    }, 0);

    /// <summary>Alte Daten löschen (Speedtest > 1 Jahr, Health > 1 Jahr).</summary>
    public void Prune() => Run(con =>
    {
        using var cmd = con.CreateCommand();
        cmd.CommandText = "DELETE FROM speedtest WHERE time < $c; DELETE FROM health WHERE time < $c;";
        cmd.Parameters.AddWithValue("$c", Ts(DateTime.Now.AddDays(-366)));
        return cmd.ExecuteNonQuery();
    }, 0);
}
