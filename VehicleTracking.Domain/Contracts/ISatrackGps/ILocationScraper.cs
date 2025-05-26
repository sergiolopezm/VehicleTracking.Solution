using VehicleTracking.Shared.InDTO.InDTOGps;

namespace VehicleTracking.Domain.Contracts.ISatrackGps
{
    public interface ILocationScraper : IDisposable
    {
        Task<bool> LoginAsync(string username, string password);
        Task<LocationDataInfo> GetVehicleLocationAsync(string patent);
    }
}
