  using Microsoft.Extensions.Options;
using VehicleTracking.Domain.Contracts.ISimonMovilidadGps;
using VehicleTracking.Domain.Scraping.SimonMovilidadGps;
using VehicleTracking.Shared.InDTO.InDTOGps;
using VehicleTracking.Util.Helpers;

namespace VehicleTracking.Domain.Services.SimonMovilidadGps
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

        public ILocationScraper CreateScraper(string provider)
        {
            return CreateScraperWithContext(provider, "SYSTEM", "SYSTEM");
        }

        public ILocationScraper CreateScraperWithContext(string provider, string userId, string ip)
        {
            return provider.ToUpper() switch
            {
                var p when p == _settings.Value.Providers.SimonMovilidad.Name.ToUpper()
                    => new SimonMovilidadGpsScraper(_fileLogger, _logRepository, _settings, userId, ip),
                _ => throw new NotSupportedException($"Provider {provider} no soportado")
            };
        }
    }
}
