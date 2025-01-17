using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc;
using VehicleTracking.Shared.GeneralDTO;

namespace VehicleTracking.Solution.Api.Attributes
{
    [AttributeUsage(AttributeTargets.Method)]
    public class ValidarModeloAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            if (!context.ModelState.IsValid)
            {
                var errores = context.ModelState
                    .Where(e => e.Value!.Errors.Count > 0)
                    .Select(e => $"{e.Key}: {string.Join(", ", e.Value!.Errors.Select(er => er.ErrorMessage))}")
                    .ToList();

                var resultado = new RespuestaDto
                {
                    Exito = false,
                    Mensaje = "Error de validación",
                    Detalle = string.Join(" | ", errores),
                    Resultado = null
                };

                context.Result = new BadRequestObjectResult(resultado);
            }
        }
        public RespuestaDto ParametrosIncorrectos(ModelStateDictionary modelState)
        {
            return new RespuestaDto()
            {
                Exito = false,
                Mensaje = "Parámetros incorrectos",
                Detalle = string.Join(". ", modelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList()),
                Resultado = null
            };
        }

    }

}
