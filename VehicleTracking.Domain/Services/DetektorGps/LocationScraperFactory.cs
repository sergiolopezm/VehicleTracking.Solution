using Microsoft.Extensions.Options;
using VehicleTracking.Domain.Contracts.IDetektorGps;
using VehicleTracking.Domain.Scraping.DetektorGps;
using VehicleTracking.Shared.InDTO.DetektorGps;
using VehicleTracking.Util.Helpers;

namespace VehicleTracking.Domain.Services.DetektorGps
{
    public class LocationScraperFactory : ILocationScraperFactory
    {
        private readonly IFileLogger _fileLogger;
        private readonly IRepositoryLogger _logRepository;
        private readonly IOptions<TrackingSettings> _settings;

        public LocationScraperFactory(
            IFileLogger fileLogger,
            IRepositoryLogger logRepository,
            IOptions<TrackingSettings> settings)
        {
            _fileLogger = fileLogger;
            _logRepository = logRepository;
            _settings = settings;
        }

        // Implementación del método original
        public ILocationScraper CreateScraper(string provider)
        {
            // Por defecto usamos "SYSTEM" como userId e ip cuando no se proporciona contexto
            return CreateScraperWithContext(provider, "SYSTEM", "SYSTEM");
        }

        // Implementación del nuevo método con contexto
        public ILocationScraper CreateScraperWithContext(string provider, string userId, string ip)
        {
            return provider.ToUpper() switch
            {
                var p when p == _settings.Value.Providers.Detektor.Name.ToUpper()
                    => new DetektorGpsScraper(_fileLogger, _logRepository, _settings, userId, ip),
                _ => throw new NotSupportedException($"Provider {provider} no soportado")
            };
        }
    }
}