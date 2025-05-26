using VehicleTracking.Infrastructure;

namespace VehicleTracking.Domain.Contracts.ISatrackGps
{
    public interface IVehicleTrackingRepository
    {
        Task<IEnumerable<Vehicle>> GetActiveVehiclesWithManifestAsync();
        Task AddVehicleTrackingAsync(VehicleInfoLocation tracking);
    }
}
