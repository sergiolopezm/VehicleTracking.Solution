using Microsoft.AspNetCore.Mvc;
using VehicleTracking.Domain.Contracts;
using VehicleTracking.Shared.GeneralDTO;
using VehicleTracking.Shared.InDTO;
using VehicleTracking.Solution.Api.Attributes;

namespace VehicleTracking.Solution.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [ServiceFilter(typeof(LogAttribute))]
    [ProducesResponseType(typeof(RespuestaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RespuestaDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RespuestaDto), StatusCodes.Status401Unauthorized)]
    public class AuthController : ControllerBase
    {
        private readonly IUsuarioRepository _usuarioRepository;
        private readonly ILogRepository _logRepository;

        public AuthController(
            IUsuarioRepository usuarioRepository,
            ILogRepository logRepository)
        {
            _usuarioRepository = usuarioRepository;
            _logRepository = logRepository;
        }

        /// <summary>
        /// Autentica un usuario en el sistema
        /// </summary>
        [HttpPost("login")]
        [ServiceFilter(typeof(AccesoAttribute))]
        [ValidarModelo]
        public IActionResult Login([FromBody] UsuarioLoginDto loginDto)
        {
            try
            {
                loginDto.Ip = HttpContext.Connection.RemoteIpAddress?.ToString();
                var resultado = _usuarioRepository.AutenticarUsuario(loginDto);

                // Registrar intento de login
                _logRepository.Log(
                    loginDto.NombreUsuario,
                    loginDto.Ip,
                    "Login",
                    resultado.Exito ? "Login exitoso" : resultado.Detalle,
                    resultado.Exito ? "200" : "400"
                );

                return StatusCode(resultado.Exito ? 200 : 400, resultado);
            }
            catch (Exception ex)
            {
                _logRepository.Error(
                    loginDto.NombreUsuario,
                    loginDto.Ip,
                    "Login",
                    ex.Message
                );

                return StatusCode(500, RespuestaDto.ErrorInterno());
            }
        }

        /// <summary>
        /// Registra un nuevo usuario en el sistema
        /// </summary>
        [HttpPost("registro")]
        [ServiceFilter(typeof(AccesoAttribute))]
        [ValidarModelo]
        public IActionResult Registro([FromBody] UsuarioRegistroDto registroDto)
        {
            try
            {
                var resultado = _usuarioRepository.RegistrarUsuario(registroDto);

                // Registrar intento de registro
                _logRepository.Log(
                    registroDto.NombreUsuario,
                    HttpContext.Connection.RemoteIpAddress?.ToString(),
                    "Registro",
                    resultado.Exito ? "Registro exitoso" : resultado.Detalle,
                    resultado.Exito ? "200" : "400"
                );

                return StatusCode(resultado.Exito ? 200 : 400, resultado);
            }
            catch (Exception ex)
            {
                _logRepository.Error(
                    registroDto.NombreUsuario,
                    HttpContext.Connection.RemoteIpAddress?.ToString(),
                    "Registro",
                    ex.Message
                );

                return StatusCode(500, RespuestaDto.ErrorInterno());
            }
        }
    }
}