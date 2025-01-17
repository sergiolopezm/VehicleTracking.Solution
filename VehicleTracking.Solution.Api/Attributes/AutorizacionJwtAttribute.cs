using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using VehicleTracking.Domain.Contracts;
using VehicleTracking.Shared.GeneralDTO;

namespace VehicleTracking.Solution.Api.Attributes
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class AutorizacionJwtAttribute : Attribute, IActionFilter
    {
        private readonly ITokenRepository _tokenRepository;

        public AutorizacionJwtAttribute(ITokenRepository tokenRepository)
        {
            _tokenRepository = tokenRepository;
        }

        public void OnActionExecuting(ActionExecutingContext context)
        {
            string idToken = context.HttpContext.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last()!;
            string idUsuario = context.HttpContext.Request.Headers["IdUsuario"].FirstOrDefault()?.Split(" ").Last()!;
            string ip = context.HttpContext.Connection.RemoteIpAddress?.ToString()!;

            var valido = _tokenRepository.EsValido(idToken!, idUsuario!, ip!);

            if (!valido.EsValido)
            {
                context.Result = new ObjectResult(RespuestaDto.ParametrosIncorrectos("Sesión inválida", valido.Detalle!))
                {
                    StatusCode = 401
                };
            }
        }

        public void OnActionExecuted(ActionExecutedContext context)
        {
            if (context.HttpContext.Response.StatusCode == 200)
            {
                string idToken = context.HttpContext.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last()!;
                _tokenRepository.AumentarTiempoExpiracion(idToken!);
            }
        }
    }
}