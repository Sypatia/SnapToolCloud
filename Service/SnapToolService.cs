using SnapToolCloud.Data;
using System.Data;
using System.Globalization;
using System.Reflection.Metadata.Ecma335;

namespace SnapToolCloud.Service
{
    public class SnapToolService
    {
        public static (List<PileDolphinRecord> PileDolphinActivations, List<GridRecord> GridActivations) GetActiveElements(List<WeatherRecord> weatherForecast, List<DockingArrangementRecord> dockingArrangements)
        {
            List<PileDolphinRecord> activePileDolphins = new();
            List<GridRecord> activeGrids = new();

            foreach (var weatherRecord in weatherForecast)
            {
                foreach (var dockingArrangement in dockingArrangements)
                {
                    var filteredWindRecords = DataFilter.FilterDMADataWindOnly(dockingArrangement, weatherRecord.DateTime, weatherForecast);

                    var filteredWaveRecords = DataFilter.FilterDMADataWaveOnly(dockingArrangement, weatherRecord.DateTime, weatherForecast);

                    activePileDolphins.AddRange(DataFilter.FilterActiveRecords(filteredWindRecords, PileDolphinDataLoader.ParseRecords));
                    activePileDolphins.AddRange(DataFilter.FilterActiveRecords(filteredWaveRecords, PileDolphinDataLoader.ParseRecords));
                    activeGrids.AddRange(DataFilter.FilterActiveRecords(filteredWaveRecords, GridDataLoader.ParseRecords));
                    activeGrids.AddRange(DataFilter.FilterActiveRecords(filteredWindRecords, GridDataLoader.ParseRecords));
                }
            }

            // Deduplicate based on meaningful composite key
            activePileDolphins = activePileDolphins
                .GroupBy(p => $"{p.Berth}|{p.Vessel}|{p.MC}|{p.ML}|{p.CombinedLocation}")
                .Select(g => g.First())
                .ToList();

            activeGrids = activeGrids
                .GroupBy(g => $"{g.Berth}|{g.Vessel}|{g.MC}|{g.ML}|{g.CombinedLocation}")
                .Select(g => g.First())
                .ToList();

            return (activePileDolphins, activeGrids);
        }

        public async static void RunWorkflow(DateTime startDate, DateTime endDate)
        {
            // Get today's date
            // Generate active piles and dolphins for every line in forecast
            List<WeatherRecord> weatherForecast = await WeatherService.GetRTIOB10ForecastAsync();
            List<DockingArrangementRecord> dockingArrangement = await DockingService.GetDockingArrangementAsync();

            // For each weather forecast ROW
            foreach (var weatherRecord in weatherForecast)
            {
                List<WeatherRecord> tempRecordList = new List<WeatherRecord>();
                tempRecordList.Add(weatherRecord);

                var (pileDolphinActivations, gridActivations) = GetActiveElements(tempRecordList, dockingArrangement);
            }
            // For a given docking arrangement
            // run activation, store activations in database

        }

    }
}
