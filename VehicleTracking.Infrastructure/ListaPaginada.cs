namespace VehicleTracking.Infrastructure
{
    public class ListaPaginada<T> where T : class
    {

        public int pagina { get; set; }

        public int totalPaginas { get; set; }

        public int totalRegistros { get; set; }

        public List<T>? lista { get; set; }

    }
}
