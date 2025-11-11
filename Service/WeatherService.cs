using SnapToolCloud.Data;
using SnapToolCloud.Database;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Net.Security;

namespace SnapToolCloud.Service
{
    public class WeatherService
    {
        private static readonly string BaseUrl = "https://datafeeds.rpsmetocean.com:8443/datafeeds-web/services/feeds/secured";
        private static readonly string ListEndpoint = $"{BaseUrl}/list.json";

        public static async Task<List<WeatherRecord>> GetRTIOB10ForecastAsync(DateTime? from = null, DateTime? to = null, int tz = 8, string feedID = "RTIOB10EOWSForecast-fs")
        {
            string environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
            X509Certificate2 certificate;

            double msToKnots = 1.943845249222;
            string thumbprint = Globals.Config["WeatherService:CertThumbprint"]
                ?? throw new InvalidOperationException("WeatherService:CertThumbprint missing in configuration.");

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
            handler.ServerCertificateCustomValidationCallback = (msg, cert, chain, errs) => true;

            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(120) };

            // Determine query parameters based on provided dates
            string query;
            if (from == null && to == null)
            {
                string today = DateTime.Today.ToString("yyyyMMdd0000");
                query = $"from={today}&tz={tz}";
            }
            else if (to == null)
            {
                string fromParam = ((DateTime)from).ToString("yyyyMMddHHmm");
                query = $"from={fromParam}&tz={tz}";
            }
            else if (from == null)
            {
                string toParam = ((DateTime)to).ToString("yyyyMMddHHmm");
                query = $"to={toParam}&tz={tz}";
            }
            else
            {
                string fromParam = ((DateTime)from).ToString("yyyyMMddHHmm");
                string toParam = ((DateTime)to).ToString("yyyyMMddHHmm");
                query = $"from={fromParam}&to={toParam}&tz={tz}";
            }

            string url = $"https://datafeeds.rpsmetocean.com:8443/datafeeds-web/services/feeds/secured/timeseries.v2/{feedID}.json?{query}";
            Console.WriteLine($"➡️ Requesting: {url}");
            Console.WriteLine($"➡️ Requesting: {url}");

            try
            {
                var response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();
                string json = await response.Content.ReadAsStringAsync();

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

                foreach (var item in dataArray)
                {
                    var time = item.GetProperty("time").GetString();
                    var vars = item.GetProperty("variables").EnumerateArray().ToList();

                    var record = new WeatherRecord
                    {
                        DateTime = DateTime.Parse(time),
                        WindDirection = vars[6].ValueKind == JsonValueKind.Number ? CardinalService.ConvertToCompass(vars[6].GetDouble()) : "N/A",
                        WindSpeed = vars[12].ValueKind == JsonValueKind.Number ? vars[12].GetDouble() * msToKnots : double.NaN,
                        WindGustSpeed = vars[0].ValueKind == JsonValueKind.Number ? vars[0].GetDouble() * msToKnots : double.NaN,
                        Wind50Speed = vars[13].ValueKind == JsonValueKind.Number ? vars[13].GetDouble() * msToKnots : double.NaN,
                        SeaHeight = vars[2].ValueKind == JsonValueKind.Number ? vars[2].GetDouble() : double.NaN,
                        SwellDirection1 = vars[11].ValueKind == JsonValueKind.Number ? CardinalService.ConvertToCompass(vars[11].GetDouble()) : "N/A",
                        SwellPeriod1 = vars[10].ValueKind == JsonValueKind.Number ? vars[10].GetDouble() : double.NaN,
                        SwellHeight1 = vars[3].ValueKind == JsonValueKind.Number ? vars[3].GetDouble() : double.NaN,
                        SwellDirection2 = vars[7].ValueKind == JsonValueKind.Number ? CardinalService.ConvertToCompass(vars[7].GetDouble()) : "N/A",
                        SwellPeriod2 = vars[9].ValueKind == JsonValueKind.Number ? vars[9].GetDouble() : double.NaN,
                        SwellHeight2 = vars[4].ValueKind == JsonValueKind.Number ? vars[4].GetDouble() : double.NaN,
                        TotalWaveSig = vars[1].ValueKind == JsonValueKind.Number ? vars[1].GetDouble() : double.NaN,
                        TotalWaveMax = vars[5].ValueKind == JsonValueKind.Number ? vars[5].GetDouble() : double.NaN,
                        WeatherDescription = "Forecast",
                        ForecastConfidence = "High",
                        RecordIntervalHours = recordIntervalHours
                    };

                    records.Add(record);
                }

                // Push parsed records into database
                int inserted = 0, skipped = 0;
                foreach (var record in records)
                {

                    bool exists = await Database.SnapToolDB.WeatherForecastExistsAsync(record.RecordHash);
                    if (!exists)
                    {
                        await Database.SnapToolDB.InsertWeatherForecastAsync(record, blobUrl: null);
                        inserted++;
                    }
                    else
                    {
                        skipped++;
                    }
                }

                Console.WriteLine($"Inserted {inserted} new forecasts, skipped {skipped} duplicates.");
                return records;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error retrieving forecast: {ex.Message}");
                Console.WriteLine($"Inner: {ex.InnerException?.Message}");
                throw;
            }
        }


        public static async Task GetLambertWaveDataAsync()
        {
            string environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
            X509Certificate2 certificate;

            // Load certificate
            string thumbprint = Globals.Config["WeatherService:CertThumbprint"]
                ?? throw new InvalidOperationException("WeatherService:CertThumbprint missing in configuration.");

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
