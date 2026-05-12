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
        DbPath = UygulamaKlasorleri.VeritabaniDosyasi(projeKoku);
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

    public DashboardSnapshot GetDashboardSnapshot(int sessionLimit, int focusThreshold)
    {
        lock (_syncRoot)
        {
            using var connection = OpenConnection();
            DashboardOverview overview = ReadDashboardOverview(connection, focusThreshold);
            IReadOnlyList<FocusTrendPoint> trend = ReadFocusTrend(connection, sessionLimit, 180);
            IReadOnlyList<PenaltySummary> penalties = ReadPenaltyBreakdown(connection, sessionLimit, 8);
            IReadOnlyList<BlacklistSummary> blacklist = ReadBlacklistBreakdown(connection, sessionLimit);
            IReadOnlyList<DailyFocusSummary> daily = ReadDailySummaries(connection, sessionLimit, focusThreshold, 7);

            return new DashboardSnapshot(overview, trend, penalties, blacklist, daily);
        }
    }

    public SessionEndAnalysis? GetLatestSessionAnalysis(int focusThreshold)
    {
        lock (_syncRoot)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    s.id,
                    s.started_at,
                    COALESCE(s.ended_at, MAX(f.time), s.started_at) AS ended_at,
                    COALESCE(COUNT(f.id), 0) AS sample_count,
                    COALESCE(AVG(f.focus_score), 0) AS avg_focus,
                    COALESCE(MIN(f.focus_score), 0) AS min_focus,
                    COALESCE(SUM(CASE WHEN f.focus_score < $threshold THEN 1 ELSE 0 END), 0) AS low_samples,
                    COALESCE(SUM(CASE WHEN f.blacklist_processes <> '' THEN 1 ELSE 0 END), 0) AS blacklist_samples,
                    COALESCE(SUM(CASE WHEN f.face_present = 0 THEN 1 ELSE 0 END), 0) AS face_missing_samples,
                    COALESCE(SUM(CASE WHEN f.intervention_required = 1 THEN 1 ELSE 0 END), 0) AS intervention_samples,
                    COALESCE(AVG(f.keys_per_min), 0) AS avg_keys,
                    COALESCE(AVG(f.mouse_pixels_per_min), 0) AS avg_mouse,
                    COALESCE(AVG(f.idle_seconds), 0) AS avg_idle
                FROM sessions s
                LEFT JOIN focus_samples f ON f.session_id = s.id
                GROUP BY s.id, s.started_at, s.ended_at
                ORDER BY COALESCE(s.ended_at, MAX(f.time), s.started_at) DESC
                LIMIT 1;
            """;
            command.Parameters.AddWithValue("$threshold", focusThreshold);

            string sessionId;
            DateTimeOffset startedAt;
            DateTimeOffset endedAt;
            int sampleCount;
            double averageFocus;
            int minimumFocus;
            int lowSamples;
            int blacklistSamples;
            int faceMissingSamples;
            int interventionSamples;
            double averageKeysPerMinute;
            double averageMousePixelsPerMinute;
            double averageIdleSeconds;

            using (SqliteDataReader reader = command.ExecuteReader())
            {
                if (!reader.Read())
                {
                    return null;
                }

                sessionId = reader.GetString(0);
                startedAt = ParseTime(reader.GetString(1));
                endedAt = ParseTime(reader.GetString(2));
                sampleCount = reader.GetInt32(3);
                averageFocus = reader.GetDouble(4);
                minimumFocus = reader.GetInt32(5);
                lowSamples = reader.GetInt32(6);
                blacklistSamples = reader.GetInt32(7);
                faceMissingSamples = reader.GetInt32(8);
                interventionSamples = reader.GetInt32(9);
                averageKeysPerMinute = reader.GetDouble(10);
                averageMousePixelsPerMinute = reader.GetDouble(11);
                averageIdleSeconds = reader.GetDouble(12);
            }

            IReadOnlyList<PenaltySummary> penalties = ReadPenaltyBreakdownForSession(connection, sessionId, 8);
            IReadOnlyList<BlacklistSummary> blacklist = ReadBlacklistBreakdownForSession(connection, sessionId);

            return new SessionEndAnalysis(
                sessionId,
                startedAt,
                endedAt,
                sampleCount,
                averageFocus,
                minimumFocus,
                lowSamples,
                sampleCount == 0 ? 0 : lowSamples / (double)sampleCount,
                blacklistSamples,
                faceMissingSamples,
                interventionSamples,
                averageKeysPerMinute,
                averageMousePixelsPerMinute,
                averageIdleSeconds,
                penalties,
                blacklist);
        }
    }

    private DashboardOverview ReadDashboardOverview(
        SqliteConnection connection,
        int focusThreshold)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                COUNT(f.id) AS sample_count,
                COALESCE(AVG(f.focus_score), 0) AS avg_focus,
                COALESCE(MIN(f.focus_score), 0) AS min_focus,
                COALESCE(MAX(f.focus_score), 0) AS max_focus,
                COALESCE(SUM(CASE WHEN f.focus_score < $threshold THEN 1 ELSE 0 END), 0) AS low_samples,
                COALESCE(SUM(CASE WHEN f.intervention_required = 1 THEN 1 ELSE 0 END), 0) AS intervention_samples,
                COALESCE(SUM(CASE WHEN f.blacklist_processes <> '' THEN 1 ELSE 0 END), 0) AS blacklist_samples,
                COALESCE(SUM(CASE WHEN f.face_present = 0 THEN 1 ELSE 0 END), 0) AS face_missing_samples,
                COALESCE(AVG(f.keys_per_min), 0) AS avg_keys,
                COALESCE(AVG(f.mouse_pixels_per_min), 0) AS avg_mouse,
                COALESCE(AVG(f.idle_seconds), 0) AS avg_idle,
                MAX(f.time) AS last_sample_time
            FROM focus_samples f;
            """;
        command.Parameters.AddWithValue("$threshold", focusThreshold);

        int sampleCount;
        double averageFocus;
        int minimumFocus;
        int maximumFocus;
        int lowSamples;
        int interventionSamples;
        int blacklistSamples;
        int faceMissingSamples;
        double averageKeysPerMinute;
        double averageMousePixelsPerMinute;
        double averageIdleSeconds;
        DateTimeOffset? lastSampleTime;

        using (SqliteDataReader reader = command.ExecuteReader())
        {
            if (!reader.Read())
            {
                return DashboardOverview.Empty;
            }

            sampleCount = reader.GetInt32(0);
            averageFocus = reader.GetDouble(1);
            minimumFocus = reader.GetInt32(2);
            maximumFocus = reader.GetInt32(3);
            lowSamples = reader.GetInt32(4);
            interventionSamples = reader.GetInt32(5);
            blacklistSamples = reader.GetInt32(6);
            faceMissingSamples = reader.GetInt32(7);
            averageKeysPerMinute = reader.GetDouble(8);
            averageMousePixelsPerMinute = reader.GetDouble(9);
            averageIdleSeconds = reader.GetDouble(10);
            lastSampleTime = reader.IsDBNull(11) ? null : ParseTime(reader.GetString(11));
        }

        (int sessionCount, TimeSpan totalDuration) = ReadSessionDurationOverview(connection);

        return new DashboardOverview(
            sessionCount,
            totalDuration,
            sampleCount,
            averageFocus,
            minimumFocus,
            maximumFocus,
            lowSamples,
            sampleCount == 0 ? 0 : lowSamples / (double)sampleCount,
            interventionSamples,
            blacklistSamples,
            faceMissingSamples,
            averageKeysPerMinute,
            averageMousePixelsPerMinute,
            averageIdleSeconds,
            lastSampleTime);
    }

    private static (int SessionCount, TimeSpan TotalDuration) ReadSessionDurationOverview(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT started_at, ended_at
            FROM sessions;
            """;

        int count = 0;
        double totalSeconds = 0;
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            count++;
            DateTimeOffset startedAt = ParseTime(reader.GetString(0));
            DateTimeOffset endedAt = reader.IsDBNull(1) ? DateTimeOffset.Now : ParseTime(reader.GetString(1));
            if (endedAt > startedAt)
            {
                totalSeconds += (endedAt - startedAt).TotalSeconds;
            }
        }

        return (count, TimeSpan.FromSeconds(totalSeconds));
    }

    private static IReadOnlyList<FocusTrendPoint> ReadFocusTrend(SqliteConnection connection, int sessionLimit, int sampleLimit)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            WITH recent_sessions AS (
                SELECT id
                FROM sessions
                ORDER BY COALESCE(ended_at, started_at) DESC
                LIMIT $session_limit
            )
            SELECT time, focus_score, raw_score
            FROM focus_samples
            WHERE session_id IN (SELECT id FROM recent_sessions)
            ORDER BY time DESC
            LIMIT $sample_limit;
            """;
        command.Parameters.AddWithValue("$session_limit", sessionLimit);
        command.Parameters.AddWithValue("$sample_limit", sampleLimit);

        var points = new List<FocusTrendPoint>();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            points.Add(new FocusTrendPoint(
                ParseTime(reader.GetString(0)),
                reader.GetInt32(1),
                reader.GetDouble(2)));
        }

        points.Reverse();
        return points;
    }

    private static IReadOnlyList<PenaltySummary> ReadPenaltyBreakdown(SqliteConnection connection, int sessionLimit, int limit)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            WITH recent_sessions AS (
                SELECT id
                FROM sessions
                ORDER BY COALESCE(ended_at, started_at) DESC
                LIMIT $session_limit
            )
            SELECT p.source, COUNT(*) AS hits, COALESCE(SUM(p.value), 0) AS total_penalty, COALESCE(AVG(p.value), 0) AS avg_penalty
            FROM penalties p
            INNER JOIN focus_samples f ON f.id = p.sample_id
            WHERE f.session_id IN (SELECT id FROM recent_sessions)
            GROUP BY p.source
            ORDER BY total_penalty DESC, hits DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$session_limit", sessionLimit);
        command.Parameters.AddWithValue("$limit", limit);

        var items = new List<PenaltySummary>();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            items.Add(new PenaltySummary(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetDouble(2),
                reader.GetDouble(3)));
        }

        return items;
    }

    private static IReadOnlyList<PenaltySummary> ReadPenaltyBreakdownForSession(SqliteConnection connection, string sessionId, int limit)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.source, COUNT(*) AS hits, COALESCE(SUM(p.value), 0) AS total_penalty, COALESCE(AVG(p.value), 0) AS avg_penalty
            FROM penalties p
            INNER JOIN focus_samples f ON f.id = p.sample_id
            WHERE f.session_id = $session_id
            GROUP BY p.source
            ORDER BY total_penalty DESC, hits DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$session_id", sessionId);
        command.Parameters.AddWithValue("$limit", limit);

        var items = new List<PenaltySummary>();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            items.Add(new PenaltySummary(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetDouble(2),
                reader.GetDouble(3)));
        }

        return items;
    }

    private static IReadOnlyList<BlacklistSummary> ReadBlacklistBreakdown(SqliteConnection connection, int sessionLimit)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            WITH recent_sessions AS (
                SELECT id
                FROM sessions
                ORDER BY COALESCE(ended_at, started_at) DESC
                LIMIT $session_limit
            )
            SELECT blacklist_processes
            FROM focus_samples
            WHERE session_id IN (SELECT id FROM recent_sessions)
              AND blacklist_processes <> '';
            """;
        command.Parameters.AddWithValue("$session_limit", sessionLimit);

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            foreach (string process in reader.GetString(0).Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                counts[process] = counts.TryGetValue(process, out int count) ? count + 1 : 1;
            }
        }

        return counts
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .Select(pair => new BlacklistSummary(pair.Key, pair.Value))
            .ToArray();
    }

    private static IReadOnlyList<BlacklistSummary> ReadBlacklistBreakdownForSession(SqliteConnection connection, string sessionId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT blacklist_processes
            FROM focus_samples
            WHERE session_id = $session_id
              AND blacklist_processes <> '';
            """;
        command.Parameters.AddWithValue("$session_id", sessionId);

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            foreach (string process in reader.GetString(0).Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                counts[process] = counts.TryGetValue(process, out int count) ? count + 1 : 1;
            }
        }

        return counts
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .Select(pair => new BlacklistSummary(pair.Key, pair.Value))
            .ToArray();
    }

    private static IReadOnlyList<DailyFocusSummary> ReadDailySummaries(SqliteConnection connection, int sessionLimit, int focusThreshold, int dayLimit)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            WITH recent_sessions AS (
                SELECT id
                FROM sessions
                ORDER BY COALESCE(ended_at, started_at) DESC
                LIMIT $session_limit
            )
            SELECT
                substr(time, 1, 10) AS day,
                COALESCE(AVG(focus_score), 0) AS avg_focus,
                COALESCE(MIN(focus_score), 0) AS min_focus,
                COUNT(id) AS sample_count,
                COALESCE(SUM(CASE WHEN focus_score < $threshold THEN 1 ELSE 0 END), 0) AS low_samples,
                COALESCE(SUM(CASE WHEN blacklist_processes <> '' THEN 1 ELSE 0 END), 0) AS blacklist_samples,
                COALESCE(SUM(CASE WHEN intervention_required = 1 THEN 1 ELSE 0 END), 0) AS intervention_samples
            FROM focus_samples
            WHERE session_id IN (SELECT id FROM recent_sessions)
            GROUP BY day
            ORDER BY day DESC
            LIMIT $day_limit;
            """;
        command.Parameters.AddWithValue("$session_limit", sessionLimit);
        command.Parameters.AddWithValue("$threshold", focusThreshold);
        command.Parameters.AddWithValue("$day_limit", dayLimit);

        var items = new List<DailyFocusSummary>();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            DateOnly day = DateOnly.TryParse(reader.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly parsed)
                ? parsed
                : DateOnly.MinValue;
            int sampleCount = reader.GetInt32(3);
            int lowSamples = reader.GetInt32(4);
            items.Add(new DailyFocusSummary(
                day,
                reader.GetDouble(1),
                reader.GetInt32(2),
                sampleCount,
                lowSamples,
                sampleCount == 0 ? 0 : lowSamples / (double)sampleCount,
                reader.GetInt32(5),
                reader.GetInt32(6)));
        }

        items.Reverse();
        return items;
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

public sealed record DashboardSnapshot(
    DashboardOverview Overview,
    IReadOnlyList<FocusTrendPoint> FocusTrend,
    IReadOnlyList<PenaltySummary> Penalties,
    IReadOnlyList<BlacklistSummary> Blacklist,
    IReadOnlyList<DailyFocusSummary> DailySummaries);

public sealed record DashboardOverview(
    int SessionCount,
    TimeSpan TotalDuration,
    int SampleCount,
    double AverageFocus,
    int MinimumFocus,
    int MaximumFocus,
    int LowFocusSamples,
    double LowFocusRate,
    int InterventionSamples,
    int BlacklistSamples,
    int FaceMissingSamples,
    double AverageKeysPerMinute,
    double AverageMousePixelsPerMinute,
    double AverageIdleSeconds,
    DateTimeOffset? LastSampleTime)
{
    public static DashboardOverview Empty { get; } = new(
        0,
        TimeSpan.Zero,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        null);
}

public sealed record FocusTrendPoint(DateTimeOffset Time, int FocusScore, double RawScore);

public sealed record PenaltySummary(string Source, int Hits, double TotalPenalty, double AveragePenalty);

public sealed record BlacklistSummary(string ProcessName, int Hits);

public sealed record SessionEndAnalysis(
    string SessionId,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int SampleCount,
    double AverageFocus,
    int MinimumFocus,
    int LowFocusSamples,
    double LowFocusRate,
    int BlacklistSamples,
    int FaceMissingSamples,
    int InterventionSamples,
    double AverageKeysPerMinute,
    double AverageMousePixelsPerMinute,
    double AverageIdleSeconds,
    IReadOnlyList<PenaltySummary> Penalties,
    IReadOnlyList<BlacklistSummary> Blacklist)
{
    public TimeSpan Duration => EndedAt > StartedAt ? EndedAt - StartedAt : TimeSpan.Zero;
}

public sealed record DailyFocusSummary(
    DateOnly Day,
    double AverageFocus,
    int MinimumFocus,
    int SampleCount,
    int LowFocusSamples,
    double LowFocusRate,
    int BlacklistSamples,
    int InterventionSamples);
