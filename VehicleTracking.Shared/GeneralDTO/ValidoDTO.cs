namespace VehicleTracking.Shared.GeneralDTO
{
    public class ValidoDTO
    {

        public bool EsValido { get; set; }

        public string? Detalle { get; set; }

        public static ValidoDTO Invalido(string detalle)
        {
            return new ValidoDTO
            {
                EsValido = false,
                Detalle = detalle
            };
        }

        public static ValidoDTO Invalido()
        {
            return new ValidoDTO
            {
                EsValido = false,
            };
        }

        public static ValidoDTO Valido()
        {
            return new ValidoDTO
            {
                EsValido = true,
            };
        }

        public static ValidoDTO Valido(string detalle)
        {
            return new ValidoDTO
            {
                EsValido = false,
                Detalle = detalle
            };
        }

        public class ListaPaginadaDto<T>
        {
            public int Pagina { get; set; }
            public int TotalPaginas { get; set; }
            public int TotalRegistros { get; set; }
            public List<T>? Lista { get; set; }
        }

    }
}
