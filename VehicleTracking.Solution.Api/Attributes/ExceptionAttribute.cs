using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc;
using VehicleTracking.Shared.GeneralDTO;

namespace VehicleTracking.Solution.Api.Attributes
{
    public class ExcepcionAttribute : Attribute, IExceptionFilter
    {
        public void OnException(ExceptionContext context)
        {
            context.Result = new ObjectResult(RespuestaDto.ErrorInterno())
            {
                StatusCode = 500,
            };
        }
    }
}
