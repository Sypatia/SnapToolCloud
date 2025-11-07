using SnapToolCloud.Data;
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

        public static async Task GetWeatherForecastFeedsAsync()
        {
            var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
            X509Certificate2 certificate;

            if (environment == "Development" || environment == "Production")
            {
                // 🔹 Load from certificate store using thumbprint from config
                string thumbprint = Globals.Config["WeatherService:CertThumbprint"]
                    ?? throw new InvalidOperationException("WeatherService:CertThumbprint missing in configuration.");

                using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
                store.Open(OpenFlags.ReadOnly);

                certificate = store.Certificates
                    .Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false)
                    .OfType<X509Certificate2>()
                    .FirstOrDefault()
                    ?? throw new InvalidOperationException($"Certificate with thumbprint {thumbprint} not found.");

                Console.WriteLine($"✅ Loaded cert from store: {certificate.Subject}, HasPrivateKey={certificate.HasPrivateKey}");
            }
            else
            {
                // 🔹 Fallback: load from embedded resource (for packaged local testing)
                var assembly = Assembly.GetExecutingAssembly();
                string? resourceName = assembly.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("WeatherSslCert.p12"))
                    ?? throw new FileNotFoundException("Embedded certificate not found.");

                using Stream stream = assembly.GetManifestResourceStream(resourceName)
                    ?? throw new FileNotFoundException($"Could not open {resourceName}");

                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                byte[] certBytes = ms.ToArray();

                string certPassword = Globals.Config["WeatherService:Password"]
                    ?? throw new InvalidOperationException("WeatherService:Password not found in configuration.");

                certificate = new X509Certificate2(
                    certBytes,
                    certPassword,
                    X509KeyStorageFlags.MachineKeySet |
                    X509KeyStorageFlags.PersistKeySet |
                    X509KeyStorageFlags.Exportable
                );

                Console.WriteLine($"✅ Loaded cert from embedded resource, HasPrivateKey={certificate.HasPrivateKey}");
            }

            // 🔹 Setup HttpClient with mutual TLS
            var handler = new HttpClientHandler
            {
                SslProtocols = System.Security.Authentication.SslProtocols.Tls12
            };

            handler.ClientCertificates.Add(certificate);

            // 🔹 Certificate validation (strict in prod, relaxed in dev)
            handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
            {
                Console.WriteLine($"Server certificate: {cert?.Subject}");
                Console.WriteLine($"SSL Policy Errors: {errors}");

                if (chain != null)
                {
                    foreach (var status in chain.ChainStatus)
                        Console.WriteLine($"Chain Status: {status.Status} - {status.StatusInformation}");
                }

                return environment == "Development"
                    ? true // bypass for local testing
                    : errors == SslPolicyErrors.None;
            };

            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            try
            {
                Console.WriteLine($"➡️ Sending request to: {ListEndpoint}");
                var response = await client.GetAsync(ListEndpoint);
                response.EnsureSuccessStatusCode();

                string json = await response.Content.ReadAsStringAsync();
                Console.WriteLine("✅ Raw response:");
                Console.WriteLine(json);

                try
                {
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    switch (root.ValueKind)
                    {
                        case JsonValueKind.Array:
                            Console.WriteLine($"Feeds count: {root.GetArrayLength()}");
                            break;

                        case JsonValueKind.Object:
                            Console.WriteLine("Feeds JSON is an object. Top-level keys:");
                            foreach (var prop in root.EnumerateObject())
                            {
                                Console.WriteLine($"- {prop.Name}");
                            }
                            break;

                        default:
                            Console.WriteLine($"Unexpected JSON root type: {root.ValueKind}");
                            break;
                    }
                }
                catch (JsonException)
                {
                    Console.WriteLine("Response is plain text, not JSON.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error retrieving feeds: {ex.Message}");
                Console.WriteLine($"Inner exception: {ex.InnerException?.Message}");
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
