using VehicleTracking.Shared.InDTO.InDTOGps;

namespace VehicleTracking.Domain.Contracts.ISimonMovilidadGps
{
    public interface ILocationScraper : IDisposable
    {
        Task<bool> LoginAsync(string username, string password, string patent);
        Task<LocationDataInfo> GetVehicleLocationAsync(string patent);
    }
}
