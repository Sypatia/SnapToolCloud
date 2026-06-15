using SnapToolCloud.Data;
using SnapToolCloud.Models;

namespace SnapToolCloud.Service
{
    public sealed class AssessmentValidationException : Exception
    {
        public AssessmentValidationException(string message) : base(message)
        {
        }
    }

    public static class AssessmentService
    {
        private const double ForecastSearchWindowHours = 3;

        public static async Task<AssessmentResponse> AssessWindAsync(WindAssessmentRequest request)
        {
            if (request == null)
                throw new AssessmentValidationException("Request body is required.");

            ValidateVessel(request.Vessel);

            var forecast = await GetNearestForecastAsync(request.AssessmentDateTime);
            var windSpeed = forecast.WindSpeed;
            var windGust = forecast.WindGustSpeed;

            if (!IsUsableNumber(windSpeed) || !IsUsableNumber(windGust))
                throw new AssessmentValidationException("Forecast wind speed or gust is missing.");

            var response = NewResponseWithForecastFacts(forecast, request.Vessel);
            response.SupportingFacts["windDirection"] = forecast.WindDirection;
            response.SupportingFacts["windSpeedKnots"] = Math.Round(windSpeed, 2);
            response.SupportingFacts["windGustKnots"] = Math.Round(windGust, 2);

            if (windSpeed >= 20)
            {
                response.Status = AssessmentStatuses.DoNotProceed;
                response.TriggeredRules.Add("Wind speed is greater than or equal to 20 knots.");
                response.RecommendedActions.Add("Work shall not proceed.");
                return response;
            }

            if (windGust >= 20)
            {
                response.Status = AssessmentStatuses.Monitor;
                response.TriggeredRules.Add("Wind speed is less than 20 knots and wind gusts are greater than or equal to 20 knots.");
                response.RecommendedActions.Add("Nominate contractor superintendent or representative.");
                response.RecommendedActions.Add("Work may proceed with monitoring of live wind conditions.");
                return response;
            }

            response.Status = AssessmentStatuses.NoRestrictions;
            response.TriggeredRules.Add("Wind speed and wind gusts are less than 20 knots.");
            response.RecommendedActions.Add("Work may proceed with no restrictions.");
            return response;
        }

        public static async Task<AssessmentResponse> AssessWaveAsync(WaveAssessmentRequest request)
        {
            if (request == null)
                throw new AssessmentValidationException("Request body is required.");

            ValidateVessel(request.Vessel);

            var forecast = await GetNearestForecastAsync(request.AssessmentDateTime);
            var components = GetWaveComponents(forecast);

            if (components.Count == 0)
                throw new AssessmentValidationException("Forecast wave height or period is missing.");

            var response = NewResponseWithForecastFacts(forecast, request.Vessel);
            response.SupportingFacts["vesselMovementObserved"] = request.VesselMovementObserved;
            response.SupportingFacts["waveComponents"] = components.Select(c => new
            {
                c.Name,
                HeightMetres = Math.Round(c.HeightMetres, 2),
                PeriodSeconds = Math.Round(c.PeriodSeconds, 2)
            }).ToList();

            var exceedanceRules = components
                .SelectMany(GetWaveExceedanceRules)
                .ToList();

            if (exceedanceRules.Count > 0)
            {
                response.Status = AssessmentStatuses.DoNotProceed;
                response.TriggeredRules.AddRange(exceedanceRules);
                response.RecommendedActions.Add("Work shall not proceed.");
                return response;
            }

            var monitorRules = components
                .Where(c => c.HeightMetres >= 0.4 && c.HeightMetres <= 1 && c.PeriodSeconds >= 10 && c.PeriodSeconds <= 17)
                .Select(c => $"{c.Name} wave height is 0.4-1m and wave period is 10-17 seconds.")
                .ToList();

            if (monitorRules.Count > 0)
            {
                response.Status = AssessmentStatuses.Monitor;
                response.TriggeredRules.AddRange(monitorRules);
                response.RecommendedActions.Add("Monitor wave conditions.");
                response.RecommendedActions.Add("Nominate contractor superintendent to complete task-based COC.");
                response.RecommendedActions.Add(
                    request.VesselMovementObserved
                        ? "Record that vessel heaving and/or rolling is observed."
                        : "Record that no vessel heaving and/or rolling is observed.");
                response.RecommendedActions.Add("Work may proceed with monitoring of conditions.");
                return response;
            }

            var allowanceRules = components
                .Where(c => c.HeightMetres < 0.4 && c.PeriodSeconds >= 0 && c.PeriodSeconds <= 17)
                .Select(c => $"{c.Name} wave height is less than 0.4m and wave period is 0-17 seconds.")
                .ToList();

            response.Status = AssessmentStatuses.NoRestrictions;
            response.TriggeredRules.AddRange(allowanceRules.Count > 0
                ? allowanceRules
                : new[] { "No wave monitor or exceedance threshold was triggered." });
            response.RecommendedActions.Add("Work may proceed with no restrictions.");
            return response;
        }

        public static AssessmentResponse AssessPassingVessels(PassingVesselAssessmentRequest request)
        {
            if (request == null)
                throw new AssessmentValidationException("Request body is required.");

            if (request.PassingVessels == null || request.PassingVessels.Count == 0)
                throw new AssessmentValidationException("At least one passing vessel movement is required.");

            var response = new AssessmentResponse
            {
                Status = AssessmentStatuses.NoRestrictions
            };

            response.SupportingFacts["passingVessels"] = request.PassingVessels.Select(v => new
            {
                v.VesselName,
                v.Location,
                v.MovementType,
                v.ScheduledDateTime
            }).ToList();

            foreach (var movement in request.PassingVessels)
            {
                var location = Normalize(movement.Location);
                var movementType = Normalize(movement.MovementType);

                if (string.IsNullOrWhiteSpace(location) || string.IsNullOrWhiteSpace(movementType))
                    throw new AssessmentValidationException("Each passing vessel must include location and movementType.");

                var isBerthing = movementType.Contains("BERTH");
                var isSailing = movementType.Contains("SAIL");

                if (location == "PPT4" && isBerthing)
                {
                    AddPassingRule(
                        response,
                        movement,
                        "Vessel scheduled to berth at PPT4.",
                        "Vacate PPT4 for 30 minutes before and after berthing activity.");
                }

                if (location == "PPT2" && isSailing)
                {
                    AddPassingRule(
                        response,
                        movement,
                        "Vessel scheduled to sail from PPT2.",
                        "Vacate PPT2 for 30 minutes before and after sail.");
                }

                if (location == "PPT3" && isSailing)
                {
                    AddPassingRule(
                        response,
                        movement,
                        "Vessel scheduled to sail from PPT3.",
                        "Vacate PPT5 for 30 minutes before and after sail.");
                }

                if (location == "FUEL TERMINAL" && isBerthing)
                {
                    AddPassingRule(
                        response,
                        movement,
                        "Vessel scheduled to berth at Fuel Terminal.",
                        "Vacate PPT4 and PPT2 for 30 minutes before and after berthing activity.");
                }
            }

            if (response.TriggeredRules.Count == 0)
            {
                response.RecommendedActions.Add("Work may proceed with no restrictions.");
                response.TriggeredRules.Add("No passing vessel interaction threshold was triggered.");
                return response;
            }

            response.Status = AssessmentStatuses.Monitor;
            response.RecommendedActions.Insert(0, "Monitor vessel berthing and sailing activities.");
            response.RecommendedActions.Insert(1, "Contractor superintendent to communicate with the Marine Scheduler.");
            response.RecommendedActions.Insert(2, "Confirm the time of vessel movements.");
            response.RecommendedActions = response.RecommendedActions.Distinct().ToList();
            return response;
        }

        private static async Task<WeatherRecord> GetNearestForecastAsync(DateTime assessmentDateTime)
        {
            if (assessmentDateTime == default)
                throw new AssessmentValidationException("assessmentDateTime is required.");

            var from = assessmentDateTime.AddHours(-ForecastSearchWindowHours);
            var to = assessmentDateTime.AddHours(ForecastSearchWindowHours);
            var forecasts = await WeatherService.GetRTIOB10ForecastAsync(from, to);

            if (forecasts.Count == 0)
                throw new AssessmentValidationException("No forecast records were returned for the assessment window.");

            var target = TimeZoneParser.AsPerthLocal(assessmentDateTime);
            var nearest = forecasts
                .OrderBy(f => Math.Abs((TimeZoneParser.AsPerthLocal(f.DateTimeForecast) - target).TotalMinutes))
                .First();

            var nearestDiffHours = Math.Abs((TimeZoneParser.AsPerthLocal(nearest.DateTimeForecast) - target).TotalHours);
            var allowedDiffHours = Math.Max(1, nearest.RecordIntervalHours > 0 ? nearest.RecordIntervalHours : 1);

            if (nearestDiffHours > allowedDiffHours)
                throw new AssessmentValidationException("No forecast record was close enough to the requested assessment time.");

            return nearest;
        }

        private static AssessmentResponse NewResponseWithForecastFacts(WeatherRecord forecast, ManualVesselInfo? vessel)
        {
            return new AssessmentResponse
            {
                SupportingFacts =
                {
                    ["forecastDateTime"] = forecast.DateTimeForecast,
                    ["forecastIntervalHours"] = forecast.RecordIntervalHours,
                    ["forecastConfidence"] = forecast.ForecastConfidence,
                    ["vessel"] = vessel
                }
            };
        }

        private static void ValidateVessel(ManualVesselInfo? vessel)
        {
            if (vessel == null)
                throw new AssessmentValidationException("vessel is required.");

            if (string.IsNullOrWhiteSpace(vessel.Berth))
                throw new AssessmentValidationException("vessel.berth is required.");

            if (string.IsNullOrWhiteSpace(vessel.Vessel))
                throw new AssessmentValidationException("vessel.vessel is required.");

            if (string.IsNullOrWhiteSpace(vessel.MC))
                throw new AssessmentValidationException("vessel.mc is required.");
        }

        private static List<WaveComponent> GetWaveComponents(WeatherRecord forecast)
        {
            var components = new List<WaveComponent>();

            AddWaveComponent(components, "Swell 1", forecast.SwellHeight1, forecast.SwellPeriod1);
            AddWaveComponent(components, "Swell 2", forecast.SwellHeight2, forecast.SwellPeriod2);

            var maxPeriod = new[] { forecast.SwellPeriod1, forecast.SwellPeriod2 }
                .Where(p => p.HasValue && IsUsableNumber(p.Value))
                .Select(p => p!.Value)
                .DefaultIfEmpty(double.NaN)
                .Max();

            if (IsUsableNumber(forecast.TotalWaveSig) && IsUsableNumber(maxPeriod))
                components.Add(new WaveComponent("Total wave significant", forecast.TotalWaveSig, maxPeriod));

            return components;
        }

        private static void AddWaveComponent(List<WaveComponent> components, string name, double? height, double? period)
        {
            if (height.HasValue && period.HasValue && IsUsableNumber(height.Value) && IsUsableNumber(period.Value))
                components.Add(new WaveComponent(name, height.Value, period.Value));
        }

        private static IEnumerable<string> GetWaveExceedanceRules(WaveComponent component)
        {
            if (component.HeightMetres >= 1.5 && component.PeriodSeconds >= 8)
                yield return $"{component.Name} wave height is at least 1.5m and wave period is at least 8 seconds.";

            if (component.HeightMetres > 1 && component.PeriodSeconds > 10)
                yield return $"{component.Name} wave height is greater than 1m and wave period is greater than 10 seconds.";
        }

        private static void AddPassingRule(
            AssessmentResponse response,
            PassingVesselMovement movement,
            string rule,
            string action)
        {
            var vesselName = string.IsNullOrWhiteSpace(movement.VesselName)
                ? "Unspecified vessel"
                : movement.VesselName.Trim();

            response.TriggeredRules.Add($"{rule} Vessel: {vesselName}.");
            response.RecommendedActions.Add(action);
        }

        private static bool IsUsableNumber(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0;
        }

        private static string Normalize(string? value)
        {
            return value?.Trim().ToUpperInvariant() ?? string.Empty;
        }

        private sealed record WaveComponent(string Name, double HeightMetres, double PeriodSeconds);
    }
}
