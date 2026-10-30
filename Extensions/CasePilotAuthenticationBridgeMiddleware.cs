using System.Security.Claims;
using Api.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Api.Extensions;

public sealed class CasePilotAuthenticationBridgeMiddleware
{
    internal const string AntiforgeryValidatedItem = "casepilot.auth0.antiforgery_validated";
    private readonly RequestDelegate _next;

    public CasePilotAuthenticationBridgeMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(
        HttpContext context,
        IUserRepository users,
        Auth0BffSettings auth0Settings,
        DevelopmentAuth0ApiSettings apiSettings)
    {
        var bearer = await context.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
        if (bearer.Succeeded && bearer.Principal is not null)
        {
            var userId = bearer.Principal.FindFirst("sub")?.Value;
            var profile = string.IsNullOrWhiteSpace(userId)
                ? null
                : await users.GetProfileByIdAsync(userId, context.RequestAborted);

            if (profile is not null && !profile.IsDisabled)
            {
                SetLocalPrincipal(context, profile, JwtBearerDefaults.AuthenticationScheme);
            }
            else
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity());
            }

            await _next(context);
            return;
        }

        if (!auth0Settings.Enabled || !apiSettings.Enabled)
        {
            await _next(context);
            return;
        }

        var cookie = await context.AuthenticateAsync(Auth0BffDefaults.CookieScheme);
        if (!cookie.Succeeded || cookie.Principal is null)
        {
            await _next(context);
            return;
        }

        var resolver = context.RequestServices.GetRequiredService<Auth0IdentityResolver>();
        var resolution = await resolver.ResolveAsync(cookie.Principal, context.RequestAborted);
        if (resolution.Status != Auth0IdentityLinkStatus.Linked)
        {
            await _next(context);
            return;
        }

        var profileForCookie = await users.GetProfileByIdAsync(
            resolution.LocalUserId!,
            context.RequestAborted);
        if (profileForCookie is null || profileForCookie.IsDisabled)
        {
            await _next(context);
            return;
        }

        SetLocalPrincipal(context, profileForCookie, Auth0BffDefaults.CookieScheme);
        context.Items[Auth0BffDefaults.ApiCookieAuthenticatedItem] = true;
        context.Items[Auth0BffDefaults.ApiCookiePrincipalItem] = cookie.Principal;

        if (RequiresAntiforgery(context.Request))
        {
            if (!HasTrustedOrigin(context.Request, auth0Settings.PublicOrigin))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { error = "invalid request origin" });
                return;
            }

            var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
            var localPrincipal = context.User;
            try
            {
                context.User = cookie.Principal;
                await antiforgery.ValidateRequestAsync(context);
                context.Items[AntiforgeryValidatedItem] = true;
            }
            catch (AntiforgeryValidationException)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { error = "invalid antiforgery token" });
                return;
            }
            finally
            {
                context.User = localPrincipal;
            }
        }

        await _next(context);
    }

    private static void SetLocalPrincipal(HttpContext context, UserProfileRecord profile, string scheme)
    {
        var role = profile.IsSuperUser ? "instructor" : "student";
        var identity = new ClaimsIdentity(
        [
            new Claim("sub", profile.Id),
            new Claim(ClaimTypes.NameIdentifier, profile.Id),
            new Claim("role", role),
            new Claim("isSuperUser", profile.IsSuperUser ? "true" : "false")
        ], scheme);

        context.User = new ClaimsPrincipal(identity);
        context.Items["userId"] = profile.Id;
        context.Items["isSuperUser"] = profile.IsSuperUser;
    }

    private static bool IsMutation(string method) =>
        HttpMethods.IsPost(method) || HttpMethods.IsPut(method) ||
        HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);

    private static bool RequiresAntiforgery(HttpRequest request)
    {
        if (IsMutation(request.Method)) return true;
        if (!HttpMethods.IsGet(request.Method)) return false;

        var path = request.Path.Value ?? "";
        return path.StartsWith("/ask/", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/api/chat/stream", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/tutor/reading/resume/", StringComparison.OrdinalIgnoreCase)
            || (path.StartsWith("/api/documents/", StringComparison.OrdinalIgnoreCase)
                && path.EndsWith("/figures", StringComparison.OrdinalIgnoreCase))
            || (path.StartsWith("/classes/", StringComparison.OrdinalIgnoreCase)
                && path.EndsWith("/join-code", StringComparison.OrdinalIgnoreCase));
    }

    internal static bool HasTrustedOrigin(HttpRequest request, string expectedOrigin)
    {
        if (!Uri.TryCreate(expectedOrigin, UriKind.Absolute, out var expected)) return false;
        var value = request.Headers.Origin.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(value)) value = request.Headers.Referer.FirstOrDefault();

        return Uri.TryCreate(value, UriKind.Absolute, out var supplied)
            && string.Equals(supplied.Scheme, expected.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(supplied.Host, expected.Host, StringComparison.OrdinalIgnoreCase)
            && supplied.Port == expected.Port;
    }
}
