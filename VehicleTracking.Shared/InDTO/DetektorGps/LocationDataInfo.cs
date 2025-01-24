namespace VehicleTracking.Shared.InDTO.DetektorGps
{
    public class LocationDataInfo
    {
        public decimal Latitude { get; set; }
        public decimal Longitude { get; set; }
        public decimal Speed { get; set; }
        public DateTime Timestamp { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string Driver { get; set; } = string.Empty;
        public string Georeference { get; set; } = string.Empty;
        public string InZone { get; set; } = string.Empty;
        public string DetentionTime { get; set; } = string.Empty;
        public decimal DistanceTraveled { get; set; }
        public decimal Temperature { get; set; }
        public decimal Angle { get; set; }
    }
}
