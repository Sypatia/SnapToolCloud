using Npgsql;
using SnapToolCloud.Data;

namespace SnapToolCloud.Database
{
    public class Database
    {
        private static string ConnectionString =>
    Globals.Config.GetConnectionString("Postgres");

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
                    blobUrl TEXT
                )");

            await EnsureTableExists(conn, "granular_activation", @"
                CREATE TABLE granular_activation (
                    id SERIAL PRIMARY KEY,
                    forecastId INT REFERENCES weather_forecast(id),
                    dockingId INT,
                    activePiles TEXT[],
                    activeDolphins TEXT[],
                    activeRegions TEXT[],
                    dateTimeUploaded TIMESTAMP
                )");

            await EnsureTableExists(conn, "consolidated_activation", @"
                CREATE TABLE consolidated_activation (
                    id SERIAL PRIMARY KEY,
                    granularIds INT[],
                    activePiles TEXT[],
                    activeDolphins TEXT[],
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
                    lastModifiedPortsDB TIMESTAMP
                )");

            Console.WriteLine("✅ All tables ensured.");
        }

        private static async Task EnsureTableExists(NpgsqlConnection conn, string tableName, string createSql)
        {
            const string checkQuery =
                "SELECT EXISTS (SELECT FROM pg_tables WHERE schemaname='public' AND tablename=@name)";
            using var checkCmd = new NpgsqlCommand(checkQuery, conn);
            checkCmd.Parameters.AddWithValue("name", tableName);

            bool exists = (bool)await checkCmd.ExecuteScalarAsync();
            if (!exists)
            {
                using var createCmd = new NpgsqlCommand(createSql, conn);
                await createCmd.ExecuteNonQueryAsync();
                Console.WriteLine($"🆕 Created table: {tableName}");
            }
        }

        // ------------------ WEATHER FORECAST CRUD ------------------
        public static async Task<int> InsertWeatherForecastAsync(DateTime localDateTime, string windDir, float windSpd)
        {
            using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();

            const string sql = @"
                INSERT INTO weather_forecast (localDateTime, windDir, windSpd, dateTimeUploaded)
                VALUES (@localDateTime, @windDir, @windSpd, NOW())
                RETURNING id";

            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("localDateTime", localDateTime);
            cmd.Parameters.AddWithValue("windDir", windDir ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("windSpd", windSpd);

            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        public static async Task<List<(int id, DateTime localDateTime, string windDir, float windSpd)>> GetAllWeatherForecastsAsync()
        {
            using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();

            const string sql = "SELECT id, localDateTime, windDir, windSpd FROM weather_forecast ORDER BY id DESC";
            using var cmd = new NpgsqlCommand(sql, conn);
            using var reader = await cmd.ExecuteReaderAsync();

            var results = new List<(int, DateTime, string, float)>();
            while (await reader.ReadAsync())
                results.Add((reader.GetInt32(0), reader.GetDateTime(1), reader.GetString(2), reader.GetFloat(3)));

            return results;
        }

        public static async Task UpdateWeatherForecastAsync(int id, float newWindSpeed)
        {
            using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();

            const string sql = "UPDATE weather_forecast SET windSpd=@windSpd WHERE id=@id";
            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("windSpd", newWindSpeed);

            await cmd.ExecuteNonQueryAsync();
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

        // ------------------ GRANULAR ACTIVATION CRUD ------------------
        public static async Task<int> InsertGranularActivationAsync(int forecastId, int dockingId, string[] activePiles)
        {
            using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();

            const string sql = @"
                INSERT INTO granular_activation (forecastId, dockingId, activePiles, dateTimeUploaded)
                VALUES (@forecastId, @dockingId, @activePiles, NOW())
                RETURNING id";

            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("forecastId", forecastId);
            cmd.Parameters.AddWithValue("dockingId", dockingId);
            cmd.Parameters.AddWithValue("activePiles", activePiles);

            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        // ------------------ CONSOLIDATED ACTIVATION CRUD ------------------
        public static async Task<int> InsertConsolidatedActivationAsync(int[] granularIds, string[] activePiles)
        {
            using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();

            const string sql = @"
                INSERT INTO consolidated_activation (granularIds, activePiles)
                VALUES (@granularIds, @activePiles)
                RETURNING id";

            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("granularIds", granularIds);
            cmd.Parameters.AddWithValue("activePiles", activePiles);

            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        // ------------------ DOCKING ARRANGEMENTS CRUD ------------------
        public static async Task<int> InsertDockingArrangementAsync(
            string berth, string vessel, string eventType, DateTime personOnBoard, DateTime pilotOff)
        {
            using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();

            const string sql = @"
                INSERT INTO docking_arrangements (dateTimeUploaded, berth, vessel, eventType, personOnBoard, pilotOff, lastModifiedPortsDB)
                VALUES (NOW(), @berth, @vessel, @eventType, @personOnBoard, @pilotOff, NOW())
                RETURNING id";

            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("berth", berth);
            cmd.Parameters.AddWithValue("vessel", vessel);
            cmd.Parameters.AddWithValue("eventType", eventType);
            cmd.Parameters.AddWithValue("personOnBoard", personOnBoard);
            cmd.Parameters.AddWithValue("pilotOff", pilotOff);

            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        // ------------------ WEATHER FORECAST RANGE QUERY ------------------
        public static async Task<List<(int id, DateTime localDateTime, string windDir, float windSpd)>>
            GetWeatherForecastsInRangeAsync(DateTime start, DateTime end)
        {
            using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();

            const string sql = @"
        SELECT id, localDateTime, windDir, windSpd
        FROM weather_forecast
        WHERE localDateTime BETWEEN @start AND @end
        ORDER BY localDateTime ASC";

            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("start", start);
            cmd.Parameters.AddWithValue("end", end);

            using var reader = await cmd.ExecuteReaderAsync();
            var results = new List<(int, DateTime, string, float)>();

            while (await reader.ReadAsync())
                results.Add((reader.GetInt32(0), reader.GetDateTime(1), reader.GetString(2), reader.GetFloat(3)));

            return results;
        }

        // ------------------ DOCKING ARRANGEMENTS RANGE QUERY ------------------
        public static async Task<List<(int id, string berth, string vessel, DateTime dateTimeUploaded)>>
            GetDockingArrangementsInRangeAsync(DateTime start, DateTime end)
        {
            using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();

            const string sql = @"
        SELECT id, berth, vessel, dateTimeUploaded
        FROM docking_arrangements
        WHERE dateTimeUploaded BETWEEN @start AND @end
        ORDER BY dateTimeUploaded ASC";

            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("start", start);
            cmd.Parameters.AddWithValue("end", end);

            using var reader = await cmd.ExecuteReaderAsync();
            var results = new List<(int, string, string, DateTime)>();

            while (await reader.ReadAsync())
                results.Add((reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetDateTime(3)));

            return results;
        }

    }
}