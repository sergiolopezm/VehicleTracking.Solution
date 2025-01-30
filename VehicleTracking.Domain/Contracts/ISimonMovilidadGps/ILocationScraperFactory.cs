namespace VehicleTracking.Domain.Contracts.ISimonMovilidadGps
{
    public interface ILocationScraperFactory
    {
        ILocationScraper CreateScraper(string provider);
        ILocationScraper CreateScraperWithContext(string provider, string userId, string ip);
    }
}
