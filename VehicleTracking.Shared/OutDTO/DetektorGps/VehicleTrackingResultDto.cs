namespace VehicleTracking.Shared.OutDTO.DetektorGps
{
    public class VehicleTrackingResultDto
    {
        public string Patent { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public DateTime ProcessedAt { get; set; }
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}
