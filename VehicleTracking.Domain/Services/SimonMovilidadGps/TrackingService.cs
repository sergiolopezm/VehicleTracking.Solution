﻿using Microsoft.EntityFrameworkCore;
using VehicleTracking.Domain.Contracts;
using VehicleTracking.Domain.Contracts.ISimonMovilidadGps;
using VehicleTracking.Infrastructure;
using VehicleTracking.Shared.InDTO.InDTOGps;
using VehicleTracking.Shared.OutDTO.OutDTOGps;
using VehicleTracking.Util.Constants;

namespace VehicleTracking.Domain.Services.SimonMovilidadGps
{
    public class TrackingService : ITrackingService
    {
        private readonly IVehicleTrackingRepository _repository;
        private readonly ILocationScraperFactory _scraperFactory;
        private readonly ILogRepository _log;
        private readonly IDbContextFactory<DBContext> _contextFactory;

        public TrackingService(
            IVehicleTrackingRepository repository,
            ILocationScraperFactory scraperFactory,
            ILogRepository log,
            IDbContextFactory<DBContext> contextFactory)
        {
            _repository = repository;
            _scraperFactory = scraperFactory;
            _log = log;
            _contextFactory = contextFactory;
        }

        public async Task<LocationDataInfo?> GetVehicleStatusAsync(string patent, string idUsuario, string ip)
        {
            try
            {
                _log.Info(idUsuario, ip, "GetVehicleStatus",
                    $"Obteniendo estado del vehículo {patent}");

                var vehicles = await _repository.GetActiveVehiclesWithManifestAsync();
                var vehicle = vehicles.FirstOrDefault(v => v.Patent == patent);

                if (vehicle == null)
                {
                    _log.Info(idUsuario, ip, "GetVehicleStatus",
                        $"No se encontró el vehículo con placa {patent}");
                    return null;
                }

                using var scraper = _scraperFactory.CreateScraperWithContext(vehicle.Provider, idUsuario, ip);

                var loginSuccess = await scraper.LoginAsync(vehicle.User, vehicle.Password);
                if (!loginSuccess)
                {
                    _log.Error(idUsuario, ip, "GetVehicleStatus",
                        $"Error al iniciar sesión para el vehículo {patent}");
                    return null;
                }

                var locationData = await scraper.GetVehicleLocationAsync(patent);
                if (locationData != null)
                {
                    await SaveVehicleTracking(vehicle, locationData);
                }

                return locationData;
            }
            catch (Exception ex)
            {
                _log.Error(idUsuario, ip, "GetVehicleStatus",
                    $"Error al obtener estado del vehículo {patent}: {ex.Message}");
                throw;
            }
        }

        public async Task<ListaPaginada<VehicleTrackingResultDto>> TrackVehiclesAsync(string idUsuario, string ip)
        {
            try
            {
                _log.Info(idUsuario, ip, "TrackVehicles", "Iniciando proceso de tracking de vehículos");

                var vehicles = await _repository.GetActiveVehiclesWithManifestAsync();
                if (!vehicles.Any())
                {
                    _log.Info(idUsuario, ip, "TrackVehicles", "No se encontraron vehículos activos para rastrear");
                    return new ListaPaginada<VehicleTrackingResultDto>
                    {
                        lista = new List<VehicleTrackingResultDto>()
                    };
                }

                var results = new List<VehicleTrackingResultDto>();

                foreach (var group in vehicles.GroupBy(v => v.Provider))
                {
                    using var scraper = _scraperFactory.CreateScraperWithContext(group.Key, idUsuario, ip);
                    var firstVehicleInGroup = true;

                    foreach (var vehicle in group)
                    {
                        var result = new VehicleTrackingResultDto
                        {
                            Patent = vehicle.Patent,
                            ProcessedAt = DateTime.UtcNow
                        };

                        try
                        {
                            // Realizar login solo si es el primer vehículo o si el anterior cerró sesión
                            var loginSuccess = await scraper.LoginAsync(vehicle.User, vehicle.Password);
                            if (!loginSuccess)
                            {
                                result.Success = false;
                                result.Message = "No se pudo iniciar sesión con las credenciales proporcionadas";
                                result.Status = "Error de autenticación";
                                results.Add(result);
                                continue;
                            }

                            var locationData = await scraper.GetVehicleLocationAsync(vehicle.Patent);
                            if (locationData != null)
                            {
                                using var context = await _contextFactory.CreateDbContextAsync();
                                var tracking = new VehicleInfoLocation
                                {
                                    VehicleId = vehicle.Id,
                                    ManifestId = vehicle.VehicleInfoLocations.First().ManifestId,
                                    Latitude = locationData.Latitude,
                                    Longitude = locationData.Longitude,
                                    Speed = locationData.Speed,
                                    Timestamp = locationData.Timestamp,
                                    Provider = vehicle.Provider,
                                    Created = DateTime.UtcNow,
                                    IsActive = true,
                                    Reason = locationData.Reason,
                                    Driver = locationData.Driver,
                                    Georeference = locationData.Georeference,
                                    InZone = locationData.InZone,
                                    DetentionTime = locationData.DetentionTime,
                                    DistanceTraveled = locationData.DistanceTraveled,
                                    Temperature = locationData.Temperature,
                                    Location = new GeoPoint(
                                        Convert.ToDouble(locationData.Latitude),
                                        Convert.ToDouble(locationData.Longitude)
                                    ).ToPoint(),
                                    Angle = (short?)locationData.Angle
                                };

                                await context.VehicleInfoLocations.AddAsync(tracking);
                                await context.SaveChangesAsync();

                                result.Success = true;
                                result.Message = "Ubicación registrada exitosamente";
                                result.Status = "Procesado";
                                result.Latitude = locationData.Latitude;
                                result.Longitude = locationData.Longitude;
                            }
                        }
                        catch (Exception ex)
                        {
                            result.Success = false;
                            result.Message = ex.Message;
                            result.Status = "Error";
                            _log.Error(idUsuario, ip, "TrackVehicles",
                                $"Error procesando vehículo {vehicle.Patent}: {ex.Message}");
                        }

                        results.Add(result);
                    }
                }

                return new ListaPaginada<VehicleTrackingResultDto>
                {
                    lista = results,
                    pagina = 1,
                    totalPaginas = 1,
                    totalRegistros = results.Count
                };
            }
            catch (Exception ex)
            {
                _log.Error(idUsuario, ip, "TrackVehicles",
                    $"Error general en el proceso de tracking: {ex.Message}");
                throw;
            }
        }

        private async Task SaveVehicleTracking(Vehicle vehicle, LocationDataInfo locationData)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var tracking = new VehicleInfoLocation
            {
                VehicleId = vehicle.Id,
                ManifestId = vehicle.VehicleInfoLocations.First().ManifestId,
                Latitude = locationData.Latitude,
                Longitude = locationData.Longitude,
                Speed = locationData.Speed,
                Timestamp = locationData.Timestamp,
                Provider = vehicle.Provider,
                Created = DateTime.UtcNow,
                IsActive = true,
                Reason = locationData.Reason,
                Driver = locationData.Driver,
                Georeference = locationData.Georeference,
                InZone = locationData.InZone,
                DetentionTime = locationData.DetentionTime,
                DistanceTraveled = locationData.DistanceTraveled,
                Temperature = locationData.Temperature,
                Location = new GeoPoint(
                    Convert.ToDouble(locationData.Latitude),
                    Convert.ToDouble(locationData.Longitude)
                ).ToPoint(),
                Angle = (short?)locationData.Angle
            };

            await _repository.AddVehicleTrackingAsync(tracking);
        }
    }
}
