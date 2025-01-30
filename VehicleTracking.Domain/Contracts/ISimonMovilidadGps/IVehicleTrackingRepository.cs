using VehicleTracking.Infrastructure;

namespace VehicleTracking.Domain.Contracts.ISimonMovilidadGps
{
    public interface IVehicleTrackingRepository
    {
        Task<IEnumerable<Vehicle>> GetActiveVehiclesWithManifestAsync();
        Task AddVehicleTrackingAsync(VehicleInfoLocation tracking);
    }
}
