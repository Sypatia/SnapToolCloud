using Npgsql;
using SnapToolCloud.Data;
using System.Text;
using System.Text.Json;

namespace SnapToolCloud.Database
{
    public class SnapToolDB
    {
        private static string ConnectionString => Globals.Config.GetConnectionString("Postgres");

        // ------------------ INITIALIZATION ------------------
        public static async Task InitializeAsync()
        {
            await using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();

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
                    blobUrl TEXT
                );
                CREATE UNIQUE INDEX IF NOT EXISTS idx_weather_forecast_recordhash 
                    ON weather_forecast(recordHash);
            ");

            await EnsureTableExists(conn, "granular_activation", @"
                CREATE TABLE granular_activation (
                    id SERIAL PRIMARY KEY,
                    forecastId INT REFERENCES weather_forecast(id),
                    dockingId INT,
                    activePileDolphins TEXT[],
                    activeRegions TEXT[],
                    dateTimeUploaded TIMESTAMP
                )");

            await EnsureTableExists(conn, "consolidated_activation", @"
                CREATE TABLE consolidated_activation (
                    id SERIAL PRIMARY KEY,
                    granularIds INT[],
                    activePileDolphins TEXT[],
                    activeRegions TEXT[]
                )");

            await EnsureTableExists(conn, "docking_arrangements", @"
                CREATE TABLE docking_arrangements (
                    id SERIAL PRIMARY KEY,
                    dateTimeUploaded TIMESTAMP,
                    berth TEXT,
                    vessel TEXT,
                    eventType TEXT,
                    personOnBoard TIMESTAMP,
                    pilotOff TIMESTAMP,
                    lastModifiedPortsDB TIMESTAMP,
                    recordHash TEXT
                );
                CREATE UNIQUE INDEX IF NOT EXISTS idx_docking_arrangements_recordhash
                    ON docking_arrangements(recordHash);
            ");

            Console.WriteLine("✅ All tables ensured.");
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
                    dateTimeUploaded, blobUrl)
                VALUES (
                    @localDateTime, @windDir, @windSpd, @windGust, @wind50, @seaHt,
                    @swell1Direction, @swell1Period, @swell1Height,
                    @swell2Direction, @swell2Period, @swell2Height,
                    @totalWaveSig, @totalWaveMax, @weatherNote, @confidence, @recordIntervalHrs, @recordHash,
                    NOW(), @blobUrl)
                RETURNING id";

            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("localDateTime", record.DateTime);
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

            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }
        public static async Task UpdateWeatherForecastAsync(int id, WeatherRecord record, string blobUrl)
        {
            using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();

            const string sql = @"
                UPDATE weather_forecast SET
                    localDateTime=@localDateTime,
                    windDir=@windDir,
                    windSpd=@windSpd,
                    windGust=@windGust,
                    wind50=@wind50,
                    seaHt=@seaHt,
                    swell1Direction=@swell1Direction,
                    swell1Period=@swell1Period,
                    swell1Height=@swell1Height,
                    swell2Direction=@swell2Direction,
                    swell2Period=@swell2Period,
                    swell2Height=@swell2Height,
                    totalWaveSig=@totalWaveSig,
                    totalWaveMax=@totalWaveMax,
                    weatherNote=@weatherNote,
                    confidence=@confidence,
                    recordIntervalHrs=@recordIntervalHrs,  
                    recordHash=@recordHash,
                    blobUrl=@blobUrl
                WHERE id=@id";

            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("localDateTime", record.DateTime);
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

            await cmd.ExecuteNonQueryAsync();
        }

        public static async Task<WeatherRecord?> GetWeatherForecastByIdAsync(int id)
        {
            using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();

            const string sql = "SELECT * FROM weather_forecast WHERE id=@id";
            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", id);

            using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            return new WeatherRecord
            {
                DateTime = reader.GetDateTime(reader.GetOrdinal("localDateTime")),
                WindDirection = reader["windDir"] as string ?? "",
                WindSpeed = reader.GetDouble(reader.GetOrdinal("windSpd")),
                WindGustSpeed = reader.GetDouble(reader.GetOrdinal("windGust")),
                Wind50Speed = reader.GetDouble(reader.GetOrdinal("wind50")),
                SeaHeight = reader.GetDouble(reader.GetOrdinal("seaHt")),
                SwellDirection1 = reader["swell1Direction"] as string ?? "",
                SwellPeriod1 = reader.GetDouble(reader.GetOrdinal("swell1Period")),
                SwellHeight1 = reader.GetDouble(reader.GetOrdinal("swell1Height")),
                SwellDirection2 = reader["swell2Direction"] as string ?? "",
                SwellPeriod2 = reader.GetDouble(reader.GetOrdinal("swell2Period")),
                SwellHeight2 = reader.GetDouble(reader.GetOrdinal("swell2Height")),
                TotalWaveSig = reader.GetDouble(reader.GetOrdinal("totalWaveSig")),
                TotalWaveMax = reader.GetDouble(reader.GetOrdinal("totalWaveMax")),
                RecordIntervalHours = reader.GetDouble(reader.GetOrdinal("recordIntervalHrs")),
                WeatherDescription = reader["weatherNote"] as string ?? "",
                ForecastConfidence = reader["confidence"] as string ?? ""
            };
        }

        public static async Task DeleteWeatherForecastAsync(int id)
        {
            using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();

            const string sql = "DELETE FROM weather_forecast WHERE id=@id";
            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", id);

            await cmd.ExecuteNonQueryAsync();
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

        // ------------------ CONSOLIDATED ACTIVATION CRUD ------------------
        public static async Task<int> InsertConsolidatedActivationAsync(
            int[] granularIds, string[] activePiles, string[] activeDolphins, string[] activeRegions)
        {
            using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();

            const string sql = @"
                INSERT INTO consolidated_activation (
                    granularIds, activePiles, activeDolphins, activeRegions)
                VALUES (@granularIds, @activePiles, @activeDolphins, @activeRegions)
                RETURNING id";

            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("granularIds", granularIds);
            cmd.Parameters.AddWithValue("activePiles", activePiles);
            cmd.Parameters.AddWithValue("activeDolphins", activeDolphins);
            cmd.Parameters.AddWithValue("activeRegions", activeRegions);

            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        // ------------------ GRANULAR ACTIVATION CRUD ------------------
        public static async Task<int> InsertGranularActivationAsync(
            int forecastId,
            int dockingId,
            List<GridRecord> activeRegions,
            List<PileDolphinRecord> activePiles,
            List<PileDolphinRecord> activeDolphins)
        {
            using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();

            // Serialize record lists to JSON
            string pilesJson = JsonSerializer.Serialize(activePiles);
            string dolphinsJson = JsonSerializer.Serialize(activeDolphins);
            string regionsJson = JsonSerializer.Serialize(activeRegions);

            const string sql = @"
                INSERT INTO granular_activation (
                    forecastId, dockingId, activePiles, activeDolphins, activeRegions, dateTimeUploaded)
                VALUES (@forecastId, @dockingId, @activePiles, @activeDolphins, @activeRegions, NOW())
                RETURNING id";

            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("forecastId", forecastId);
            cmd.Parameters.AddWithValue("dockingId", dockingId);
            cmd.Parameters.AddWithValue("activePiles", pilesJson);
            cmd.Parameters.AddWithValue("activeDolphins", dolphinsJson);
            cmd.Parameters.AddWithValue("activeRegions", regionsJson);

            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        public static async Task<(List<GridRecord> Regions, List<PileDolphinRecord> Piles, List<PileDolphinRecord> Dolphins)?>
            GetGranularActivationAsync(int id)
        {
            using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();

            const string sql = @"
                SELECT activePiles, activeDolphins, activeRegions
                FROM granular_activation
                WHERE id=@id";

            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", id);

            using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var piles = JsonSerializer.Deserialize<List<PileDolphinRecord>>(reader["activePiles"]?.ToString() ?? "[]");
            var dolphins = JsonSerializer.Deserialize<List<PileDolphinRecord>>(reader["activeDolphins"]?.ToString() ?? "[]");
            var regions = JsonSerializer.Deserialize<List<GridRecord>>(reader["activeRegions"]?.ToString() ?? "[]");

            return (regions ?? new(), piles ?? new(), dolphins ?? new());
        }

        public static async Task DeleteGranularActivationAsync(int id)
        {
            using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();

            const string sql = "DELETE FROM granular_activation WHERE id=@id";
            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", id);

            await cmd.ExecuteNonQueryAsync();
        }

        // ------------------ DOCKING ARRANGEMENTS CRUD ------------------
        public static async Task<int> InsertDockingArrangementAsync(DockingArrangementRecord record)
        {
            using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();

            const string sql = @"
                INSERT INTO docking_arrangements (
                    dateTimeUploaded, berth, vessel, eventType, personOnBoard, pilotOff, recordHash, lastModifiedPortsDB)
                VALUES (NOW(), @berth, @vessel, @eventType, @personOnBoard, @pilotOff, @recordHash, NOW())
                RETURNING id";

            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("berth", record.Berth);
            cmd.Parameters.AddWithValue("vessel", record.Vessel);
            cmd.Parameters.AddWithValue("eventType", record.MC ?? "Unknown");
            cmd.Parameters.AddWithValue("personOnBoard", record.PlanToBerth);
            cmd.Parameters.AddWithValue("pilotOff", record.PlanToSail);
            cmd.Parameters.AddWithValue("recordHash", record.RecordHash);

            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        public static async Task<DockingArrangementRecord?> GetDockingArrangementAsync(int id)
        {
            using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();

            const string sql = @"
                SELECT berth, vessel, eventType, personOnBoard, pilotOff
                FROM docking_arrangements
                WHERE id=@id";

            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", id);

            using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            return new DockingArrangementRecord
            {
                Berth = reader["berth"] as string ?? "",
                Vessel = reader["vessel"] as string ?? "",
                MC = reader["eventType"] as string ?? "",
                VesselName = reader["vessel"] as string ?? "",
                PlanToBerth = reader.GetDateTime(reader.GetOrdinal("personOnBoard")),
                PlanToSail = reader.GetDateTime(reader.GetOrdinal("pilotOff"))
            };
        }

        public static async Task DeleteDockingArrangementAsync(int id)
        {
            using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();

            const string sql = "DELETE FROM docking_arrangements WHERE id=@id";
            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", id);

            await cmd.ExecuteNonQueryAsync();
        }


        public static async Task<bool> DockingArrangementExistsAsync(string recordHash)
        {
            await using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();

            const string sql = "SELECT EXISTS (SELECT 1 FROM docking_arrangements WHERE recordHash=@hash)";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("hash", recordHash);

            return (bool)await cmd.ExecuteScalarAsync();
        }

    }
}
