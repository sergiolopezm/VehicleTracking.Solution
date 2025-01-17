using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc;
using VehicleTracking.Domain.Contracts;
using VehicleTracking.Shared.GeneralDTO;

namespace VehicleTracking.Solution.Api.Attributes
{
    public class AccesoAttribute : ActionFilterAttribute
    {
        private readonly IAccesoRepository _acceso;

        public AccesoAttribute(IAccesoRepository acceso)
        {
            _acceso = acceso;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            string sitio = context.HttpContext.Request.Headers["Sitio"].FirstOrDefault()?.Split(" ").Last()!;
            string clave = context.HttpContext.Request.Headers["Clave"].FirstOrDefault()?.Split(" ").Last()!;

            if (string.IsNullOrEmpty(sitio) || string.IsNullOrEmpty(clave))
            {
                context.Result = new ObjectResult(RespuestaDto.ParametrosIncorrectos(
                    "Acceso inválido",
                    "No se han enviado credenciales de acceso"))
                {
                    StatusCode = 401
                };
                return;
            }

            if (!_acceso.ValidarAcceso(sitio!, clave!))
            {
                context.Result = new ObjectResult(RespuestaDto.ParametrosIncorrectos(
                    "Acceso inválido",
                    "Las credenciales de acceso son inválidas"))
                {
                    StatusCode = 401
                };
            }
        }
        public bool EsValido()
        {
            return true;
        }
    }
}
