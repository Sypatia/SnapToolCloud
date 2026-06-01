using Npgsql;
using SnapToolCloud.Data;
using SnapToolCloud.Database;
using System.Data;
using System.Globalization;
using System.Reflection.Metadata.Ecma335;

namespace SnapToolCloud.Service
{
    public class SnapToolService
    {
        private static void RunNewAnalyses()
        {

        }
        private static (List<PileDolphinRecord> PileDolphinActivations, List<GridRecord> GridActivations) ConsolidateActivations(List<PileDolphinRecord> activePileDolphins, List<GridRecord> activeGrids)
        {

            // ---- Helpers ----
            string Key(string s) => s?.Trim().ToUpperInvariant();

            // -------------------------------------------------------------
            // 1. LOAD UNIQUE MASTER LISTS
            // -------------------------------------------------------------
            var masterPiles =
                PileDolphinDataLoader.ParseRecords()
                    .GroupBy(m => Key(m.CombinedLocation))
                    .Select(g => g.First() with { TriggeringTension = 0 })
                    .ToDictionary(m => Key(m.CombinedLocation), m => m);

            var masterGrids =
                GridDataLoader.ParseRecords()
                    .GroupBy(m => Key(m.CombinedLocation))
                    .Select(g => g.First() with { TriggeringTension = 0 })
                    .ToDictionary(m => Key(m.CombinedLocation), m => m);

            // -------------------------------------------------------------
            // 2. GROUP ACTIVE RECORDS BY UNIQUE CombinedLocation
            // -------------------------------------------------------------
            var pileGroups =
                activePileDolphins
                    .GroupBy(p => Key(p.CombinedLocation))
                    .ToDictionary(g => g.Key, g => g.ToList());

            var gridGroups =
                activeGrids
                    .GroupBy(g => Key(g.CombinedLocation))
                    .ToDictionary(g => g.Key, g => g.ToList());

            // -------------------------------------------------------------
            // 3. FINAL MERGE PER UNIQUE CombinedLocation
            // -------------------------------------------------------------
            List<PileDolphinRecord> finalPiles = new();
            List<GridRecord> finalGrids = new();

            // ---- PILES ----
            foreach (var key in masterPiles.Keys)
            {
                if (pileGroups.TryGetValue(key, out var matches))
                {
                    var actives = matches.Where(x => x.InEnvelope).ToList();

                    if (actives.Any())
                    {
                        // case 1: ACTIVE exists → highest tension ACTIVE
                        finalPiles.Add(
                            actives.OrderByDescending(x => x.TriggeringTension).First()
                        );
                    }
                    else
                    {
                        // case 2: No active → highest tension INACTIVE
                        finalPiles.Add(
                            matches.OrderByDescending(x => x.TriggeringTension).First()
                        );
                    }
                }
                else
                {
                    // nothing at all for this location → master fallback
                    finalPiles.Add(masterPiles[key]);
                }
            }

            // ---- GRIDS ----
            foreach (var key in masterGrids.Keys)
            {
                if (gridGroups.TryGetValue(key, out var matches))
                {
                    var actives = matches.Where(x => x.InEnvelope).ToList();

                    if (actives.Any())
                    {
                        finalGrids.Add(
                            actives.OrderByDescending(x => x.TriggeringTension).First()
                        );
                    }
                    else
                    {
                        finalGrids.Add(
                            matches.OrderByDescending(x => x.TriggeringTension).First()
                        );
                    }
                }
                else
                {
                    finalGrids.Add(masterGrids[key]);
                }
            }

            return (finalPiles, finalGrids);
        }

        public static (List<PileDolphinRecord> PileDolphinActivations, List<GridRecord> GridActivations)
            GetActiveElements(List<WeatherRecord> weatherForecast, List<DockingArrangementRecord> dockingArrangements)
        {

            var activePileDolphins = new List<PileDolphinRecord>();
            var activeGrids = new List<GridRecord>();

            // -------------------------------------------------------------
            // 1. COLLECT ACTIVE RECORDS
            // -------------------------------------------------------------
            foreach (var weatherRecord in weatherForecast)
            {
                foreach (var dockingArrangement in dockingArrangements)
                {
                    var wind = DataFilter.FilterDMADataWindOnly(dockingArrangement, weatherRecord.DateTimeForecast, weatherForecast);
                    var wave = DataFilter.FilterDMADataWaveOnly(dockingArrangement, weatherRecord.DateTimeForecast, weatherForecast);

                    activePileDolphins.AddRange(DataFilter.FilterActiveRecords(wind, PileDolphinDataLoader.ParseRecords));
                    activePileDolphins.AddRange(DataFilter.FilterActiveRecords(wave, PileDolphinDataLoader.ParseRecords));

                    activeGrids.AddRange(DataFilter.FilterActiveRecords(wave, GridDataLoader.ParseRecords));
                    activeGrids.AddRange(DataFilter.FilterActiveRecords(wind, GridDataLoader.ParseRecords));
                }
            }

            var (finalPiles, finalGrids) = ConsolidateActivations(activePileDolphins, activeGrids);

            return (finalPiles, finalGrids);
        }
        public async static Task RunWorkflow(DateTime? startDate = null, DateTime? endDate = null)
        {
            startDate ??= new DateTime(2025, 11, 15);
            endDate ??= new DateTime(2025, 11, 25);

            await using var conn = new NpgsqlConnection(SnapToolDB.ConnectionString);
            await conn.OpenAsync();

            await using var tx = await conn.BeginTransactionAsync();

            try
            {
                // --------------------------------------------------
                // FETCH FORECASTS & DOCKING
                // --------------------------------------------------
                List<WeatherRecord> weatherForecast =
                    await WeatherService.GetRTIOB10ForecastAsync(from: startDate, to: endDate);

                List<DockingArrangementRecord> dockingArrangement =
                    await DockingService.GetDockingArrangementAsync();

                // Pre-calculate berths once (stable scenario identity)
                var berths = dockingArrangement
                    .Select(d => d.Berth)
                    .Distinct()
                    .ToArray();

                // --------------------------------------------------
                // INSERT WEATHER FORECASTS (Clear first)
                // --------------------------------------------------
                foreach (var record in weatherForecast)
                {
                    var dt = TimeZoneParser.AsPerthLocal(record.DateTimeForecast);

                    bool hashExists = await SnapToolDB.WeatherForecastExistsAsync(record.RecordHash);

                    if (!hashExists)
                    {
                        bool timestampExists = await SnapToolDB.WeatherTimestampExistsAsync(conn, tx, dt);

                        if (timestampExists)
                            await SnapToolDB.ClearWeatherByDateTimeAsync(conn, tx, dt);

                        await SnapToolDB.InsertWeatherForecastAsync(record, blobUrl: null);
                    }
                }

                // --------------------------------------------------
                // DOCKING ARRANGEMENTS: full refresh each run
                // --------------------------------------------------
                await SnapToolDB.ClearAllLatestAsync(conn, tx, Tables.Docking);
                await SnapToolDB.BulkInsertDockingArrangementsAsync(conn, tx, dockingArrangement);

                // --------------------------------------------------
                // PROCESS + BUILD OUTPUT LISTS
                // --------------------------------------------------
                var gridsForDB = new List<(string ForecastHash,
                                           string[] DockingHashes,
                                           string Name,
                                           bool ActiveStatus,
                                           double TriggeringTension,
                                           DateTime TimeStampFrom,
                                           DateTime TimeStampTo)>();

                var pilesForDB = new List<(string ForecastHash,
                                           string[] DockingHashes,
                                           string Name,
                                           bool ActiveStatus,
                                           double TriggeringTension,
                                           DateTime TimeStampFrom,
                                           DateTime TimeStampTo)>();


                foreach (var weatherRecord in weatherForecast)
                {
                    // Define the scenario window
                    var ws = weatherRecord.DateTimeForecast;
                    var we = weatherRecord.DateTimeForecast.AddHours(weatherRecord.RecordIntervalHours);

                    // -------------------------------
                    // CHECK SCENARIO FOR GRIDS
                    // -------------------------------
                    bool gridScenarioExists =
                        await SnapToolDB.ScenarioExistsAsync(conn, tx, Tables.ActivationsGrids, ws, we, berths);

                    if (!gridScenarioExists)
                    {
                        await SnapToolDB.ClearScenarioAsync(conn, tx, Tables.ActivationsGrids, ws, we, berths);

                        var (pileDolphinActivations, gridActivations) =
                            GetActiveElements([weatherRecord], dockingArrangement);

                        foreach (var grid in gridActivations)
                        {
                            gridsForDB.Add((
                                weatherRecord.RecordHash,
                                dockingArrangement.Select(x => x.RecordHash).ToArray(),
                                grid.CombinedLocation!,
                                grid.IsActive,
                                grid.TriggeringTension ?? -1d,
                                ws,
                                we
                            ));
                        }

                        // -------------------------------
                        // CHECK SCENARIO FOR PD
                        // -------------------------------
                        bool pdScenarioExists =
                            await SnapToolDB.ScenarioExistsAsync(conn, tx, Tables.ActivationsPD, ws, we, berths);

                        if (!pdScenarioExists)
                        {
                            await SnapToolDB.ClearScenarioAsync(conn, tx, Tables.ActivationsPD, ws, we, berths);

                            foreach (var pd in pileDolphinActivations)
                            {
                                pilesForDB.Add((
                                    weatherRecord.RecordHash,
                                    dockingArrangement.Select(x => x.RecordHash).ToArray(),
                                    pd.CombinedLocation!,
                                    pd.IsActive,
                                    pd.TriggeringTension ?? -1d,
                                    ws,
                                    we
                                ));
                            }
                        }
                    }
                }

                // --------------------------------------------------
                // BULK INSERTS (grid + PD)
                // --------------------------------------------------
                if (gridsForDB.Count > 0)
                    await SnapToolDB.BulkInsertGridActivationsAsync(conn, tx, gridsForDB);

                if (pilesForDB.Count > 0)
                    await SnapToolDB.BulkInsertPdActivationsAsync(conn, tx, pilesForDB);

                // --------------------------------------------------
                // COMMIT
                // --------------------------------------------------
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }
    }
    }
    
