using NetTopologySuite.Geometries;

namespace VehicleTracking.Util.Constants
{
    public sealed class GeoPoint
    {
        private static readonly GeometryFactory _geometryFactory = new GeometryFactory(new PrecisionModel(), 4326);

        public static GeoPoint Empty => new GeoPoint(0, 0);
        public double Latitude { get; set; }
        public double Longitude { get; set; }

        public GeoPoint() { }

        public GeoPoint(string point)
        {
            var data = point
                .Replace("POINT(", "")
                .Replace("POINT (", "")
                .Replace(")", "")
                .Split(' ');
            if (data.Length != 2)
                throw new ArgumentException(nameof(point));

            Longitude = double.TryParse(data[0], out var lng) ? lng : 0;
            Latitude = double.TryParse(data[1], out var lat) ? lat : 0;
        }

        public GeoPoint(double latitude, double longitude)
        {
            Latitude = latitude;
            Longitude = longitude;
        }

        public string ToGoogleString() => $"{Latitude},{Longitude}";

        public override string ToString() => $"POINT({Longitude} {Latitude})";

        // Nuevos métodos para trabajar con NetTopologySuite
        public NetTopologySuite.Geometries.Point ToPoint()
        {
            return _geometryFactory.CreatePoint(new Coordinate(Longitude, Latitude));
        }

        public static GeoPoint FromPoint(NetTopologySuite.Geometries.Point point)
        {
            if (point == null) return Empty;
            return new GeoPoint(point.Y, point.X);
        }

        // Operadores implícitos existentes
        public static implicit operator string(GeoPoint point) => point.ToString();
        public static implicit operator GeoPoint(string point) => new GeoPoint(point);

        // Nuevos operadores para NetTopologySuite
        public static implicit operator NetTopologySuite.Geometries.Point(GeoPoint point) => point.ToPoint();
        public static implicit operator GeoPoint(NetTopologySuite.Geometries.Point point) => FromPoint(point);
    }
}
