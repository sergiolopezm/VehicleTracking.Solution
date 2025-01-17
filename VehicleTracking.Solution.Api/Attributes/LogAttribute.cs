using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc;
using VehicleTracking.Domain.Contracts;
using VehicleTracking.Shared.GeneralDTO;

namespace VehicleTracking.Solution.Api.Attributes
{
    public class LogAttribute : ActionFilterAttribute
    {
        private readonly ILogRepository _log;

        public LogAttribute(ILogRepository log)
        {
            _log = log;
        }

        public override void OnActionExecuted(ActionExecutedContext context)
        {
            var resultado = context.Result as ObjectResult;
            string idUsuario = context.HttpContext.Request.Headers["IdUsuario"].FirstOrDefault()?.Split(" ").Last()!;
            string ip = context.HttpContext.Connection.RemoteIpAddress?.ToString()!;
            string accion = context.HttpContext.Request.Path.Value!;
            string tipo = resultado?.StatusCode.ToString() ?? "500";
            string detalle = "";

            if (resultado?.Value is RespuestaDto respuesta)
            {
                detalle = respuesta.Detalle!;
            }

            _log.Log(idUsuario, ip, accion, detalle, tipo);
        }
    }
}
