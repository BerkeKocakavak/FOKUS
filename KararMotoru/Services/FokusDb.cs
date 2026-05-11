using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using FokusKararMotoru.Models;

namespace FokusKararMotoru.Services;

public sealed class FokusDb
{
    private readonly string _connectionString;
    private readonly object _syncRoot = new();

    public FokusDb(string projeKoku)
    {
        DbPath = Path.Combine(projeKoku, "fokus.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DbPath,
            Pooling = false
        }.ToString();
    }

    public string DbPath { get; }

    public void EnsureCreated()
    {
        lock (_syncRoot)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA journal_mode=WAL;

                CREATE TABLE IF NOT EXISTS sessions (
                    id TEXT PRIMARY KEY,
                    started_at TEXT NOT NULL,
                    ended_at TEXT NULL,
                    status TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS focus_samples (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    session_id TEXT NOT NULL,
                    time TEXT NOT NULL,
                    focus_score INTEGER NOT NULL,
                    raw_score REAL NOT NULL,
                    intervention_required INTEGER NOT NULL,
                    face_present INTEGER NOT NULL,
                    ear REAL NOT NULL,
                    ear_threshold REAL NOT NULL,
                    gaze REAL NOT NULL,
                    gaze_deviation REAL NOT NULL,
                    gaze_direction TEXT NULL,
                    posture_status TEXT NULL,
                    forward_deviation REAL NOT NULL,
                    side_deviation REAL NOT NULL,
                    blink_count INTEGER NOT NULL,
                    calibration_done INTEGER NOT NULL,
                    calibration_remaining INTEGER NOT NULL,
                    analysis_ready INTEGER NOT NULL,
                    analysis_status TEXT NULL,
                    pipe_connected INTEGER NOT NULL,
                    intervention_enabled INTEGER NOT NULL,
                    foreground_process TEXT NULL,
                    foreground_whitelisted INTEGER NOT NULL,
                    blacklist_processes TEXT NOT NULL,
                    blacklist_penalty INTEGER NOT NULL,
                    keys_per_min REAL NOT NULL,
                    mouse_pixels_per_min REAL NOT NULL,
                    idle_seconds REAL NOT NULL,
                    FOREIGN KEY(session_id) REFERENCES sessions(id)
                );

                CREATE TABLE IF NOT EXISTS penalties (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    sample_id INTEGER NOT NULL,
                    source TEXT NOT NULL,
                    value REAL NOT NULL,
                    description TEXT NOT NULL,
                    FOREIGN KEY(sample_id) REFERENCES focus_samples(id)
                );
                """;
            command.ExecuteNonQuery();
        }
    }

    public void StartSession(string sessionId, DateTimeOffset startedAt)
    {
        lock (_syncRoot)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT OR REPLACE INTO sessions (id, started_at, ended_at, status)
                VALUES ($id, $started_at, NULL, 'active');
                """;
            command.Parameters.AddWithValue("$id", sessionId);
            command.Parameters.AddWithValue("$started_at", FormatTime(startedAt));
            command.ExecuteNonQuery();
        }
    }

    public void EndSession(string sessionId, DateTimeOffset endedAt)
    {
        lock (_syncRoot)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE sessions
                SET ended_at = $ended_at,
                    status = 'ended'
                WHERE id = $id;
                """;
            command.Parameters.AddWithValue("$id", sessionId);
            command.Parameters.AddWithValue("$ended_at", FormatTime(endedAt));
            command.ExecuteNonQuery();
        }
    }

    public void SaveSample(string sessionId, KararMotoruState state)
    {
        if (state.Odak is null)
        {
            return;
        }

        lock (_syncRoot)
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            long sampleId;

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO focus_samples (
                        session_id, time, focus_score, raw_score, intervention_required,
                        face_present, ear, ear_threshold, gaze, gaze_deviation,
                        gaze_direction, posture_status, forward_deviation, side_deviation,
                        blink_count, calibration_done, calibration_remaining, analysis_ready,
                        analysis_status, pipe_connected, intervention_enabled,
                        foreground_process, foreground_whitelisted, blacklist_processes,
                        blacklist_penalty, keys_per_min, mouse_pixels_per_min, idle_seconds
                    )
                    VALUES (
                        $session_id, $time, $focus_score, $raw_score, $intervention_required,
                        $face_present, $ear, $ear_threshold, $gaze, $gaze_deviation,
                        $gaze_direction, $posture_status, $forward_deviation, $side_deviation,
                        $blink_count, $calibration_done, $calibration_remaining, $analysis_ready,
                        $analysis_status, $pipe_connected, $intervention_enabled,
                        $foreground_process, $foreground_whitelisted, $blacklist_processes,
                        $blacklist_penalty, $keys_per_min, $mouse_pixels_per_min, $idle_seconds
                    );
                    """;

                BiyometrikVeri? biyometrik = state.Biyometrik;
                GirdiAktiviteOzeti? girdi = state.Girdi;
                SurecTaramaSonucu? surec = state.Surec;

                command.Parameters.AddWithValue("$session_id", sessionId);
                command.Parameters.AddWithValue("$time", FormatTime(state.Zaman));
                command.Parameters.AddWithValue("$focus_score", state.Odak.Puan);
                command.Parameters.AddWithValue("$raw_score", state.Odak.HamHedefPuan);
                command.Parameters.AddWithValue("$intervention_required", Bool(state.Odak.MudahaleGerekli));
                command.Parameters.AddWithValue("$face_present", Bool(biyometrik?.YuzVar == true));
                command.Parameters.AddWithValue("$ear", biyometrik?.Ear ?? 0);
                command.Parameters.AddWithValue("$ear_threshold", biyometrik?.EarEsik ?? 0);
                command.Parameters.AddWithValue("$gaze", biyometrik?.Gaze ?? 0);
                command.Parameters.AddWithValue("$gaze_deviation", biyometrik?.GazeSapma ?? 0);
                command.Parameters.AddWithValue("$gaze_direction", NullIfEmpty(biyometrik?.GazeYon));
                command.Parameters.AddWithValue("$posture_status", NullIfEmpty(biyometrik?.BasDurum));
                command.Parameters.AddWithValue("$forward_deviation", biyometrik?.OneSapma ?? 0);
                command.Parameters.AddWithValue("$side_deviation", biyometrik?.YanaSapma ?? 0);
                command.Parameters.AddWithValue("$blink_count", biyometrik?.KirpmaSayisi ?? 0);
                command.Parameters.AddWithValue("$calibration_done", Bool(biyometrik?.KalibrasyonTamam == true));
                command.Parameters.AddWithValue("$calibration_remaining", biyometrik?.KalibrasyonKalanSaniye ?? 0);
                command.Parameters.AddWithValue("$analysis_ready", Bool(biyometrik?.AnalizHazir != false));
                command.Parameters.AddWithValue("$analysis_status", NullIfEmpty(biyometrik?.AnalizDurumu));
                command.Parameters.AddWithValue("$pipe_connected", Bool(state.PipeBagli));
                command.Parameters.AddWithValue("$intervention_enabled", Bool(state.MudahaleAktif));
                command.Parameters.AddWithValue("$foreground_process", NullIfEmpty(surec?.OnPlanSurec));
                command.Parameters.AddWithValue("$foreground_whitelisted", Bool(surec?.OnPlanBeyazListede == true));
                command.Parameters.AddWithValue("$blacklist_processes", surec is null ? string.Empty : string.Join("|", surec.KaraListedekiSurecler));
                command.Parameters.AddWithValue("$blacklist_penalty", surec?.KaraListeCezasi ?? 0);
                command.Parameters.AddWithValue("$keys_per_min", girdi?.TusDakika ?? 0);
                command.Parameters.AddWithValue("$mouse_pixels_per_min", girdi?.FarePikselDakika ?? 0);
                command.Parameters.AddWithValue("$idle_seconds", girdi?.HareketsizSaniye ?? 0);

                command.ExecuteNonQuery();
            }

            using (var idCommand = connection.CreateCommand())
            {
                idCommand.Transaction = transaction;
                idCommand.CommandText = "SELECT last_insert_rowid();";
                sampleId = (long)(idCommand.ExecuteScalar() ?? 0L);
            }

            foreach (CezaKalemi ceza in state.Odak.Cezalar)
            {
                using var penaltyCommand = connection.CreateCommand();
                penaltyCommand.Transaction = transaction;
                penaltyCommand.CommandText = """
                    INSERT INTO penalties (sample_id, source, value, description)
                    VALUES ($sample_id, $source, $value, $description);
                    """;
                penaltyCommand.Parameters.AddWithValue("$sample_id", sampleId);
                penaltyCommand.Parameters.AddWithValue("$source", ceza.Kaynak);
                penaltyCommand.Parameters.AddWithValue("$value", ceza.Deger);
                penaltyCommand.Parameters.AddWithValue("$description", ceza.Aciklama);
                penaltyCommand.ExecuteNonQuery();
            }

            transaction.Commit();
        }
    }

    public IReadOnlyList<SessionSummary> GetSessionSummaries(int limit, int focusThreshold)
    {
        lock (_syncRoot)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    s.id,
                    s.started_at,
                    COALESCE(s.ended_at, MAX(f.time), s.started_at) AS last_time,
                    COALESCE(AVG(f.focus_score), 0) AS avg_focus,
                    COALESCE(MIN(f.focus_score), 0) AS min_focus,
                    COALESCE(SUM(CASE WHEN f.focus_score < $threshold THEN 1 ELSE 0 END), 0) AS low_samples,
                    COALESCE(SUM(CASE WHEN f.blacklist_processes <> '' THEN 1 ELSE 0 END), 0) AS blacklist_samples,
                    COALESCE(COUNT(f.id), 0) AS sample_count
                FROM sessions s
                LEFT JOIN focus_samples f ON f.session_id = s.id
                GROUP BY s.id, s.started_at, s.ended_at
                ORDER BY last_time DESC
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$threshold", focusThreshold);
            command.Parameters.AddWithValue("$limit", limit);

            var summaries = new List<SessionSummary>();
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                DateTimeOffset startedAt = ParseTime(reader.GetString(1));
                DateTimeOffset endedAt = ParseTime(reader.GetString(2));
                summaries.Add(new SessionSummary(
                    reader.GetString(0),
                    startedAt,
                    endedAt,
                    reader.GetDouble(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5),
                    reader.GetInt32(6),
                    reader.GetInt32(7)));
            }

            return summaries;
        }
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys=ON;";
        command.ExecuteNonQuery();
        return connection;
    }

    private static string FormatTime(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTime(string value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed)
            ? parsed
            : DateTimeOffset.MinValue;

    private static int Bool(bool value) => value ? 1 : 0;

    private static object NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
}

public sealed record SessionSummary(
    string SessionId,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    double AverageFocus,
    int MinimumFocus,
    int LowFocusSamples,
    int BlacklistSamples,
    int SampleCount)
{
    public TimeSpan Duration => EndedAt > StartedAt ? EndedAt - StartedAt : TimeSpan.Zero;
}
