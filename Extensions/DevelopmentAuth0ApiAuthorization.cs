using System.Security.Claims;
using Api.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Antiforgery;

namespace Api.Extensions;

public sealed record DevelopmentAuth0ApiSettings(bool Enabled)
{
    public static DevelopmentAuth0ApiSettings Load(
        IConfiguration configuration,
        Auth0BffSettings auth0Settings,
        IHostEnvironment environment)
    {
        var raw = Environment.GetEnvironmentVariable("AUTH0_BFF_API_ENABLED")
            ?? configuration["Auth0:BffApiEnabled"]
            ?? "false";
        if (!bool.TryParse(raw, out var enabled))
            throw new InvalidOperationException("AUTH0_BFF_API_ENABLED must be 'true' or 'false'.");

        if (enabled && !auth0Settings.Enabled)
            throw new InvalidOperationException(
                "AUTH0_BFF_API_ENABLED cannot be true when AUTH0_ENABLED is false.");
        if (environment.IsProduction() && auth0Settings.Enabled && !enabled)
            throw new InvalidOperationException(
                "Production Auth0 requires AUTH0_BFF_API_ENABLED=true so BFF sessions can authorize APIs.");

        return new DevelopmentAuth0ApiSettings(enabled);
    }
}

public sealed class DevelopmentAuth0ApiRequirement : IAuthorizationRequirement;

public sealed class DevelopmentAuth0ApiAuthorizationHandler
    : AuthorizationHandler<DevelopmentAuth0ApiRequirement>
{
    private readonly Auth0IdentityResolver _resolver;
    private readonly IUserRepository _users;

    public DevelopmentAuth0ApiAuthorizationHandler(
        Auth0IdentityResolver resolver,
        IUserRepository users)
    {
        _resolver = resolver;
        _users = users;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        DevelopmentAuth0ApiRequirement requirement)
    {
        if (context.Resource is not HttpContext httpContext) return;

        var bearer = await httpContext.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
        if (bearer.Succeeded && bearer.Principal is not null)
        {
            var userId = bearer.Principal.FindFirst("sub")?.Value;
            var profile = string.IsNullOrWhiteSpace(userId)
                ? null
                : await _users.GetProfileByIdAsync(userId, httpContext.RequestAborted);
            if (profile is null || profile.IsDisabled) return;

            httpContext.Items.Remove(Auth0BffDefaults.ApiCookieAuthenticatedItem);
            httpContext.Items.Remove(Auth0BffDefaults.ApiCookiePrincipalItem);
            var role = profile.IsSuperUser ? "instructor" : "student";
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("sub", profile.Id),
                new Claim(ClaimTypes.NameIdentifier, profile.Id),
                new Claim("role", role),
                new Claim("isSuperUser", profile.IsSuperUser ? "true" : "false")
            ], JwtBearerDefaults.AuthenticationScheme));
            httpContext.Items["userId"] = profile.Id;
            httpContext.Items["isSuperUser"] = profile.IsSuperUser;
            context.Succeed(requirement);
            return;
        }

        var cookie = await httpContext.AuthenticateAsync(Auth0BffDefaults.CookieScheme);
        if (!cookie.Succeeded || cookie.Principal is null) return;

        // Re-resolve the validated Auth0 subject and reload the local user on every API call.
        // Cookie-cached local IDs and roles are deliberately not authorization inputs.
        var resolution = await _resolver.ResolveAsync(cookie.Principal, httpContext.RequestAborted);
        if (resolution.Status != Auth0IdentityLinkStatus.Linked) return;

        var isInstructor = string.Equals(resolution.Role, "instructor", StringComparison.Ordinal);
        var localIdentity = new ClaimsIdentity(
        [
            new Claim("sub", resolution.LocalUserId!),
            new Claim(ClaimTypes.NameIdentifier, resolution.LocalUserId!),
            new Claim("role", resolution.Role!),
            new Claim("isSuperUser", isInstructor ? "true" : "false")
        ], Auth0BffDefaults.CookieScheme);

        httpContext.User = new ClaimsPrincipal(localIdentity);
        httpContext.Items["userId"] = resolution.LocalUserId;
        httpContext.Items["isSuperUser"] = isInstructor;
        httpContext.Items[Auth0BffDefaults.ApiCookieAuthenticatedItem] = true;
        httpContext.Items[Auth0BffDefaults.ApiCookiePrincipalItem] = cookie.Principal;
        context.Succeed(requirement);
    }
}

public sealed class DevelopmentAuth0AntiforgeryFilter : IEndpointFilter
{
    private readonly IAntiforgery _antiforgery;
    private readonly Uri _expectedOrigin;

    public DevelopmentAuth0AntiforgeryFilter(
        IAntiforgery antiforgery,
        Auth0BffSettings settings)
    {
        _antiforgery = antiforgery;
        _expectedOrigin = new Uri(settings.PublicOrigin, UriKind.Absolute);
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext invocationContext,
        EndpointFilterDelegate next)
    {
        var context = invocationContext.HttpContext;
        if (context.Items[CasePilotAuthenticationBridgeMiddleware.AntiforgeryValidatedItem] is true)
        {
            return await next(invocationContext);
        }
        if (context.Items[Auth0BffDefaults.ApiCookieAuthenticatedItem] is not true)
        {
            return await next(invocationContext);
        }

        if (!HasTrustedOrigin(context.Request))
        {
            return Results.Json(
                new { error = "invalid request origin" },
                statusCode: StatusCodes.Status403Forbidden);
        }

        if (context.Items[Auth0BffDefaults.ApiCookiePrincipalItem] is not ClaimsPrincipal cookiePrincipal)
        {
            return Results.Forbid();
        }

        var localPrincipal = context.User;
        try
        {
            // Antiforgery tokens are issued for the validated Auth0 cookie principal.
            // Restore that principal only for validation, then continue with the
            // freshly resolved local principal used by the mutation itself.
            context.User = cookiePrincipal;
            await _antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest(new { error = "invalid antiforgery token" });
        }
        finally
        {
            context.User = localPrincipal;
        }

        return await next(invocationContext);
    }

    private bool HasTrustedOrigin(HttpRequest request)
    {
        var value = request.Headers.Origin.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(value))
        {
            value = request.Headers.Referer.FirstOrDefault();
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var supplied)
            && string.Equals(supplied.Scheme, _expectedOrigin.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(supplied.Host, _expectedOrigin.Host, StringComparison.OrdinalIgnoreCase)
            && supplied.Port == _expectedOrigin.Port;
    }
}
