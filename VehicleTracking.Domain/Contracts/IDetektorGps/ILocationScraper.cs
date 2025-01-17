using VehicleTracking.Shared.InDTO.DetektorGps;

namespace VehicleTracking.Domain.Contracts.IDetektorGps
{
    public interface ILocationScraper : IDisposable
    {
        Task<bool> LoginAsync(string username, string password);
        Task<LocationDataInfo> GetVehicleLocationAsync(string patent);
    }
}
