namespace RoleRadar.Api.Filters;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.OData;
using RoleRadar.Api.Envelopes;

public sealed class ODataExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is ODataException odataEx)
        {
            var envelope = ErrorEnvelope.Validation(odataEx.Message);
            context.Result = new ObjectResult(envelope)
            {
                StatusCode = StatusCodes.Status400BadRequest
            };
            context.ExceptionHandled = true;
        }
    }
}
