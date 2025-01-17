using Microsoft.AspNetCore.Mvc;
using VehicleTracking.Domain.Contracts.IDetektorGps;
using VehicleTracking.Solution.Api.Attributes;
using VehicleTracking.Shared.GeneralDTO;
using VehicleTracking.Domain.Contracts;

namespace VehicleTracking.Solution.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [ServiceFilter(typeof(LogAttribute))]
    [ServiceFilter(typeof(AutorizacionJwtAttribute))]
    [ServiceFilter(typeof(AccesoAttribute))]
    public class TrackingController : ControllerBase
    {
        private readonly ITrackingService _trackingService;
        private readonly ILogRepository _logRepository;

        public TrackingController(
            ITrackingService trackingService,
            ILogRepository logRepository)
        {
            _trackingService = trackingService;
            _logRepository = logRepository;
        }

        /// <summary>
        /// Inicia el proceso de tracking de vehículos
        /// </summary>
        [HttpPost("track")]
        [ServiceFilter(typeof(LogAttribute))]
        [ServiceFilter(typeof(AutorizacionJwtAttribute))]
        [ServiceFilter(typeof(AccesoAttribute))]
        [ProducesResponseType(typeof(RespuestaDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(RespuestaDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(RespuestaDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(RespuestaDto), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> TrackVehicles()
        {
            try
            {
                string idUsuario = HttpContext.Request.Headers["IdUsuario"].FirstOrDefault()?.Split(" ").Last()!;
                string ip = HttpContext.Connection.RemoteIpAddress?.ToString()!;

                var results = await _trackingService.TrackVehiclesAsync(idUsuario, ip);

                _logRepository.Accion(
                    idUsuario,
                    ip,
                    "TrackVehicles",
                    $"Proceso de tracking completado: {results.totalRegistros} vehículos procesados"
                );

                // Preparar estadísticas para el detalle
                var exitosos = results.lista?.Count(r => r.Success) ?? 0;
                var fallidos = results.totalRegistros - exitosos;
                var detalleResumen = $"Se procesaron {results.totalRegistros} vehículos en total. " +
                                   $"Exitosos: {exitosos}, Con errores: {fallidos}";

                return Ok(new RespuestaDto
                {
                    Exito = true,
                    Mensaje = "Proceso de tracking",
                    Detalle = detalleResumen,
                    Resultado = new
                    {
                        estadisticas = new
                        {
                            totalProcesados = results.totalRegistros,
                            procesadosExitosamente = exitosos,
                            procesadosConError = fallidos
                        },
                        resultados = results
                    }
                });
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("SERVIDOR_CAIDO:"))
            {
                string idUsuario = HttpContext.Request.Headers["IdUsuario"].FirstOrDefault()?.Split(" ").Last()!;
                string ip = HttpContext.Connection.RemoteIpAddress?.ToString()!;

                _logRepository.Error(
                    idUsuario,
                    ip,
                    "TrackVehicles",
                    $"Error de conectividad con el servidor: {ex.Message}"
                );

                return StatusCode(503, new RespuestaDto
                {
                    Exito = false,
                    Mensaje = "Error de conectividad",
                    Detalle = "No se pudo establecer conexión con el servidor de tracking. Por favor, intente nuevamente más tarde.",
                    Resultado = null
                });
            }
            catch (Exception ex)
            {
                string idUsuario = HttpContext.Request.Headers["IdUsuario"].FirstOrDefault()?.Split(" ").Last()!;
                string ip = HttpContext.Connection.RemoteIpAddress?.ToString()!;

                _logRepository.Error(
                    idUsuario,
                    ip,
                    "TrackVehicles",
                    ex.Message
                );

                // Determinar si es un error conocido que podemos mostrar al usuario
                var esErrorConocido = ex.Message.Contains("CONFIGURACION_INVALIDA:") ||
                                     ex.Message.Contains("Error de autenticación:") ||
                                     ex.Message.Contains("Error de validación:");

                var mensajeError = esErrorConocido
                    ? ex.Message.Split(":").Last().Trim()
                    : "Se ha producido un error inesperado durante el proceso de tracking.";

                return StatusCode(500, new RespuestaDto
                {
                    Exito = false,
                    Mensaje = "Error en proceso de tracking",
                    Detalle = mensajeError,
                    Resultado = null
                });
            }
        }

        /// <summary>
        /// Obtiene el estado actual de un vehículo específico
        /// </summary>
        [HttpGet("vehicle/{patent}")]
        public async Task<IActionResult> GetVehicleStatus(string patent)
        {
            try
            {
                string idUsuario = HttpContext.Request.Headers["IdUsuario"].FirstOrDefault()?.Split(" ").Last()!;
                string ip = HttpContext.Connection.RemoteIpAddress?.ToString()!;

                var vehicle = await _trackingService.GetVehicleStatusAsync(patent, idUsuario, ip);
                if (vehicle == null)
                {
                    return NotFound(new RespuestaDto
                    {
                        Exito = false,
                        Mensaje = "Vehículo no encontrado",
                        Detalle = $"No se encontró información para el vehículo con placa {patent}",
                        Resultado = null
                    });
                }

                return Ok(new RespuestaDto
                {
                    Exito = true,
                    Mensaje = "Información de vehículo",
                    Detalle = $"Información obtenida exitosamente para el vehículo {patent}",
                    Resultado = vehicle
                });
            }
            catch (Exception ex)
            {
                string idUsuario = HttpContext.Request.Headers["IdUsuario"].FirstOrDefault()?.Split(" ").Last()!;
                string ip = HttpContext.Connection.RemoteIpAddress?.ToString()!;

                _logRepository.Error(
                    idUsuario,
                    ip,
                    $"GetVehicleStatus - {patent}",
                    ex.Message
                );

                return StatusCode(500, RespuestaDto.ErrorInterno());
            }
        }
    }
}