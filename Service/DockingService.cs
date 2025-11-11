using SnapToolCloud.Data;

namespace SnapToolCloud.Service
{
    public class DockingService
    {
        public static async Task<List<DockingArrangementRecord>> GetDockingArrangementAsync(DateTime? from = null, DateTime? to = null)
        {
            try
            {
                await Task.Delay(50); // Simulate async operation

                var dockingArrangement = new List<DockingArrangementRecord>
            {
                new()
                {
                    Berth = "Berth 02",
                    Vessel = "180k",
                    MC = "MC1",
                    VesselName = "Iron Voyager",
                    PlanToBerth = DateTime.UtcNow.AddHours(2),
                    PlanToSail = DateTime.UtcNow.AddHours(12)
                },
                new()
                {
                    Berth = "Berth 03",
                    Vessel = "180k",
                    MC = "MC2",
                    VesselName = "Ocean Titan",
                    PlanToBerth = DateTime.UtcNow.AddHours(5),
                    PlanToSail = DateTime.UtcNow.AddHours(20)
                },
                new()
                {
                    Berth = "Berth 04",
                    Vessel = "180k",
                    MC = "MC2",
                    VesselName = "Sea Horizon",
                    PlanToBerth = DateTime.UtcNow.AddHours(8),
                    PlanToSail = DateTime.UtcNow.AddHours(30)
                },
                new()
                {
                    Berth = "Berth 05",
                    Vessel = "180k",
                    MC = "MC1",
                    VesselName = "Pacific Glory",
                    PlanToBerth = DateTime.UtcNow.AddHours(1),
                    PlanToSail = DateTime.UtcNow.AddHours(10)
                }
            };

                // Push parsed records into database
                int inserted = 0, skipped = 0;
                foreach (var record in dockingArrangement)
                {

                    bool exists = await Database.SnapToolDB.DockingArrangementExistsAsync(record.RecordHash);
                    if (!exists)
                    {
                        await Database.SnapToolDB.InsertDockingArrangementAsync(record);
                        inserted++;
                    }
                    else
                    {
                        skipped++;
                    }
                }

                return dockingArrangement;
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }
    }
}
