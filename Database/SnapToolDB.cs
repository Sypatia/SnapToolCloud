using Npgsql;
using SnapToolCloud.Data;
using System.Text;
using System.Text.Json;

namespace SnapToolCloud.Database
{
    public class SnapToolDB
    {
        public static string ConnectionString => Globals.Config.GetConnectionString("Postgres");

        // ------------------ INITIALIZATION ------------------
        public static async Task InitializeAsync()
        {
            await using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();

            await EnsureTableExists(conn, "mooring_configs", @"
                CREATE TABLE mooring_configs (
                    id SERIAL PRIMARY KEY,
                    berth TEXT,
                    vessel TEXT,
                    mc TEXT,
                    bow TEXT,
                    fwdspring TEXT,
                    aftspring TEXT,
                    stern TEXT,
                    isLatest BOOLEAN
                );
                CREATE INDEX IF NOT EXISTS idx_mooring_configs_berth
                    ON mooring_configs(berth);
            ");

            await EnsureTableExists(conn, "weather_forecast", @"
                CREATE TABLE weather_forecast (
                    id SERIAL PRIMARY KEY,
                    localDateTime TIMESTAMP,
                    windDir TEXT,
                    windSpd REAL,
                    windGust REAL,
                    wind50 REAL,
                    seaHt REAL,
                    swell1Direction TEXT,
                    swell1Period REAL,
                    swell1Height REAL,
                    swell2Direction TEXT,
                    swell2Period REAL,
                    swell2Height REAL,
                    totalWaveSig REAL,
                    totalWaveMax REAL,
                    weatherNote TEXT,
                    confidence TEXT,
                    dateTimeUploaded TIMESTAMP,

                    recordIntervalHrs REAL,
                    recordHash TEXT,
                    blobUrl TEXT,
                    isLatest BOOLEAN
                );
                CREATE UNIQUE INDEX IF NOT EXISTS idx_weather_forecast_recordhash 
                    ON weather_forecast(recordHash);
                CREATE INDEX IF NOT EXISTS idx_weather_forecast_localdatetime 
                    ON weather_forecast(localDateTime);
            ");

            await EnsureTableExists(conn, "grid_activation", @"
                CREATE TABLE grid_activation (
                    id SERIAL PRIMARY KEY,
                    forecastHash TEXT,
                    dockingHashes TEXT[],
                    name TEXT,
                    activeStatus BOOLEAN,
                    dateTimeUploaded TIMESTAMP,
                    triggeringTension REAL,
                    timestampFrom TIMESTAMP,
                    timestampTo TIMESTAMP,
                    isLatest BOOLEAN
                );
            ");

            await EnsureTableExists(conn, "pd_activation", @"
                CREATE TABLE pd_activation (
                    id SERIAL PRIMARY KEY,
                    forecastHash TEXT,
                    dockingHashes TEXT[],
                    name TEXT,
                    activeStatus BOOLEAN,
                    triggeringTension REAL,
                    dateTimeUploaded TIMESTAMP,
                    timestampFrom TIMESTAMP,
                    timestampTo TIMESTAMP,
                    isLatest BOOLEAN
                );
            ");

            await EnsureTableExists(conn, "docking_arrangements", @"
               CREATE TABLE docking_arrangements (
                    id SERIAL PRIMARY KEY,
                    dateTimeUploaded TIMESTAMP,
                    berth TEXT,
                    vessel TEXT,
                    MC TEXT,
                    eventType TEXT,
                    allSecureDateTime TIMESTAMP,
                    sailDateTime TIMESTAMP,

                    recordHash TEXT,
                    isLatest BOOLEAN
                );
                CREATE UNIQUE INDEX IF NOT EXISTS idx_docking_arrangements_recordhash
                    ON docking_arrangements(recordHash);
            ");

            Console.WriteLine("✅ All tables ensured.");
        }

        public static async Task BulkInsertMooringConfigsAsync(
    NpgsqlConnection conn,
    NpgsqlTransaction tx,
    IEnumerable<(string Berth, string Vessel, string MC, string Bow, string FwdSpring, string AftSpring, string Stern)> configs)
        {
            using var writer = conn.BeginBinaryImport(@"
        COPY mooring_configs (
            berth, vessel, mc, bow, fwdspring, aftspring, stern, isLatest
        ) FROM STDIN (FORMAT BINARY)");

            foreach (var c in configs)
            {
                await writer.StartRowAsync();
                await writer.WriteAsync(c.Berth, NpgsqlTypes.NpgsqlDbType.Text);
                await writer.WriteAsync(c.Vessel, NpgsqlTypes.NpgsqlDbType.Text);
                await writer.WriteAsync(c.MC, NpgsqlTypes.NpgsqlDbType.Text);
                await writer.WriteAsync(c.Bow, NpgsqlTypes.NpgsqlDbType.Text);
                await writer.WriteAsync(c.FwdSpring, NpgsqlTypes.NpgsqlDbType.Text);
                await writer.WriteAsync(c.AftSpring, NpgsqlTypes.NpgsqlDbType.Text);
                await writer.WriteAsync(c.Stern, NpgsqlTypes.NpgsqlDbType.Text);
                await writer.WriteAsync(true, NpgsqlTypes.NpgsqlDbType.Boolean);
            }

            await writer.CompleteAsync();
        }

        private static async Task EnsureTableExists(NpgsqlConnection conn, string tableName, string createSql)
        {
            const string checkQuery = "SELECT EXISTS (SELECT FROM pg_tables WHERE schemaname='public' AND tablename=@name)";
            using var checkCmd = new NpgsqlCommand(checkQuery, conn);
            checkCmd.Parameters.AddWithValue("name", tableName);

            bool exists = (bool)await checkCmd.ExecuteScalarAsync();
            if (!exists)
            {
                // Split multi-statement SQL and execute each command individually
                var statements = createSql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var sql in statements)
                {
                    if (!string.IsNullOrWhiteSpace(sql))
                    {
                        using var cmd = new NpgsqlCommand(sql, conn);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                Console.WriteLine($"🆕 Created table: {tableName}");
            }
        }

        // ------------------ WEATHER FORECAST CRUD ------------------
        public static async Task<int> InsertWeatherForecastAsync(WeatherRecord record, string blobUrl)
        {
            using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();

            const string sql = @"
                INSERT INTO weather_forecast (
                    localDateTime, windDir, windSpd, windGust, wind50, seaHt,
                    swell1Direction, swell1Period, swell1Height,
                    swell2Direction, swell2Period, swell2Height,
                    totalWaveSig, totalWaveMax, weatherNote, confidence, recordIntervalHrs, recordHash,
                    dateTimeUploaded, blobUrl, isLatest)
                VALUES (
                    @localDateTime, @windDir, @windSpd, @windGust, @wind50, @seaHt,
                    @swell1Direction, @swell1Period, @swell1Height,
                    @swell2Direction, @swell2Period, @swell2Height,
                    @totalWaveSig, @totalWaveMax, @weatherNote, @confidence, @recordIntervalHrs, @recordHash,
                    NOW(), @blobUrl, @isLatest)
                RETURNING id";

            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("localDateTime", TimeZoneParser.AsPerthLocal(record.DateTimeForecast));
            cmd.Parameters.AddWithValue("windDir", (object?)record.WindDirection ?? DBNull.Value);
            cmd.Parameters.AddWithValue("windSpd", record.WindSpeed);
            cmd.Parameters.AddWithValue("windGust", record.WindGustSpeed);
            cmd.Parameters.AddWithValue("wind50", record.Wind50Speed);
            cmd.Parameters.AddWithValue("seaHt", record.SeaHeight);
            cmd.Parameters.AddWithValue("swell1Direction", (object?)record.SwellDirection1 ?? DBNull.Value);
            cmd.Parameters.AddWithValue("swell1Period", record.SwellPeriod1);
            cmd.Parameters.AddWithValue("swell1Height", record.SwellHeight1);
            cmd.Parameters.AddWithValue("swell2Direction", (object?)record.SwellDirection2 ?? DBNull.Value);
            cmd.Parameters.AddWithValue("swell2Period", record.SwellPeriod2);
            cmd.Parameters.AddWithValue("swell2Height", record.SwellHeight2);
            cmd.Parameters.AddWithValue("totalWaveSig", record.TotalWaveSig);
            cmd.Parameters.AddWithValue("totalWaveMax", record.TotalWaveMax);
            cmd.Parameters.AddWithValue("weatherNote", (object?)record.WeatherDescription ?? DBNull.Value);
            cmd.Parameters.AddWithValue("confidence", (object?)record.ForecastConfidence ?? DBNull.Value);
            cmd.Parameters.AddWithValue("recordIntervalHrs", (object?)record.RecordIntervalHours ?? DBNull.Value);
            cmd.Parameters.AddWithValue("recordHash", record.RecordHash);
            cmd.Parameters.AddWithValue("blobUrl", (object?)blobUrl ?? DBNull.Value);
            cmd.Parameters.AddWithValue("isLatest", true);

            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        public static async Task<bool> WeatherForecastExistsAsync(string recordHash)
        {
            await using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();

            const string sql = "SELECT EXISTS (SELECT 1 FROM weather_forecast WHERE recordHash=@hash)";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("hash", recordHash);

            return (bool)await cmd.ExecuteScalarAsync();
        }

        public static async Task<bool> WeatherTimestampExistsAsync(
    NpgsqlConnection conn,
    NpgsqlTransaction tx,
    DateTime localDateTime)
        {
            const string sql = @"
        SELECT EXISTS (
            SELECT 1 FROM weather_forecast
            WHERE localDateTime = @dt
        )";

            using var cmd = new NpgsqlCommand(sql, conn, tx);
            cmd.Parameters.AddWithValue("dt", localDateTime);

            return (bool)await cmd.ExecuteScalarAsync();
        }

        public static async Task<int> ClearWeatherByDateTimeAsync(
    NpgsqlConnection conn,
    NpgsqlTransaction tx,
    DateTime dateTime)
        {
            const string sql = @"
        UPDATE weather_forecast
        SET isLatest = FALSE
        WHERE isLatest = TRUE
          AND localDateTime = @dt";

            using var cmd = new NpgsqlCommand(sql, conn, tx);
            cmd.Parameters.AddWithValue("dt", dateTime);

            return await cmd.ExecuteNonQueryAsync();
        }


        // ------------------ DOCKING ARRANGEMENTS CRUD ------------------
        public static async Task<int> InsertDockingArrangementAsync(DockingArrangementRecord record)
        {
            using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();

            const string sql = @"
                INSERT INTO docking_arrangements (
                    dateTimeUploaded,
                    berth,
                    vessel,
                    MC,
                    eventType,
                    allSecureDateTime,
                    sailDateTime,
                    recordHash
                )
                VALUES (
                    NOW(),
                    @berth,
                    @vessel,
                    @MC,
                    @eventType,
                    @allSecureDateTime,
                    @sailDateTime,
                    @recordHash
                )
                RETURNING id";

            using var cmd = new NpgsqlCommand(sql, conn);

            cmd.Parameters.AddWithValue("berth", record.Berth);
            cmd.Parameters.AddWithValue("vessel", record.Vessel);
            cmd.Parameters.AddWithValue("MC", record.MC ?? "Unknown");
            cmd.Parameters.AddWithValue("eventType", record.EventType ?? "Unknown");
            cmd.Parameters.AddWithValue("allSecureDateTime", TimeZoneParser.AsPerthLocal(record.AllSecureDateTime));
            cmd.Parameters.AddWithValue("sailDateTime", TimeZoneParser.AsPerthLocal(record.SailDateTime));
            cmd.Parameters.AddWithValue("recordHash", record.RecordHash);
            cmd.Parameters.AddWithValue("isLatest", true);

            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        public static async Task<int> ClearScenarioAsync(
            NpgsqlConnection conn,
            NpgsqlTransaction tx,
            TableDef table,
            DateTime weatherStart,
            DateTime weatherEnd,
            string[] berths)
        {
            const string sql = @"
        UPDATE {0}
        SET isLatest = FALSE
        WHERE isLatest = TRUE
          AND timestampFrom <= @end
          AND timestampTo   >= @start
          AND (dockingHashes && @berths)";

            string finalSql = string.Format(sql, table.Name);

            using var cmd = new NpgsqlCommand(finalSql, conn, tx);
            cmd.Parameters.AddWithValue("start", weatherStart);
            cmd.Parameters.AddWithValue("end", weatherEnd);
            cmd.Parameters.AddWithValue("berths", berths);

            return await cmd.ExecuteNonQueryAsync();
        }

        public static async Task<bool> ScenarioExistsAsync(
    NpgsqlConnection conn,
    NpgsqlTransaction tx,
    TableDef table,
    DateTime weatherStart,
    DateTime weatherEnd,
    string[] berths)
        {
            const string sql = @"
        SELECT EXISTS (
            SELECT 1 
            FROM {0}
            WHERE isLatest = TRUE
              AND timestampFrom <= @end
              AND timestampTo   >= @start
              AND (dockingHashes && @berths)
        )";

            string finalSql = string.Format(sql, table.Name);

            using var cmd = new NpgsqlCommand(finalSql, conn, tx);
            cmd.Parameters.AddWithValue("start", weatherStart);
            cmd.Parameters.AddWithValue("end", weatherEnd);
            cmd.Parameters.AddWithValue("berths", berths);

            return (bool)await cmd.ExecuteScalarAsync();
        }


        public static async Task<bool> DockingArrangementExistsAsync(
            NpgsqlConnection conn,
            NpgsqlTransaction? tx,
            string recordHash)
        {
            const string sql = @"
        SELECT EXISTS (
            SELECT 1
            FROM docking_arrangements
            WHERE recordHash = @hash
        )";

            using var cmd = new NpgsqlCommand(sql, conn, tx);
            cmd.Parameters.AddWithValue("hash", recordHash);

            return (bool)await cmd.ExecuteScalarAsync();
        }

        public static async Task<bool> DockingBerthExistsAsync(
    NpgsqlConnection conn,
    NpgsqlTransaction tx,
    string berth)
        {
            const string sql = @"
        SELECT EXISTS (
            SELECT 1 FROM docking_arrangements
            WHERE berth = @berth
        )";

            using var cmd = new NpgsqlCommand(sql, conn, tx);
            cmd.Parameters.AddWithValue("berth", berth);

            return (bool)await cmd.ExecuteScalarAsync();
        }

        // ------------------ BULK INSERT HELPERS ------------------
        public static async Task BulkInsertGridActivationsAsync(
            NpgsqlConnection conn,
            NpgsqlTransaction tx,
            IEnumerable<(string ForecastHash, string[] DockingHashes, string Name, bool ActiveStatus, double TriggeringTension, DateTime TimestampFrom, DateTime TimestampTo)> records)
        {
            using var writer = conn.BeginBinaryImport(
                "COPY grid_activation (forecastHash, dockingHashes, name, activeStatus, triggeringTension, dateTimeUploaded, timestampFrom, timestampTo, isLatest) FROM STDIN (FORMAT BINARY)"
            );

            foreach (var r in records)
            {
                await writer.StartRowAsync();
                await writer.WriteAsync(r.ForecastHash, NpgsqlTypes.NpgsqlDbType.Text);
                await writer.WriteAsync(r.DockingHashes, NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text);
                await writer.WriteAsync(r.Name, NpgsqlTypes.NpgsqlDbType.Text);
                await writer.WriteAsync(r.ActiveStatus, NpgsqlTypes.NpgsqlDbType.Boolean);
                await writer.WriteAsync(r.TriggeringTension, NpgsqlTypes.NpgsqlDbType.Real);
                await writer.WriteAsync(TimeZoneParser.AsPerthLocal(DateTime.UtcNow), NpgsqlTypes.NpgsqlDbType.Timestamp);
                await writer.WriteAsync(TimeZoneParser.AsPerthLocal(r.TimestampFrom), NpgsqlTypes.NpgsqlDbType.Timestamp);
                await writer.WriteAsync(TimeZoneParser.AsPerthLocal(r.TimestampTo), NpgsqlTypes.NpgsqlDbType.Timestamp);
                await writer.WriteAsync(true, NpgsqlTypes.NpgsqlDbType.Boolean);
            }

            await writer.CompleteAsync();
        }

        public static async Task BulkInsertPdActivationsAsync(
            NpgsqlConnection conn,
            NpgsqlTransaction tx,
            IEnumerable<(string ForecastHash, string[] DockingHashes, string Name, bool ActiveStatus, double TriggeringTension, DateTime TimestampFrom, DateTime TimestampTo)> records)
        {

            using var writer = conn.BeginBinaryImport(@"
        COPY pd_activation (forecastHash, dockingHashes, name, activeStatus, triggeringTension, dateTimeUploaded, timestampFrom, timestampTo, isLatest)
        FROM STDIN (FORMAT BINARY)");

            foreach (var r in records)
            {
                await writer.StartRowAsync();
                await writer.WriteAsync(r.ForecastHash, NpgsqlTypes.NpgsqlDbType.Text);
                await writer.WriteAsync(r.DockingHashes, NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text);
                await writer.WriteAsync(r.Name, NpgsqlTypes.NpgsqlDbType.Text);
                await writer.WriteAsync(r.ActiveStatus, NpgsqlTypes.NpgsqlDbType.Boolean);
                await writer.WriteAsync(r.TriggeringTension, NpgsqlTypes.NpgsqlDbType.Real);
                await writer.WriteAsync(TimeZoneParser.AsPerthLocal(DateTime.UtcNow), NpgsqlTypes.NpgsqlDbType.Timestamp);
                await writer.WriteAsync(TimeZoneParser.AsPerthLocal(r.TimestampFrom), NpgsqlTypes.NpgsqlDbType.Timestamp);
                await writer.WriteAsync(TimeZoneParser.AsPerthLocal(r.TimestampTo), NpgsqlTypes.NpgsqlDbType.Timestamp);
                await writer.WriteAsync(true, NpgsqlTypes.NpgsqlDbType.Boolean);
            }

            await writer.CompleteAsync();
            Console.WriteLine($"✅ Bulk inserted {records.Count()} pd_activation records");
        }

        public static async Task BulkInsertDockingArrangementsAsync(
            NpgsqlConnection conn,
            NpgsqlTransaction tx,
            IEnumerable<DockingArrangementRecord> records)
        {

            using var writer = conn.BeginBinaryImport(@"
            COPY docking_arrangements (
                dateTimeUploaded,
                berth,
                vessel,
                MC,
                eventType,
                allSecureDateTime,
                sailDateTime,
                recordHash,
                isLatest
            ) FROM STDIN (FORMAT BINARY)");

            foreach (var r in records)
            {
                await writer.StartRowAsync();
                await writer.WriteAsync(TimeZoneParser.AsPerthLocal(DateTime.UtcNow), NpgsqlTypes.NpgsqlDbType.Timestamp);
                await writer.WriteAsync(r.Berth, NpgsqlTypes.NpgsqlDbType.Text);
                await writer.WriteAsync(r.Vessel, NpgsqlTypes.NpgsqlDbType.Text);
                await writer.WriteAsync(r.MC ?? "Unknown", NpgsqlTypes.NpgsqlDbType.Text);
                await writer.WriteAsync(r.EventType ?? "Unknown", NpgsqlTypes.NpgsqlDbType.Text);
                await writer.WriteAsync(TimeZoneParser.AsPerthLocal(r.AllSecureDateTime), NpgsqlTypes.NpgsqlDbType.Timestamp);
                await writer.WriteAsync(TimeZoneParser.AsPerthLocal(r.SailDateTime), NpgsqlTypes.NpgsqlDbType.Timestamp);
                await writer.WriteAsync(r.RecordHash, NpgsqlTypes.NpgsqlDbType.Text);
                await writer.WriteAsync(true, NpgsqlTypes.NpgsqlDbType.Boolean);
            }

            await writer.CompleteAsync();
        }
        public static async Task<int> ClearAllLatestAsync(
            NpgsqlConnection conn,
            NpgsqlTransaction tx,
            TableDef table)
        {
            string sql = $@"
        UPDATE {table.Name}
        SET isLatest = FALSE
        WHERE isLatest = TRUE";

            using var cmd = new NpgsqlCommand(sql, conn, tx);
            return await cmd.ExecuteNonQueryAsync();
        }


        public static async Task<int> ClearByForecastAndDockingAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        TableDef table,
        string forecastHash,
        string[] dockingHashes)
        {
            string sql = $@"
        UPDATE {table.Name}
        SET isLatest = FALSE
        WHERE isLatest = TRUE
          AND {table.HashColumn} = @forecastHash
          AND dockingHashes @> @hashes";

            using var cmd = new NpgsqlCommand(sql, conn, tx);
            cmd.Parameters.AddWithValue("forecastHash", forecastHash);
            cmd.Parameters.AddWithValue("hashes", dockingHashes);

            return await cmd.ExecuteNonQueryAsync();
        }

        public static async Task<int> ClearByBerthAsync(
    NpgsqlConnection conn,
    NpgsqlTransaction tx,
    string berth)
        {
            const string sql = @"
        UPDATE docking_arrangements
        SET isLatest = FALSE
        WHERE isLatest = TRUE
          AND berth = @berth";

            using var cmd = new NpgsqlCommand(sql, conn, tx);
            cmd.Parameters.AddWithValue("berth", berth);

            return await cmd.ExecuteNonQueryAsync();
        }

        public static async Task<bool> ActivationExistsAsync(
    NpgsqlConnection conn,
    NpgsqlTransaction tx,
    TableDef table,
    string forecastHash,
    string[] dockingHashes)
        {
            // order-insensitive check using array comparison
            const string sql = @"
        SELECT EXISTS (
            SELECT 1
            FROM {0}
            WHERE forecastHash = @forecastHash
              AND dockingHashes @> @docking
              AND dockingHashes <@ @docking
        )";

            string finalSql = string.Format(sql, table.Name);

            using var cmd = new NpgsqlCommand(finalSql, conn, tx);
            cmd.Parameters.AddWithValue("forecastHash", forecastHash);
            cmd.Parameters.AddWithValue("docking", dockingHashes);

            return (bool)await cmd.ExecuteScalarAsync();
        }

        public static async Task<int> ClearByRecordHashAsync(
    NpgsqlConnection conn,
    NpgsqlTransaction tx,
    TableDef table,
    string recordHash)
        {
            string sql = $@"
        UPDATE {table.Name}
        SET isLatest = FALSE
        WHERE isLatest = TRUE
          AND {table.HashColumn} = @recordHash";

            using var cmd = new NpgsqlCommand(sql, conn, tx);
            cmd.Parameters.AddWithValue("recordHash", recordHash);

            return await cmd.ExecuteNonQueryAsync();
        }

        public static string CanonicalActivationKey(string forecastHash, string[] dockingHashes)
        {
            var sorted = dockingHashes.OrderBy(x => x).ToArray();
            return $"{forecastHash}::{string.Join("|", sorted)}";
        }
    }

        public readonly struct TableDef
        {
            public string Name { get; }
            public string HashColumn { get; }

            public TableDef(string name, string hashColumn)
            {
                Name = name;
                HashColumn = hashColumn;
            }

            public override string ToString() => Name;
        }


        public static class Tables
        {
            public static readonly TableDef WeatherForecasts =
                new("weather_forecast", "recordHash");

            public static readonly TableDef ActivationsGrids =
                new("grid_activation", "forecastHash");

            public static readonly TableDef ActivationsPD =
                new("pd_activation", "forecastHash");

            public static readonly TableDef Docking =
                new("docking_arrangements", "recordHash");
        }
    }

