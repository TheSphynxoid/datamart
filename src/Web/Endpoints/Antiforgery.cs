using DataMart.Web.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;

namespace DataMart.Web.Endpoints;

/// <summary>
/// Issues an antiforgery token pair so the SPA can echo the request token back in the
/// <c>RequestVerificationToken</c> header on subsequent same-origin POSTs.
///
/// The antiforgery system has two halves:
/// - the <em>cookie token</em> (the secret) stored in the <c>XSRF-TOKEN</c> cookie, and
/// - the <em>request token</em> (derived from the cookie token) that must be sent back
///   on each state-changing request and validated against the cookie.
///
/// The framework's <see cref="IAntiforgery.GetAndStoreTokens"/> sets the cookie via
/// <c>Response.Cookies</c> (which can be HttpOnly — the SPA does not need to read it)
/// and returns the request token in <see cref="AntiforgeryTokenSet.RequestToken"/>. This
/// endpoint returns that request token in the response body so the SPA's
/// <see cref="Web.ClientApp.api-authorization.xsrf.interceptor"/> can cache it and attach
/// it to subsequent requests. Echoing the cookie value as the header would be wrong —
/// it would be the secret token, not the request token — and the framework would reject
/// the request with "The cookie token and the request token were swapped".
///
/// The endpoint is intentionally unauthenticated and has no antiforgery check itself
/// (it's the thing that issues the token, so it has to run before the check).
/// </summary>
public class Antiforgery : IEndpointGroup
{
    public static string? RoutePrefix => "/api/antiforgery";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet("", IssueToken);
    }

    public static AntiforgeryTokenDto IssueToken(IAntiforgery antiforgery, HttpContext httpContext)
    {
        // GetAndStoreTokens mints a new token pair (or reuses the existing cookie if
        // it's still valid) and stores the cookie on the response. The returned
        // token set's RequestToken is what the SPA must echo back in the
        // RequestVerificationToken header on subsequent state-changing requests.
        var tokens = antiforgery.GetAndStoreTokens(httpContext);
        return new AntiforgeryTokenDto(tokens.RequestToken!);
    }
}

public record AntiforgeryTokenDto(string RequestToken);