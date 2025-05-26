namespace VehicleTracking.Shared.InDTO.InDTOGps
{
    public class TrackingSettings
    {
        public ProvidersConfig Providers { get; set; } = null!;
        public SeleniumConfig Selenium { get; set; } = null!;
        public LoggingConfig Logging { get; set; } = null!;
    }

    public class ProvidersConfig
    {
        public ProviderConfig Detektor { get; set; } = null!;
        public ProviderConfig SimonMovilidad { get; set; } = null!;
        public ProviderConfig Satrack { get; set; } = null!;
    }

    public class ProviderConfig
    {
        public string Name { get; set; } = null!;
        public string BaseUrl { get; set; } = null!;
        public int PollingIntervalSeconds { get; set; }
        public int MaxRetryAttempts { get; set; }
        public int TimeoutSeconds { get; set; }
    }

    public class SeleniumConfig
    {
        public string? ChromeDriverPath { get; set; }
        public bool Headless { get; set; }
        public string WindowSize { get; set; } = null!;
        public int ImplicitWaitSeconds { get; set; }
    }

    public class LoggingConfig
    {
        public bool DetailedErrors { get; set; }
        public int RetentionDays { get; set; }
    }
}
