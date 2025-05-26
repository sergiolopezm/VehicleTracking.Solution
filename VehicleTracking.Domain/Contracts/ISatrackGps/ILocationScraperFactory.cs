namespace VehicleTracking.Domain.Contracts.ISatrackGps
{
    public interface ILocationScraperFactory
    {
        ILocationScraper CreateScraper(string provider);
        ILocationScraper CreateScraperWithContext(string provider, string userId, string ip);
    }
}
