using VehicleTracking.Infrastructure;
using VehicleTracking.Shared.InDTO.InDTOGps;
using VehicleTracking.Shared.OutDTO.OutDTOGps;


namespace VehicleTracking.Domain.Contracts.ISimonMovilidadGps
{
    public interface ITrackingService
    {
        Task<ListaPaginada<VehicleTrackingResultDto>> TrackVehiclesAsync(string idUsuario, string ip);
        Task<LocationDataInfo?> GetVehicleStatusAsync(string patent, string idUsuario, string ip);
    }
}
