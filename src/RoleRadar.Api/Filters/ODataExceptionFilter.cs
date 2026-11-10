namespace RoleRadar.Api.Filters;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.OData;
using RoleRadar.Api.Problems;

/// <summary>
/// A rejected OData query is a 400 RFC 9457 problem document with the error code <c>validation_error</c> and the parser's message
/// as <c>detail</c> (feature 014, ADR 0044: one error format for the typed web client, in place of OData's native error JSON).
/// </summary>
public sealed class ODataExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is ODataException odataEx)
        {
            context.Result = new ProblemActionResult(ApiProblems.Validation(odataEx.Message));
            context.ExceptionHandled = true;
        }
    }

    private sealed class ProblemActionResult(IResult result) : IActionResult
    {
        public Task ExecuteResultAsync(ActionContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            return result.ExecuteAsync(context.HttpContext);
        }
    }
}
