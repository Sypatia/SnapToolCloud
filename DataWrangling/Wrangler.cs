using CsvHelper;
using CsvHelper.Configuration;
using SixLabors.ImageSharp;
using SnapToolCloud.Properties;
using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
namespace SnapToolCloud.Data
{
    public static class TimeZoneParser
    {
        public static DateTime AsPerthLocal(DateTimeOffset dto)
        {
            return dto.ToOffset(TimeSpan.FromHours(8)).DateTime;
        }
        public static DateTime AsPerthLocal(DateTime dt)
        {
            // If coming as unspecified OR local, treat as Perth-local
            return DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);
        }
    }

    public static class DictionaryExtensions
    {
        public static SortedDictionary<TKey, TValue> ToSortedDictionary<TKey, TValue>(this IEnumerable<KeyValuePair<TKey, TValue>> source)
            where TKey : IComparable<TKey>
        {
            var sortedDict = new SortedDictionary<TKey, TValue>();
            foreach (var kvp in source)
            {
                sortedDict.Add(kvp.Key, kvp.Value);
            }
            return sortedDict;
        }
    }
    public class CardinalService
    {

        // Helper to calculate direction range (+/- 22.5 degrees from the cardinal direction)
        public static List<string> GetWindDirectionRange(string direction)
        {
            if (!CompassToDegrees.ContainsKey(direction))
                throw new ArgumentException($"Invalid wind direction: {direction}");

            double baseDegrees = CompassToDegrees[direction];

            // Calculate ±22.5° range, wrapping around 0-360°
            double minDegrees = (baseDegrees - 22.5 + 360.0) % 360.0;
            double maxDegrees = (baseDegrees + 22.5) % 360.0;

            var result = new List<string>();

            // Iterate through compass directions in the ±22.5° range
            foreach (var entry in DegreesToCompass)
            {
                double degrees = entry.Key;
                if (IsInRange(degrees, minDegrees, maxDegrees))
                {
                    result.Add(entry.Value);
                }
            }

            return result;
        }

        public static readonly Dictionary<string, double> CompassToDegrees = new Dictionary<string, double>
    {
    { "N", 0.0 },
    { "NNE", 22.5 },
    { "NE", 45.0 },
    { "ENE", 67.5 },
    { "E", 90.0 },
    { "ESE", 112.5 },
    { "SE", 135.0 },
    { "SSE", 157.5 },
    { "S", 180.0 },
    { "SSW", 202.5 },
    { "SW", 225.0 },
    { "WSW", 247.5 },
    { "W", 270.0 },
    { "WNW", 292.5 },
    { "NW", 315.0 },
    { "NNW", 337.5 },
};

        public static readonly SortedDictionary<double, string> DegreesToCompass = CompassToDegrees
            .ToDictionary(kv => kv.Value, kv => kv.Key)
            .OrderBy(kv => kv.Key)
            .ToSortedDictionary(); // Ensure it is sorted

        // Check if a degree is within the range (handling wrap-around)
        public static bool IsInRange(double degrees, double min, double max)
        {
            min = (min + 360) % 360;
            max = (max + 360) % 360;
            if (min < max)
            {
                return degrees >= min && degrees <= max;
            }
            else
            {
                return degrees >= min || degrees <= max;
            }
        }

        // Helper method to convert cardinal direction (e.g., N, NW) to an angle using the CompassToDegrees dictionary
        public static double? GetCardinalDirectionAngle(string direction)
        {
            if (string.IsNullOrEmpty(direction))
                return null;

            if (CompassToDegrees.TryGetValue(direction.ToUpper(), out double angle))
                return angle;

            return null; // Return null if the direction is not found in the dictionary
        }

        // Helper method to check if a given angle falls within a direction range (±5 degrees tolerance)
        public static bool IsInDirectionRange(double direction, string conditionDirection)
        {
            if (string.IsNullOrEmpty(conditionDirection))
                return false;

            double conditionAngle = double.Parse(conditionDirection);
            return direction >= (conditionAngle - 5) && direction <= (conditionAngle + 5);
        }


        public static string ConvertToCompass(double dirClockwiseFromNorth)
        {
            double degrees = dirClockwiseFromNorth;
            if (degrees < 0 || degrees > 360)
                return "N/A";

            // Find closest match in DegreesToCompass
            var closest = DegreesToCompass
                .OrderBy(kv => Math.Abs(kv.Key - degrees))
                .FirstOrDefault();

            return closest.Value ?? "N/A";
        }
    }

    public static class Globals
    {
        public static double TensionThreshold = 200;

        private static IConfigurationRoot _config;
        public static IConfigurationRoot Config
        {
            get
            {
                if (_config == null)
                {
                    // Determine environment (e.g., "Development", "Production")
                    var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";

                    _config = new ConfigurationBuilder()
                        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                        .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: true)
                        .AddEnvironmentVariables()
                        .Build();
                }

                return _config;
            }
        }
    }

    public record WeatherRecord
    {
        public DateTime DateTimeForecast;
        public DateTime? LastModifiedApi;
        public string WindDirection;
        public double WindSpeed; // KNOTS
        public double WindGustSpeed; // KNOTS
        public double Wind50Speed; // KNOTS
        public double SeaHeight; // Metres
        public string? SwellDirection1;
        public double? SwellPeriod1; // Seconds
        public double? SwellHeight1; // Metres
        public string? SwellDirection2;
        public double? SwellPeriod2; // Seconds
        public double? SwellHeight2; // Metres
        public double TotalWaveSig;
        public double TotalWaveMax;
        public string WeatherDescription;
        public string ForecastConfidence;
        public double RecordIntervalHours;

        [JsonIgnore]
        public string RecordHash => ComputeHash(this);

        [JsonIgnore]
        public bool IsLatest { get; set; }

        private static string ComputeHash(WeatherRecord record)
        {
            var clone = new
            {
                record.DateTimeForecast,
                record.LastModifiedApi,
                record.WindDirection,
                record.WindSpeed,
                record.WindGustSpeed,
                record.Wind50Speed,
                record.SeaHeight,
                record.SwellDirection1,
                record.SwellPeriod1,
                record.SwellHeight1,
                record.SwellDirection2,
                record.SwellPeriod2,
                record.SwellHeight2,
                record.TotalWaveSig,
                record.TotalWaveMax,
                record.WeatherDescription,
                record.ForecastConfidence,
                record.RecordIntervalHours
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = false,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            string json = JsonSerializer.Serialize(clone, options);
            using var sha = SHA256.Create();
            byte[] hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(json));
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }

        public override string ToString() => $"WeatherRecord({DateTimeForecast:u})";
    }

    public record PileDolphinLocationRecord
    {
        public int Page { get; set; }
        public string Name { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
    }

    public record GridLocationRecord
    {
        public int Page { get; set; }
        public string Name { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
    }
    public record DMARecord
    {
        public string WindSpeed { get; set; }
        public string WindDirection { get; set; }
        public string WaveCondition { get; set; }
        public string Tension { get; set; }
        public string Vessel { get; set; }
        public string Berth { get; set; }
        public string MC { get; set; }
        public string ML { get; set; }
        public DateTime? WeatherDateTime { get; set; }

        public string? Cause { get; set; }
    }

    public record GridRecord
    {
        public string Berth { get; set; }
        public string Vessel { get; set; }
        public string MC { get; set; }
        public string ML { get; set; }
        public bool InEnvelope { get; set; }

        public bool IsActive => InEnvelope && TriggeringTension >= Globals.TensionThreshold;
        public string Location { get; set; }
        public string Number { get; set; }
        public string CombinedLocation => Number + Location;
        public double? TriggeringTension { get; set; }  // Populated during filtering based on DMARecord data
        public DateTime? WeatherDateTime { get; set; }
        public string? Cause { get; set; }

        public bool IsLatest { get; set; }
    }
    public record PileDolphinRecord
    {
        public string Berth { get; set; }
        public string Vessel { get; set; }
        public string MC { get; set; }
        public string ML { get; set; }
        public bool InEnvelope { get; set; }
        public bool IsActive => InEnvelope && TriggeringTension >= Globals.TensionThreshold;
        public string Type { get; set; }
        public string Location { get; set; }
        public string Number { get; set; }
        public double? TriggeringTension { get; set; }  // Populated during filtering based on DMARecord data
        public string? CombinedLocation => Number + Location;
        public DateTime? WeatherDateTime { get; set; }
        public string? Cause { get; set; }

        public bool IsLatest { get; set; }
    }

    public record MooringRecord
    {
        public string Berth { get; set; }
        public string Vessel { get; set; }
        public string MC { get; set; }
        public string Bow { get; set; }
        public string FwdSpring { get; set; }
        public string AftSpring { get; set; }
        public string Stern { get; set; }
    }

    public record WaveConditionRecord
    {
        public double WindSpeed { get; set; }
        public string WindDirection { get; set; }
        public double WaveHeight1 { get; set; }
        public double WavePeriod1 { get; set; }
        public double WaveDirection1 { get; set; }
        public double? WaveHeight2 { get; set; }
        public double? WavePeriod2 { get; set; }
        public string? WaveDirection2 { get; set; }
        public string? Condition { get; set; }
    }

    public record DockingArrangementRecord
    {
        public string Berth { get; set; }
        public string Vessel { get; set; }
        public string MC { get; set; }
        public string VesselName { get; set; }
        public string EventType { get; set; }
        public DateTime AllSecureDateTime { get; set; }
        public DateTime SailDateTime { get; set; }
        public DateTime? LastModifiedApi { get; set; }

        [JsonIgnore]
        public bool IsLatest { get; set; }

        [JsonIgnore]
        public string RecordHash => ComputeHash(this);

        private static string ComputeHash(DockingArrangementRecord record)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = false,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault
            };

            // manually remove RecordHash recursion
            var clone = new
            {
                record.Berth,
                record.Vessel,
                record.MC,
                record.VesselName,
                record.EventType,
                record.AllSecureDateTime,
                record.SailDateTime,
                record.LastModifiedApi
            };

            string json = JsonSerializer.Serialize(clone, options);
            using var sha = SHA256.Create();
            byte[] hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(json));
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }

        public override string ToString() =>
    $"DockingArrangementRecord(Vessel={Vessel}, Berth={Berth})";

    }
    public static class CsvDataLoader
    {
        public static List<T> ParseRecords<T>(string csvContent) where T : class
        {
            if (string.IsNullOrEmpty(csvContent))
            {
                throw new InvalidOperationException("The CSV content is empty or missing.");
            }

            var records = new List<T>();

            using (var reader = new StringReader(csvContent))
            {
                var config = new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    HasHeaderRecord = true,
                    MissingFieldFound = null,
                    HeaderValidated = null
                };

                using (var csv = new CsvReader(reader, config))
                {
                    records = csv.GetRecords<T>().ToList();
                }
            }

            return records;
        }
    }

    public static class DolphinMooringLoader
    {
        private static readonly Lazy<List<MooringRecord>> _records = new Lazy<List<MooringRecord>>(() =>
        {
            string csvContent = Encoding.UTF8.GetString(Resources.MooringData);
            return CsvDataLoader.ParseRecords<MooringRecord>(csvContent);
        });

        public static List<MooringRecord> ParseRecords() => _records.Value;
    }
    public static class WaveDataLoader
    {
        private static readonly Lazy<List<WaveConditionRecord>> _records = new Lazy<List<WaveConditionRecord>>(() =>
        {
            string csvContent = Encoding.UTF8.GetString(Resources.WaveConditionData);
            return CsvDataLoader.ParseRecords<WaveConditionRecord>(csvContent);
        });

        public static List<WaveConditionRecord> ParseRecords() => _records.Value;
    }

    public static class PileDolphinRecordLoader
    {
        private static readonly Lazy<List<PileDolphinRecord>> _records = new Lazy<List<PileDolphinRecord>>(() =>
        {
            string csvContent = Encoding.UTF8.GetString(Resources.PileDolphinData);
            return CsvDataLoader.ParseRecords<PileDolphinRecord>(csvContent);
        });

        public static List<PileDolphinRecord> ParseRecords() => _records.Value;
    }
    public static class DataFilter
    {
        public static List<T> FilterActiveRecords<T>(
    List<DMARecord> activeValues,
    Func<List<T>> recordLoader)
    where T : class, new()
        {
            var allRecords = recordLoader();
            var filtered = new List<T>();
            var type = typeof(T);

            foreach (var record in allRecords)
            {
                var berth = type.GetProperty("Berth")?.GetValue(record)?.ToString();
                var vessel = type.GetProperty("Vessel")?.GetValue(record)?.ToString();
                var mc = type.GetProperty("MC")?.GetValue(record)?.ToString();
                var ml = type.GetProperty("ML")?.GetValue(record)?.ToString();

                double maxTension = double.MinValue;
                DMARecord bestActive = null;

                foreach (var active in activeValues)
                {
                    if (active.Berth == berth &&
                        active.Vessel == vessel &&
                        active.MC == mc &&
                        active.ML == ml)
                    {
                        if (double.TryParse(active.Tension, out var tension) &&
                            tension > maxTension)
                        {
                            maxTension = tension;
                            bestActive = active;
                        }
                    }
                }

                // No match → skip
                if (bestActive == null)
                    continue;

                // ----------------------------------------------------
                // Populate the T record output
                // ----------------------------------------------------
                type.GetProperty("TriggeringTension")?.SetValue(record, maxTension);
                type.GetProperty("WeatherDateTime")?.SetValue(record, bestActive.WeatherDateTime);
                type.GetProperty("Cause")?.SetValue(record, bestActive.Cause);


                // Force InEnvelope = true/false/1/0
                var activeProp = type.GetProperty("InEnvelope");
                if (activeProp != null)
                {
                    var raw = activeProp.GetValue(record)?.ToString()?.Trim()?.ToLowerInvariant();

                    bool newActive =
                        raw == "false" || raw == "0" || string.IsNullOrWhiteSpace(raw)
                        ? false
                        : true;

                    if (activeProp.PropertyType == typeof(bool))
                        activeProp.SetValue(record, newActive);
                    else if (activeProp.PropertyType == typeof(int))
                        activeProp.SetValue(record, newActive ? 1 : 0);
                    else
                        activeProp.SetValue(record, newActive ? "1" : "0");
                }

                filtered.Add(record);   // <-- RETURN T
            }

            return filtered;
        }

        public static List<DMARecord> FilterDMADataWaveOnly(
    DockingArrangementRecord dockingArrangement,
    DateTime targetDateTime,
    List<WeatherRecord> weatherForecast,
    bool filterTension = true)
        {
            List<DMARecord> dmaRecords = DmaDataLoader.ParseRecords();


            // Parse and find matching weather data for the given date and time
            var selectedWeatherData = weatherForecast.FirstOrDefault(x => x.DateTimeForecast == targetDateTime);

            if (selectedWeatherData == null)
                throw new Exception("Issue parsing weather data. Cannot find matching weather data for given date and time.");


            // Load wave condition data
            var waveConditionRecords = WaveDataLoader.ParseRecords();

            // Convert cardinal direction (e.g., N, NW, NNW) to angle range
            double? waveDirection1Angle = CardinalService.GetCardinalDirectionAngle(selectedWeatherData.SwellDirection1);
            double? waveDirection2Angle = CardinalService.GetCardinalDirectionAngle(selectedWeatherData.SwellDirection2);

            // Find the corresponding condition in wave condition data
            var conditionToCauseMap = waveConditionRecords.Select(condition =>
            {
                bool wave1Matches = false;
                bool wave2Matches = false;
                string cause = null;

                // Check for wave 1 conditions
                if (waveDirection1Angle != null)
                {

                    // Checks NW/NE quadrants for wave 1
                    // Checks if the wave period is greater than or equal to the condition's wave period
                    if (CardinalService.IsInRange((double)waveDirection1Angle, -22.5, 22.5) && selectedWeatherData.SwellPeriod1 >= condition.WavePeriod1)
                    {
                        wave1Matches = true;
                        cause = $"Wave 1 - Condition #{condition.Condition}";
                    }

                }

                // Check for wave 2 conditions
                if ((waveDirection2Angle != null) && (condition.WaveDirection2 != null))
                {

                    // Checks NW/NE quadrants for wave 2
                    // Checks if the wave period is greater than or equal to the condition's wave period
                    if (CardinalService.IsInRange((double)waveDirection2Angle, -45, 45) && selectedWeatherData.SwellPeriod2 >= condition.WavePeriod2)
                    {
                        wave2Matches = true;
                        cause = $"Wave 2 - Condition #{condition.Condition}";
                    }
                }

                return (condition, cause, isMatch: wave1Matches || wave2Matches);
            }).Where(entry => entry.isMatch)  // Filter only matched conditions
            .ToDictionary(entry => entry.condition.Condition, entry => entry.cause);

            List<DMARecord> allMatchingRecords = new List<DMARecord>();


            allMatchingRecords = dmaRecords
                .Where(record => conditionToCauseMap.ContainsKey(record.WaveCondition) &&
                                    record.Berth == dockingArrangement.Berth &&
                                    record.Vessel == dockingArrangement.Vessel &&
                                    record.MC == dockingArrangement.MC)
                .Select(record =>
                {
                    record.WeatherDateTime = targetDateTime;
                    record.Cause = conditionToCauseMap[record.WaveCondition]; // Use precomputed cause
                    return record;
                })
                .ToList();


            // Return the aggregated list of all matching records
            return allMatchingRecords;
        }
        

        // Finds DMA records 
        public static List<DMARecord> FilterDMADataWindOnly(
DockingArrangementRecord dockingArrangement,
DateTime targetDateTime,
List<WeatherRecord> weatherForecast,
bool filterTension = true)
        {
            List<DMARecord> dmaRecords = DmaDataLoader.ParseRecords();


            // Parse and find matching weather data for the given date and time
            var selectedWeatherData = weatherForecast.FirstOrDefault(row =>
                    row.DateTimeForecast == targetDateTime);

            if (selectedWeatherData == null)
            {
                throw new Exception("Issue parsing weather data. Cannot find matching weather data for given date and time.");
            }

            // Filter DMA records and set WeatherForecastDateAndTime
            if (filterTension)
            {
                return dmaRecords
                    .Where(record =>
                    {
                        if (!double.TryParse(record.WindSpeed.Trim(), out double windSpeed))
                        {
                            throw new TypeLoadException("Issue parsing wind speed. Cannot convert string to double.");
                        }

                        if (!double.TryParse(record.Tension.Trim(), out double tension))
                        {
                            throw new TypeLoadException("Issue parsing tension. Cannot convert string to double.");
                        }

                        // Extract relevant weather data
                        string windDirection = selectedWeatherData.WindDirection;
                        double windSpd = selectedWeatherData.WindSpeed;

                        // Convert wind direction to range (+/- 15 degrees)
                        var directionRange = CardinalService.GetWindDirectionRange(windDirection);
                        bool matches;

                        if (windSpd < 17)
                        {
                            matches = record.Berth == dockingArrangement.Berth &&
                                        record.MC == dockingArrangement.MC &&
                                        record.Vessel == dockingArrangement.Vessel &&
                                        record.WindSpeed == "20.0" &&
                                        directionRange.Contains(record.WindDirection) &&
                                        record.WaveCondition == "";
                            if (matches)
                                record.Cause = "Wind <17";
                        }
                        else if (windSpd < 22)
                        {
                            matches = record.Berth == dockingArrangement.Berth &&
                                        record.MC == dockingArrangement.MC &&
                                        record.Vessel == dockingArrangement.Vessel &&
                                        record.WindSpeed == "30.0" &&
                                        directionRange.Contains(record.WindDirection) &&
                                        record.WaveCondition == "";
                            if (matches)
                                record.Cause = "Wind <22";
                        }
                        else if (windSpd < 28)
                        {
                            matches = record.Berth == dockingArrangement.Berth &&
                                        record.MC == dockingArrangement.MC &&
                                        record.Vessel == dockingArrangement.Vessel &&
                                        record.WindSpeed == "50.0" &&
                                        directionRange.Contains(record.WindDirection) &&
                                        record.WaveCondition == "";
                            if (matches)
                                record.Cause = "Wind <28";
                        }
                        else
                        {
                            // Include all mooring lines for given Berth and MC, regardless of speed, direction or tension force
                            matches = record.Berth == dockingArrangement.Berth &&
                                        record.MC == dockingArrangement.MC &&
                                        record.Vessel == dockingArrangement.Vessel &&
                                        record.WaveCondition == "";
                            if (matches)
                                record.Cause = "Wind >=28";
                        }

                        if (matches)
                            record.WeatherDateTime = targetDateTime;


                        return matches;
                    })
                    .ToList();
            }
            else
            {
                return dmaRecords
                    .Where(record =>
                    {
                        if (!double.TryParse(record.WindSpeed.Trim(), out double windSpeed))
                        {
                            throw new TypeLoadException("Issue parsing wind speed. Cannot convert string to double.");
                        }

                        if (!double.TryParse(record.Tension.Trim(), out double tension))
                        {
                            throw new TypeLoadException("Issue parsing tension. Cannot convert string to double.");
                        }

                        // Extract relevant weather data
                        string windDirection = selectedWeatherData.WindDirection;
                        double windSpd = selectedWeatherData.WindGustSpeed;

                        // Convert wind direction to range (+/- 15 degrees)
                        var directionRange = CardinalService.GetWindDirectionRange(windDirection);
                        bool matches;

                        if (windSpd > 20)
                        {
                            // Include all mooring lines for given Berth and MC, regardless of speed, direction or tension force
                            matches = record.Berth == dockingArrangement.Berth &&
                            record.Vessel == dockingArrangement.Vessel &&
                                        record.MC == dockingArrangement.MC &&
                                        record.WaveCondition == "";
                            if (matches)
                                record.Cause = "Wind >20";
                        }
                        else
                        {
                            windSpd = 30;
                            matches = record.Berth == dockingArrangement.Berth &&
                            record.Vessel == dockingArrangement.Vessel &&
                                        record.MC == dockingArrangement.MC &&
                                        directionRange.Contains(record.WindDirection) &&
                                        windSpeed >= windSpd &&
                                        record.WaveCondition == "";
                            if (matches)
                                record.Cause = "Wind";
                        }

                        if (matches)
                            record.WeatherDateTime = targetDateTime;


                        return matches;
                    })
                    .ToList();
            }


        }
    }

    public static class DmaDataLoader
    {
        private static readonly Lazy<List<DMARecord>> _records = new Lazy<List<DMARecord>>(() =>
        {
            string csvContent = Encoding.UTF8.GetString(Resources.DmaData);

            // Use the generic ParseRecords method
            return CsvDataLoader.ParseRecords<DMARecord>(csvContent);
        });

        public static List<DMARecord> ParseRecords() => _records.Value;


    }

    public class PileDolphinDataLoader
    {

        private static readonly Lazy<List<PileDolphinRecord>> _records = new Lazy<List<PileDolphinRecord>>(() =>
        {
            string csvContent = Encoding.UTF8.GetString(Resources.PileDolphinData);

            if (string.IsNullOrEmpty(csvContent))
            {
                throw new InvalidOperationException("The embedded PileDolphinData resource is empty or missing.");
            }

            return CsvDataLoader.ParseRecords<PileDolphinRecord>(csvContent);
        });

        public static List<PileDolphinRecord> ParseRecords() => _records.Value;
    }

    public class GridDataLoader
    {
        private static readonly Lazy<List<GridRecord>> _records = new Lazy<List<GridRecord>>(() =>
        {
            string csvContent = Encoding.UTF8.GetString(Resources.GridData);

            if (string.IsNullOrEmpty(csvContent))
            {
                throw new InvalidOperationException("The embedded PileDolphinData resource is empty or missing.");
            }

            return CsvDataLoader.ParseRecords<GridRecord>(csvContent);
        });

        public static List<GridRecord> ParseRecords() => _records.Value;
    }

    public class Wrangler
    {
        public static DataTable FormatAndLoadWeatherData(DataTable inputTable)
        {
            if (inputTable == null || inputTable.Rows.Count == 0)
            {
                throw new ArgumentException("Input table is null or empty.");
            }

            DataTable formattedTable = new DataTable();

            // Define the columns in the new DataTable
            formattedTable.Columns.Add("Date", typeof(string));
            formattedTable.Columns.Add("Time", typeof(string));
            formattedTable.Columns.Add("WindDirection", typeof(string));
            formattedTable.Columns.Add("WindGust", typeof(string));
            formattedTable.Columns.Add("WaveDirection1", typeof(string));
            formattedTable.Columns.Add("WavePeriod1", typeof(string));
            formattedTable.Columns.Add("WaveDirection2", typeof(string));
            formattedTable.Columns.Add("WavePeriod2", typeof(string));
            formattedTable.Columns.Add("Confidence", typeof(string));


            // Define the expected column names
            string[] expectedColumns = new string[]
            {
                "Date/Time WST",
                "Wind (from / knots) Dir",
                "Wind (from / knots) Spd",
                "Wind (from / knots) Gust",
                "Wind (from / knots) 50m",
                "Sea Ht",
                "Swell 1 Dir",
                "Swell 1 Per",
                "Swell 1 Ht",
                "Swell 2 Dir",
                "Swell 2 Per",
                "Swell 2 Ht",
                "Total Wave Sig",
                "Total Wave Max",
                "Weather",
                "Confidence"
            };

            // Verify column names
            for (int i = 0; i < expectedColumns.Length; i++)
            {
                if (i >= inputTable.Columns.Count || inputTable.Columns[i].ColumnName != expectedColumns[i])
                {
                    throw new ArgumentException($"Column mismatch. Expected: {expectedColumns[i]}, Found: {inputTable.Columns[i]?.ColumnName ?? "null"}");
                }
            }

            foreach (DataRow row in inputTable.Rows)
            {
                // Parse the Date/Time column
                string dateTimeRaw = row[0]?.ToString()?.Trim(); // "Date/Time WST" column

                if (string.IsNullOrWhiteSpace(dateTimeRaw))
                    throw new InvalidOperationException("Date/Time column contains empty or invalid data.");

                // Convert ISO 8601 format to the format "Wed 27-Nov"
                DateTime dateTime;
                if (!DateTime.TryParse(dateTimeRaw, null, DateTimeStyles.RoundtripKind, out dateTime))
                    throw new FormatException($"Unable to parse Date/Time: {dateTimeRaw}");

                string formattedDate = dateTime.ToString("ddd dd-MMM", CultureInfo.InvariantCulture);
                string formattedTime = dateTime.ToString("HH:mm");

                string windDirection = row[1]?.ToString()?.Trim(); // Wind direction
                string windGust = row[3]?.ToString()?.Trim();      // Wind gust

                string waveDirection1 = row[6]?.ToString()?.Trim(); // Wave 1 direction
                string wavePeriod1 = row[7]?.ToString()?.Trim();    // Wave 1 period

                string waveDirection2 = row[9]?.ToString()?.Trim(); // Wave 2 direction
                string wavePeriod2 = row[10]?.ToString()?.Trim();    // Wave 2 period

                string confidence = row[15]?.ToString()?.Trim();    // Confidence

                // Add a new row to the formatted DataTable
                formattedTable.Rows.Add(formattedDate, formattedTime, windDirection, windGust, waveDirection1, wavePeriod1, waveDirection2, wavePeriod2, confidence);
            }

            if (formattedTable.Rows.Count == 0)
                throw new InvalidOperationException("Formatted table is empty after processing.");

            return formattedTable;
        }
    }
}
