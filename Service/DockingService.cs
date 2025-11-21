using SnapToolCloud.Data;

namespace SnapToolCloud.Service
{
    public class DockingService
    {
        public static async Task<List<DockingArrangementRecord>> GetDockingArrangementAsync(
    DateTime? from = null,
    DateTime? to = null)
        {
            await Task.Delay(50); // Simulate async call / future API request

            var dockingArrangement = new List<DockingArrangementRecord>
    {
        new()
        {
            Berth = "Berth 02",
            Vessel = "180k",
            MC = "MC1",
            VesselName = "Iron Voyager",
            AllSecureDateTime = DateTime.UtcNow.AddHours(2),
            SailDateTime = DateTime.UtcNow.AddHours(12)
        },
        new()
        {
            Berth = "Berth 03",
            Vessel = "180k",
            MC = "MC1",
            VesselName = "Iron Defender",
            AllSecureDateTime = DateTime.UtcNow.AddHours(3),
            SailDateTime = DateTime.UtcNow.AddHours(13)
        },
        new()
        {
            Berth = "Berth 04",
            Vessel = "180k",
            MC = "MC1",
            VesselName = "Iron Titan",
            AllSecureDateTime = DateTime.UtcNow.AddHours(4),
            SailDateTime = DateTime.UtcNow.AddHours(14)
        },
        new()
        {
            Berth = "Berth 05",
            Vessel = "180k",
            MC = "MC1",
            VesselName = "Iron Guardian",
            AllSecureDateTime = DateTime.UtcNow.AddHours(5),
            SailDateTime = DateTime.UtcNow.AddHours(15)
        }
    };

            return dockingArrangement;
        }
    }
}
