namespace VehicleTracking.Shared.GeneralDTO
{
    public class RespuestaDto
    {

        public bool Exito { get; set; }

        public string? Mensaje { get; set; }

        public string? Detalle { get; set; }

        public object? Resultado { get; set; }


        public static RespuestaDto ErrorInterno()
        {
            return new RespuestaDto()
            {
                Exito = false,
                Mensaje = "Error de servidor",
                Detalle = "No se pudo cumplir con la solicitud, se ha presentado un error.",
                Resultado = null
            };
        }

        public static RespuestaDto ParametrosIncorrectos(string mensaje, string detalle)
        {

            return new RespuestaDto()
            {
                Exito = false,
                Mensaje = mensaje,
                Detalle = detalle,
                Resultado = null
            };
        }

    }
}
