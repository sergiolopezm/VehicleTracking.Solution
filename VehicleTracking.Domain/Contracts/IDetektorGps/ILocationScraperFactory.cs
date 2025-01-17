namespace VehicleTracking.Domain.Contracts.IDetektorGps
{
    public interface ILocationScraperFactory
    {
        ILocationScraper CreateScraper(string provider);

        ILocationScraper CreateScraperWithContext(string provider, string userId, string ip);
    }
}

