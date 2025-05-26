using Microsoft.EntityFrameworkCore;
using VehicleTracking.Domain.Contracts;
using VehicleTracking.Domain.Contracts.ISatrackGps;
using VehicleTracking.Infrastructure;
using VehicleTracking.Shared.InDTO.InDTOGps;
using VehicleTracking.Shared.OutDTO.OutDTOGps;
using VehicleTracking.Util.Constants;

namespace VehicleTracking.Domain.Services.SatrackGps
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
            ILocationScraper? scraper = null;

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

                scraper = _scraperFactory.CreateScraperWithContext(vehicle.Provider, idUsuario, ip);

                var loginSuccess = await scraper.LoginAsync(vehicle.User, vehicle.Password, vehicle.Patent);
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
                    _log.Info(idUsuario, ip, "GetVehicleStatus",
                        $"Estado obtenido exitosamente para vehículo {patent}");
                }

                return locationData;
            }
            catch (Exception ex)
            {
                _log.Error(idUsuario, ip, "GetVehicleStatus",
                    $"Error al obtener estado del vehículo {patent}: {ex.Message}");
                throw;
            }
            finally
            {
                // Asegurar que se libere el scraper
                scraper?.Dispose();
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

                // Procesar cada vehículo individualmente con su propio scraper
                foreach (var vehicle in vehicles)
                {
                    ILocationScraper? scraper = null;
                    var result = new VehicleTrackingResultDto
                    {
                        Patent = vehicle.Patent,
                        ProcessedAt = DateTime.UtcNow
                    };

                    try
                    {
                        _log.Info(idUsuario, ip, "TrackVehicles",
                            $"Iniciando procesamiento de vehículo {vehicle.Patent}");

                        // Crear un nuevo scraper para cada vehículo
                        scraper = _scraperFactory.CreateScraperWithContext(vehicle.Provider, idUsuario, ip);

                        var loginSuccess = await scraper.LoginAsync(vehicle.User, vehicle.Password, vehicle.Patent);
                        if (!loginSuccess)
                        {
                            result.Success = false;
                            result.Message = "No se pudo iniciar sesión con las credenciales proporcionadas";
                            result.Status = "Error de autenticación";

                            _log.Error(idUsuario, ip, "TrackVehicles",
                                $"Error de autenticación para vehículo {vehicle.Patent}");

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

                            _log.Info(idUsuario, ip, "TrackVehicles",
                                $"Vehículo {vehicle.Patent} procesado exitosamente");
                        }
                        else
                        {
                            result.Success = false;
                            result.Message = "No se pudo obtener información de ubicación";
                            result.Status = "Sin datos";

                            _log.Error(idUsuario, ip, "TrackVehicles",
                                $"No se pudo obtener información de ubicación para vehículo {vehicle.Patent}");
                        }
                    }
                    catch (InvalidOperationException ex) when (ex.Message.StartsWith("CONFIGURACION_INVALIDA:"))
                    {
                        result.Success = false;
                        result.Message = "El vehículo no está disponible con las credenciales actuales";
                        result.Status = "Error de configuración";

                        _log.Error(idUsuario, ip, "TrackVehicles",
                            $"Error de configuración para vehículo {vehicle.Patent}: {ex.Message}");
                    }
                    catch (InvalidOperationException ex) when (ex.Message.StartsWith("SERVIDOR_CAIDO:"))
                    {
                        result.Success = false;
                        result.Message = "Error de conectividad con el servidor";
                        result.Status = "Error de servidor";

                        _log.Error(idUsuario, ip, "TrackVehicles",
                            $"Error de servidor para vehículo {vehicle.Patent}: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        result.Success = false;
                        result.Message = "Error durante el procesamiento del vehículo";
                        result.Status = "Error interno";

                        _log.Error(idUsuario, ip, "TrackVehicles",
                            $"Error procesando vehículo {vehicle.Patent}: {ex.Message}");
                    }
                    finally
                    {
                        // IMPORTANTE: Cerrar y liberar el scraper después de cada vehículo
                        // Esto asegura que cada vehículo tenga una nueva sesión de navegador
                        try
                        {
                            scraper?.Dispose();
                            _log.Info(idUsuario, ip, "TrackVehicles",
                                $"Scraper cerrado correctamente para vehículo {vehicle.Patent}");
                        }
                        catch (Exception disposeEx)
                        {
                            _log.Info(idUsuario, ip, "TrackVehicles",
                                $"Error al cerrar scraper para vehículo {vehicle.Patent}: {disposeEx.Message}");
                        }
                    }

                    results.Add(result);

                    // Breve pausa entre vehículos para evitar sobrecargar el servidor
                    await Task.Delay(1000);
                }

                var exitosos = results.Count(r => r.Success);
                var fallidos = results.Count - exitosos;

                _log.Info(idUsuario, ip, "TrackVehicles",
                    $"Proceso de tracking completado. Total: {results.Count}, Exitosos: {exitosos}, Fallidos: {fallidos}");

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
