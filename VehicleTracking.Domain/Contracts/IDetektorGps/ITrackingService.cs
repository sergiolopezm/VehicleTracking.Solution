using VehicleTracking.Infrastructure;
using VehicleTracking.Shared.InDTO.DetektorGps;
using VehicleTracking.Shared.OutDTO.DetektorGps;

namespace VehicleTracking.Domain.Contracts.IDetektorGps
{
    public interface ITrackingService
    {
        Task<ListaPaginada<VehicleTrackingResultDto>> TrackVehiclesAsync(string idUsuario, string ip);
        Task<LocationDataInfo?> GetVehicleStatusAsync(string patent, string idUsuario, string ip);
    }
}
