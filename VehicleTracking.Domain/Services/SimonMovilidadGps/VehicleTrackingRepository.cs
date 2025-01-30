using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VehicleTracking.Domain.Contracts.ISimonMovilidadGps;
using VehicleTracking.Infrastructure;
using VehicleTracking.Shared.InDTO.InDTOGps;
using VehicleTracking.Util.Constants;

namespace VehicleTracking.Domain.Services.SimonMovilidadGps
{
    public class VehicleTrackingRepository : IVehicleTrackingRepository
    {
        private readonly IDbContextFactory<DBContext> _contextFactory;
        private readonly TrackingSettings _settings;

        public VehicleTrackingRepository(
            IDbContextFactory<DBContext> contextFactory,
            IOptions<TrackingSettings> settings)
        {
            _contextFactory = contextFactory;
            _settings = settings.Value;
        }

        public async Task<IEnumerable<Vehicle>> GetActiveVehiclesWithManifestAsync()
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var vehicles = await context.Vehicles
                .Join(context.Manifests,
                    v => v.Id,
                    m => m.VehicleId,
                    (v, m) => new { Vehicle = v, Manifest = m })
                .Where(vm =>
                    vm.Vehicle.Provider == _settings.Providers.SimonMovilidad.Name &&
                    vm.Manifest.Active &&
                    vm.Manifest.Process != 7 &&
                    vm.Manifest.State != 36 &&
                    vm.Manifest.State != 35)
                .Select(vm => new Vehicle
                {
                    Id = vm.Vehicle.Id,
                    Patent = vm.Vehicle.Patent,
                    Provider = vm.Vehicle.Provider,
                    User = vm.Vehicle.User,
                    Password = vm.Vehicle.Password,
                    VehicleInfoLocations = new List<VehicleInfoLocation>
                    {
                        new VehicleInfoLocation
                        {
                            ManifestId = vm.Manifest.Id,
                            IsActive = true
                        }
                    }
                })
                .ToListAsync();

            return vehicles;
        }

        public async Task AddVehicleTrackingAsync(VehicleInfoLocation tracking)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            try
            {
                var geoPoint = new GeoPoint(
                    Convert.ToDouble(tracking.Latitude),
                    Convert.ToDouble(tracking.Longitude)
                );

                tracking.Location = geoPoint.ToPoint();

                if (tracking.Created == default)
                {
                    tracking.Created = DateTime.UtcNow;
                }

                tracking.IsActive = true;
                tracking.Reason = tracking.Reason ?? string.Empty;
                tracking.Driver = tracking.Driver ?? string.Empty;
                tracking.Georeference = tracking.Georeference ?? string.Empty;
                tracking.InZone = tracking.InZone ?? string.Empty;
                tracking.DetentionTime = tracking.DetentionTime ?? string.Empty;
                tracking.DistanceTraveled = tracking.DistanceTraveled ?? 0m;
                tracking.Temperature = tracking.Temperature ?? 0m;
                tracking.Angle = tracking.Angle ?? 0;

                await context.VehicleInfoLocations.AddAsync(tracking);
                await context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error al agregar el tracking del vehículo: {ex.Message}", ex);
            }
        }
    }
}
