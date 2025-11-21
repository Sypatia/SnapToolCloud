using SnapToolCloud.Data;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace SnapToolCloud.Service
{
    public class WeatherService
    {
        private static readonly string BaseUrl = "https://datafeeds.rpsmetocean.com:8443/datafeeds-web/services/feeds/secured";
        private static readonly string ListEndpoint = $"{BaseUrl}/list.json";
        private static double msToKnots = 1.943845249222;

        private static List<WeatherRecord> ParseMetoceanForecast(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var dataArray = root.GetProperty("data").EnumerateArray().ToList();
            var records = new List<WeatherRecord>();

            // Extract record interval in seconds and convert to hours
            double recordIntervalHours = 0;
            if (root.TryGetProperty("attributes", out var attrs) &&
                attrs.TryGetProperty("expectedRecordInterval", out var interval) &&
                interval.TryGetProperty("value", out var val) &&
                val.ValueKind == JsonValueKind.Number)
            {
                recordIntervalHours = val.GetDouble() / 3600.0; // convert seconds → hours
            }

            // Parse dfLastModified as Perth local time
            DateTime? dfLastModifiedLocal = null;

            if (root.TryGetProperty("attributes", out attrs) &&
                attrs.TryGetProperty("dfLastModified", out var lastMod) &&
                lastMod.TryGetProperty("value", out var lmVal) &&
                lmVal.ValueKind == JsonValueKind.String)
            {
                if (DateTimeOffset.TryParse(lmVal.GetString(), out var dto))
                    dfLastModifiedLocal = dto.ToOffset(TimeSpan.FromHours(8)).DateTime;
            }

            foreach (var item in dataArray)
            {
                var time = item.GetProperty("time").GetString();
                var vars = item.GetProperty("variables").EnumerateArray().ToList();

                // Convert forecast timestamp to Perth local time
                var dtoTime = DateTimeOffset.Parse(time);
                var forecastLocal = dtoTime.ToOffset(TimeSpan.FromHours(8)).DateTime;

                var record = new WeatherRecord
                {
                    DateTimeForecast = forecastLocal,
                    LastModifiedApi = (DateTime)dfLastModifiedLocal,
                    WindGustSpeed = vars[0].ValueKind == JsonValueKind.Number ? vars[0].GetDouble() * msToKnots : double.NaN,
                    TotalWaveMax = vars[1].ValueKind == JsonValueKind.Number ? Math.Max(0, vars[5].GetDouble()) : double.NaN,
                    TotalWaveSig = vars[2].ValueKind == JsonValueKind.Number ? vars[1].GetDouble() : double.NaN,
                    SeaHeight = vars[3].ValueKind == JsonValueKind.Number ? vars[2].GetDouble() : double.NaN,
                    SwellHeight1 = vars[4].ValueKind == JsonValueKind.Number ? Math.Max(0, vars[3].GetDouble()) : double.NaN,
                    SwellHeight2 = vars[5].ValueKind == JsonValueKind.Number ? Math.Max(0, vars[4].GetDouble()) : double.NaN,
                    SwellDirection1 = vars[6].ValueKind == JsonValueKind.Number ? CardinalService.ConvertToCompass(vars[6].GetDouble()) : null,
                    SwellDirection2 = vars[7].ValueKind == JsonValueKind.Number ? CardinalService.ConvertToCompass(vars[7].GetDouble()) : null,
                    SwellPeriod1 = vars[9].ValueKind == JsonValueKind.Number ? Math.Max(0, vars[9].GetDouble()) : double.NaN,
                    SwellPeriod2 = vars[10].ValueKind == JsonValueKind.Number ? Math.Max(0, vars[10].GetDouble()) : double.NaN,
                    WindDirection = vars[11].ValueKind == JsonValueKind.Number ? CardinalService.ConvertToCompass(vars[11].GetDouble()) : null,
                    WindSpeed = vars[12].ValueKind == JsonValueKind.Number ? vars[12].GetDouble() * msToKnots : double.NaN,
                    Wind50Speed = vars[13].ValueKind == JsonValueKind.Number ? vars[13].GetDouble() * msToKnots : double.NaN,

                    WeatherDescription = "Forecast",
                    ForecastConfidence = "High",
                    RecordIntervalHours = recordIntervalHours
                };

                if (record.WindDirection == "N/A")
                    continue;

                records.Add(record);
            }

            return records;
        }


        public static async Task<List<WeatherRecord>> GetRTIOB10ForecastAsync(
    DateTime? from = null,
    DateTime? to = null,
    int tz = 8,
    string feedID = "RTIOB10EOWSForecast-fs")
        {
            string environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";

            // ---- Load certificate ----
            string thumbprint = Globals.Config["MetoceanWeatherService:CertThumbprint"]
                ?? throw new InvalidOperationException("MetoceanWeatherService:CertThumbprint missing.");

            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly);

            var certificate = store.Certificates
                .Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false)
                .OfType<X509Certificate2>()
                .FirstOrDefault()
                ?? throw new InvalidOperationException($"Certificate {thumbprint} not found.");

            // ---- HTTP client ----
            var handler = new HttpClientHandler
            {
                SslProtocols = System.Security.Authentication.SslProtocols.Tls12
            };
            handler.ClientCertificates.Add(certificate);
            handler.ServerCertificateCustomValidationCallback = (msg, cert, chain, errs) => true;

            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(120) };

            // ---- Build query ----
            string query;
            if (from == null && to == null)
            {
                string today = DateTime.Today.ToString("yyyyMMdd0000");
                query = $"from={today}&tz={tz}";
            }
            else if (to == null)
            {
                query = $"from={((DateTime)from):yyyyMMddHHmm}&tz={tz}";
            }
            else if (from == null)
            {
                query = $"to={((DateTime)to):yyyyMMddHHmm}&tz={tz}";
            }
            else
            {
                query = $"from={((DateTime)from):yyyyMMddHHmm}&to={((DateTime)to):yyyyMMddHHmm}&tz={tz}";
            }

            string url = $"https://datafeeds.rpsmetocean.com:8443/datafeeds-web/services/feeds/secured/timeseries.v2/{feedID}.json?{query}";
            Console.WriteLine($"➡️ Requesting: {url}");

            // ---- Fetch + Parse only ----
            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync();
            return ParseMetoceanForecast(json);
        }
        public static async Task GetLambertWaveDataAsync()
        {
            string environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
            X509Certificate2 certificate;

            // Load certificate
            string thumbprint = Globals.Config["MetoceanWeatherService:CertThumbprint"]
                ?? throw new InvalidOperationException("MetoceanWeatherService:CertThumbprint missing in configuration.");

            using (var store = new X509Store(StoreName.My, StoreLocation.CurrentUser))
            {
                store.Open(OpenFlags.ReadOnly);
                certificate = store.Certificates
                    .Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false)
                    .OfType<X509Certificate2>()
                    .FirstOrDefault()
                    ?? throw new InvalidOperationException($"Certificate with thumbprint {thumbprint} not found.");
            }

            var handler = new HttpClientHandler
            {
                SslProtocols = System.Security.Authentication.SslProtocols.Tls12
            };
            handler.ClientCertificates.Add(certificate);

            // Optional diagnostics — accept invalid server certs only for debugging
            handler.ServerCertificateCustomValidationCallback = (msg, cert, chain, errs) =>
            {
                Console.WriteLine($"SSL Policy Errors: {errs}");
                return true;
            };

            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(120) };

            // 🔹 Lambert B28 Waves (last month)
            string url = "https://datafeeds.rpsmetocean.com:8443/datafeeds-web/services/feeds/secured/timeseries.v2/DampierB1WindsRems-ws.json?from=202510010000&to=202511050000&tz=8";

            Console.WriteLine($"➡️ Requesting: {url}");

            try
            {
                var response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();

                string json = await response.Content.ReadAsStringAsync();
                Console.WriteLine("✅ Response received.");

                using var doc = JsonDocument.Parse(json);

                // Navigate to the data array inside HashMap → data or similar
                JsonElement? dataArray = null;
                if (doc.RootElement.TryGetProperty("HashMap", out var hashMap))
                {
                    foreach (var prop in hashMap.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.Array)
                        {
                            dataArray = prop.Value;
                            break;
                        }
                    }
                }

                if (dataArray.HasValue && dataArray.Value.ValueKind == JsonValueKind.Array)
                {
                    var times = dataArray.Value.EnumerateArray()
                        .Where(e => e.TryGetProperty("Time", out _))
                        .Select(e => DateTime.Parse(e.GetProperty("Time").GetString()!))
                        .ToList();

                    if (times.Any())
                    {
                        Console.WriteLine($"Earliest record : {times.Min():u}");
                        Console.WriteLine($"Latest record   : {times.Max():u}");
                    }
                    else
                    {
                        Console.WriteLine("No time data found in the response.");
                    }
                }
                else
                {
                    Console.WriteLine("⚠️ Could not locate a data array in the response structure.");
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error retrieving Lambert data: {ex.Message}");
                Console.WriteLine($"Inner: {ex.InnerException?.Message}");
            }
        }

    }
}
