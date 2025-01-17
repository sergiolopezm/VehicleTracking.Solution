using VehicleTracking.Infrastructure;

namespace VehicleTracking.Domain.Contracts.IDetektorGps
{
    public interface IVehicleTrackingRepository
    {
        Task<IEnumerable<Vehicle>> GetActiveVehiclesWithManifestAsync();
        Task AddVehicleTrackingAsync(VehicleInfoLocation tracking);
    }
}
