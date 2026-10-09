using DataMart.Application.Common.Exceptions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace DataMart.Web.Infrastructure;

/// <summary>
/// Converts well-known application exceptions into RFC 9110-compliant <see cref="ProblemDetails"/> responses,
/// mapping <see cref="AppValidationException"/> → 400, <see cref="NotFoundException"/> → 404,
/// <see cref="UnauthorizedAccessException"/> → 401, <see cref="ForbiddenAccessException"/> → 403,
/// and antiforgery failures (thrown as <see cref="BadHttpRequestException"/> wrapping
/// <see cref="AntiforgeryValidationException"/>) → 400 with a stable <c>Type</c> marker so the
/// SPA's <c>XsrfInterceptor</c> can recognise a stale token and re-prime instead of failing.
/// Unrecognised exceptions are not handled and fall through to the default middleware.
/// </summary>
public class ProblemDetailsExceptionHandler : IExceptionHandler
{
    /// <summary>Stable marker the SPA matches to detect a stale antiforgery token.</summary>
    public const string AntiforgeryProblemType = "https://datamart/antiforgery";

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (statusCode, problemDetails) = exception switch
        {
            AppValidationException ve => (StatusCodes.Status400BadRequest, (ProblemDetails)new ValidationProblemDetails(ve.Errors)
            {
                Status = StatusCodes.Status400BadRequest,
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1"
            }),
            BadHttpRequestException bre when IsAntiforgeryFailure(bre) => (StatusCodes.Status400BadRequest, (ProblemDetails)new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Type = AntiforgeryProblemType,
                Title = "Invalid anti-forgery token.",
                Detail = "The page holds a stale token (e.g. the server restarted). Refresh and retry; the app recovers automatically."
            }),
            NotFoundException ne => (StatusCodes.Status404NotFound, new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.5",
                Title = "The specified resource was not found.",
                Detail = ne.Message
            }),
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Unauthorized",
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.2"
            }),
            ForbiddenAccessException => (StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Forbidden",
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.4"
            }),
            _ => (-1, null)
        };

        if (problemDetails is null) return false;

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);
        return true;
    }

    private static bool IsAntiforgeryFailure(BadHttpRequestException bre) =>
        bre.InnerException is AntiforgeryValidationException
        || bre.Message.Contains("anti-forgery", StringComparison.OrdinalIgnoreCase);
}
