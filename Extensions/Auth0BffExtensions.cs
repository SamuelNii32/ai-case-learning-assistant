using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using Api.Infrastructure;

namespace Api.Extensions;

public static class Auth0BffDefaults
{
    public const string CookieScheme = "CasePilot.Auth0.Cookie";
    public const string OpenIdConnectScheme = "CasePilot.Auth0.OpenIdConnect";
    public const string ApiPolicy = "CasePilot.Auth0.Api";
    public const string AntiforgeryHeaderName = "X-CSRF-TOKEN";
    internal const string ApiCookieAuthenticatedItem = "casepilot.auth0.bff_api_cookie";
    internal const string ApiCookiePrincipalItem = "casepilot.auth0.bff_api_cookie_principal";
}

public static class Auth0BffExtensions
{
    public static IServiceCollection AddDevelopmentAuth0Bff(
        this IServiceCollection services,
        IHostEnvironment environment,
        Auth0BffSettings settings)
        => services.AddDevelopmentAuth0Bff(
            environment,
            settings,
            new DevelopmentAuth0ApiSettings(false));

    public static IServiceCollection AddDevelopmentAuth0Bff(
        this IServiceCollection services,
        IHostEnvironment environment,
        Auth0BffSettings settings,
        DevelopmentAuth0ApiSettings apiSettings)
    {
        if (!settings.Enabled)
        {
            return services;
        }

        services.AddAntiforgery(options =>
        {
            options.HeaderName = Auth0BffDefaults.AntiforgeryHeaderName;
            options.Cookie.Name = "__Host-CasePilot.Antiforgery";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.Path = "/";
        });
        services.AddSingleton<Auth0IdentityResolver>();
        services.TryAddSingleton<Auth0LogoutTicketStore>();
        services.TryAddSingleton(settings);

        if (apiSettings.Enabled)
        {
            services.AddSingleton<IAuthorizationHandler, DevelopmentAuth0ApiAuthorizationHandler>();
            services.AddSingleton<DevelopmentAuth0AntiforgeryFilter>();
            services.AddAuthorization(options =>
            {
                options.AddPolicy(Auth0BffDefaults.ApiPolicy, policy =>
                {
                    policy.AddAuthenticationSchemes(
                        JwtBearerDefaults.AuthenticationScheme,
                        Auth0BffDefaults.CookieScheme);
                    policy.AddRequirements(new DevelopmentAuth0ApiRequirement());
                });
            });
        }

        services.AddAuthentication()
            .AddCookie(Auth0BffDefaults.CookieScheme, options =>
            {
                options.Cookie.Name = "__Host-CasePilot.Auth0";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.Path = "/";
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            })
            .AddOpenIdConnect(Auth0BffDefaults.OpenIdConnectScheme, options =>
            {
                options.SignInScheme = Auth0BffDefaults.CookieScheme;
                options.Authority = settings.Authority.AbsoluteUri;
                options.ClientId = settings.ClientId;
                options.ClientSecret = settings.ClientSecret;
                options.CallbackPath = settings.CallbackPath;
                options.SignedOutCallbackPath = settings.SignedOutCallbackPath;
                options.ResponseType = OpenIdConnectResponseType.Code;
                options.UsePkce = true;
                options.SaveTokens = false;
                options.GetClaimsFromUserInfoEndpoint = false;
                options.MapInboundClaims = false;
                options.RequireHttpsMetadata = true;
                options.ProtocolValidator.RequireNonce = true;
                // The ASP.NET Core OIDC handler protects, restores, and correlates state itself,
                // then clears the raw protocol value before IdentityModel validates the response.
                // Enabling ProtocolValidator.RequireStateValidation would incorrectly validate it twice.

                options.Scope.Clear();
                options.Scope.Add(OpenIdConnectScope.OpenId);
                options.Scope.Add(OpenIdConnectScope.Profile);
                options.Scope.Add(OpenIdConnectScope.Email);

                options.CorrelationCookie.Name = "__Host-CasePilot.Auth0.Correlation.";
                options.CorrelationCookie.HttpOnly = true;
                options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
                options.CorrelationCookie.SameSite = SameSiteMode.None;
                options.CorrelationCookie.Path = "/";

                options.NonceCookie.Name = "__Host-CasePilot.Auth0.Nonce.";
                options.NonceCookie.HttpOnly = true;
                options.NonceCookie.SecurePolicy = CookieSecurePolicy.Always;
                options.NonceCookie.SameSite = SameSiteMode.None;
                options.NonceCookie.Path = "/";

                options.Events.OnRedirectToIdentityProvider = context =>
                {
                    context.ProtocolMessage.RedirectUri = settings.CallbackUrl.AbsoluteUri;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToIdentityProviderForSignOut = context =>
                {
                    // SaveTokens remains disabled, so there is intentionally no
                    // id_token_hint in the browser logout request. Identify the
                    // confidential web application explicitly so Auth0 validates
                    // the redirect against this application's Allowed Logout URLs.
                    context.ProtocolMessage.ClientId = settings.ClientId;
                    context.ProtocolMessage.PostLogoutRedirectUri = settings.SignedOutCallbackUrl.AbsoluteUri;
                    return Task.CompletedTask;
                };
                options.Events.OnTokenValidated = async context =>
                {
                    var resolver = context.HttpContext.RequestServices
                        .GetRequiredService<Auth0IdentityResolver>();
                    context.Properties ??= new AuthenticationProperties();
                    await resolver.ResolveAndStoreAsync(
                        context.Principal!,
                        context.Properties,
                        context.HttpContext.RequestAborted);
                };
            });

        return services;
    }

    public static IEndpointRouteBuilder MapAuth0BffEndpoints(
        this IEndpointRouteBuilder endpoints,
        IHostEnvironment environment,
        Auth0BffSettings settings)
    {
        if (!settings.Enabled)
        {
            return endpoints;
        }

        MapAuth0BffEndpointGroup(endpoints, settings, "/bff");
        if (environment.IsDevelopment())
        {
            MapAuth0BffEndpointGroup(endpoints, settings, "/bff/dev");
        }

        return endpoints;
    }

    private static void MapAuth0BffEndpointGroup(
        IEndpointRouteBuilder endpoints,
        Auth0BffSettings settings,
        string routePrefix)
    {
        var group = endpoints.MapGroup(routePrefix)
            .WithTags("Auth0 BFF");

        group.MapGet("/login", (string? returnUrl) =>
        {
            var properties = new AuthenticationProperties
            {
                RedirectUri = SafeLocalReturnUrl(returnUrl)
            };
            return Results.Challenge(properties, [Auth0BffDefaults.OpenIdConnectScheme]);
        }).AllowAnonymous();

        group.MapGet("/session", async (HttpContext context, Auth0IdentityResolver resolver) =>
        {
            var result = await context.AuthenticateAsync(Auth0BffDefaults.CookieScheme);
            if (!result.Succeeded)
            {
                return Results.Ok(new
                {
                    authenticated = false,
                    accountLinked = false,
                    status = "not_authenticated"
                });
            }

            var resolution = await resolver.ResolveAsync(result.Principal!, context.RequestAborted);
            if (resolution.Status == Auth0IdentityLinkStatus.Linked)
            {
                return Results.Ok(new
                {
                    authenticated = true,
                    accountLinked = true,
                    status = "linked",
                    localUserId = resolution.LocalUserId,
                    role = resolution.Role
                });
            }

            return Results.Ok(new
            {
                authenticated = true,
                accountLinked = false,
                status = resolution.Status switch
                {
                    Auth0IdentityLinkStatus.AccountNotLinked => "account_not_linked",
                    Auth0IdentityLinkStatus.InvalidMapping => "invalid_mapping",
                    Auth0IdentityLinkStatus.AccountDisabled => "account_disabled",
                    _ => "invalid_identity"
                }
            });
        }).AllowAnonymous();

        group.MapPost("/register/student", async (
            HttpContext context,
            [FromServices] IAntiforgery antiforgery,
            [FromServices] Auth0BffSettings auth0,
            [FromServices] Auth0ProvisioningSettings provisioning,
            [FromServices] Auth0ProvisioningService service) =>
        {
            if (!provisioning.StudentRegistrationEnabled) return Results.NotFound();
            var authentication = await context.AuthenticateAsync(Auth0BffDefaults.CookieScheme);
            if (!authentication.Succeeded || authentication.Principal is null) return Results.Unauthorized();

            if (!CasePilotAuthenticationBridgeMiddleware.HasTrustedOrigin(context.Request, auth0.PublicOrigin))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            context.User = authentication.Principal;
            try { await antiforgery.ValidateRequestAsync(context); }
            catch (AntiforgeryValidationException)
            { return Results.BadRequest(new { error = "invalid antiforgery token" }); }

            var principal = authentication.Principal;
            var subject = principal.FindFirst("sub")?.Value;
            var email = principal.FindFirst("email")?.Value?.Trim().ToLowerInvariant();
            var emailVerified = string.Equals(
                principal.FindFirst("email_verified")?.Value,
                "true",
                StringComparison.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(email) || !emailVerified)
                return Results.BadRequest(new { error = "a verified Auth0 email is required" });

            var (result, userId) = await service.CreateStudentAsync(
                subject,
                email,
                principal.FindFirst("name")?.Value,
                context.RequestAborted);
            return result switch
            {
                Auth0ProvisioningResult.Created => Results.Ok(new { status = "created", userId, role = "student" }),
                Auth0ProvisioningResult.AlreadyLinked => Results.Ok(new { status = "already_linked", userId }),
                Auth0ProvisioningResult.ExistingAccountRequiresLinking => Results.Conflict(new { error = "existing_account_requires_linking" }),
                _ => Results.Conflict(new { error = "identity_conflict" })
            };
        }).AllowAnonymous();

        group.MapPost("/link-existing", async (
            HttpContext context,
            [FromServices] IAntiforgery antiforgery,
            [FromServices] Auth0BffSettings auth0,
            [FromServices] Auth0ProvisioningSettings provisioning,
            [FromServices] IUserIdentityRepository identities,
            [FromServices] IUserRepository users) =>
        {
            if (!provisioning.ExistingAccountLinkingEnabled) return Results.NotFound();
            var cookie = await context.AuthenticateAsync(Auth0BffDefaults.CookieScheme);
            var bearer = await context.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
            if (!cookie.Succeeded || cookie.Principal is null || !bearer.Succeeded || bearer.Principal is null)
                return Results.Unauthorized();

            if (!CasePilotAuthenticationBridgeMiddleware.HasTrustedOrigin(context.Request, auth0.PublicOrigin))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            context.User = cookie.Principal;
            try { await antiforgery.ValidateRequestAsync(context); }
            catch (AntiforgeryValidationException)
            { return Results.BadRequest(new { error = "invalid antiforgery token" }); }

            var subject = cookie.Principal.FindFirst("sub")?.Value;
            var localUserId = bearer.Principal.FindFirst("sub")?.Value;
            if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(localUserId))
                return Results.Unauthorized();
            var profile = await users.GetProfileByIdAsync(localUserId, context.RequestAborted);
            if (profile is null || profile.IsDisabled) return Results.Unauthorized();

            var result = await identities.CreateAsync("auth0", subject, localUserId, DateTime.UtcNow, context.RequestAborted);
            return result switch
            {
                CreateUserIdentityResult.Created or CreateUserIdentityResult.AlreadyExists => Results.Ok(new { status = "linked", userId = localUserId }),
                CreateUserIdentityResult.Conflict => Results.Conflict(new { error = "identity_conflict" }),
                _ => Results.Unauthorized()
            };
        }).AllowAnonymous();

        group.MapGet("/csrf", async (HttpContext context, IAntiforgery antiforgery) =>
        {
            var authentication = await context.AuthenticateAsync(Auth0BffDefaults.CookieScheme);
            if (!authentication.Succeeded || authentication.Principal is null)
            {
                return Results.Unauthorized();
            }

            context.User = authentication.Principal;
            var tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Ok(new { requestToken = tokens.RequestToken });
        }).AllowAnonymous();

        group.MapPost("/logout", async (
            HttpContext context,
            IAntiforgery antiforgery,
            string? returnUrl) =>
        {
            var authentication = await context.AuthenticateAsync(Auth0BffDefaults.CookieScheme);
            if (!authentication.Succeeded || authentication.Principal is null)
            {
                return Results.Unauthorized();
            }

            context.User = authentication.Principal;
            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                return Results.BadRequest(new { error = "invalid antiforgery token" });
            }

            var properties = new AuthenticationProperties
            {
                RedirectUri = SafeLocalReturnUrl(returnUrl)
            };
            return Results.SignOut(
                properties,
                [Auth0BffDefaults.CookieScheme, Auth0BffDefaults.OpenIdConnectScheme]);
        }).AllowAnonymous();

        group.MapPost("/logout/prepare", async (
            HttpContext context,
            IAntiforgery antiforgery,
            Auth0LogoutTicketStore tickets,
            string? returnUrl) =>
        {
            var authentication = await context.AuthenticateAsync(Auth0BffDefaults.CookieScheme);
            if (!authentication.Succeeded || authentication.Principal is null)
            {
                return Results.Unauthorized();
            }

            if (context.Items[CasePilotAuthenticationBridgeMiddleware.AntiforgeryValidatedItem] is not true)
            {
                context.User = authentication.Principal;
                try
                {
                    await antiforgery.ValidateRequestAsync(context);
                }
                catch (AntiforgeryValidationException)
                {
                    return Results.BadRequest(new { error = "invalid antiforgery token" });
                }
            }

            var subject = authentication.Principal.FindFirst("sub")?.Value;
            if (string.IsNullOrWhiteSpace(subject)) return Results.Unauthorized();

            var ticket = tickets.Create(subject, SafeLocalReturnUrl(returnUrl));
            return Results.Ok(new
            {
                logoutUrl = $"{routePrefix}/logout/complete?ticket={Uri.EscapeDataString(ticket)}"
            });
        }).AllowAnonymous();

        group.MapGet("/logout/complete", async (
            HttpContext context,
            Auth0LogoutTicketStore tickets,
            string? ticket) =>
        {
            var authentication = await context.AuthenticateAsync(Auth0BffDefaults.CookieScheme);
            var subject = authentication.Principal?.FindFirst("sub")?.Value;
            if (!authentication.Succeeded || string.IsNullOrWhiteSpace(subject)
                || !tickets.TryConsume(ticket, subject, out var returnUrl))
            {
                return Results.BadRequest(new { error = "invalid or expired logout request" });
            }

            return Results.SignOut(
                new AuthenticationProperties { RedirectUri = returnUrl },
                [Auth0BffDefaults.CookieScheme, Auth0BffDefaults.OpenIdConnectScheme]);
        }).AllowAnonymous();

    }

    [Obsolete("Use MapAuth0BffEndpoints. The /bff/dev alias is available only in Development.")]
    public static IEndpointRouteBuilder MapDevelopmentAuth0BffEndpoints(
        this IEndpointRouteBuilder endpoints,
        IHostEnvironment environment,
        Auth0BffSettings settings) => endpoints.MapAuth0BffEndpoints(environment, settings);

    public static bool IsDevelopmentAuth0BffEnabled(
        this IHostEnvironment environment,
        Auth0BffSettings settings) => settings.Enabled;

    public static bool IsDevelopmentAuth0ApiEnabled(
        this IHostEnvironment environment,
        Auth0BffSettings settings,
        DevelopmentAuth0ApiSettings apiSettings) =>
        settings.Enabled && apiSettings.Enabled;

    public static RouteHandlerBuilder RequireDevelopmentAuth0ApiOrBearer(
        this RouteHandlerBuilder endpoint,
        bool developmentAuth0ApiEnabled) =>
        developmentAuth0ApiEnabled
            ? endpoint.RequireAuthorization(Auth0BffDefaults.ApiPolicy)
            : endpoint.RequireAuthorization();

    public static RouteHandlerBuilder RequireDevelopmentAuth0WriteOrBearer(
        this RouteHandlerBuilder endpoint,
        bool developmentAuth0ApiEnabled)
    {
        endpoint.RequireDevelopmentAuth0ApiOrBearer(developmentAuth0ApiEnabled);
        if (developmentAuth0ApiEnabled)
        {
            endpoint.AddEndpointFilter<DevelopmentAuth0AntiforgeryFilter>();
        }

        return endpoint;
    }

    private static string SafeLocalReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl)
            || !returnUrl.StartsWith("/", StringComparison.Ordinal)
            || returnUrl.StartsWith("//", StringComparison.Ordinal)
            || returnUrl.Contains('\\')
            || returnUrl.Any(char.IsControl))
        {
            return "/";
        }

        return returnUrl;
    }
}

public sealed class Auth0LogoutTicketStore
{
    private readonly ConcurrentDictionary<string, LogoutTicket> _tickets = new(StringComparer.Ordinal);

    public string Create(string subject, string returnUrl)
    {
        RemoveExpired();
        var value = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        _tickets[value] = new LogoutTicket(subject, returnUrl, DateTimeOffset.UtcNow.AddMinutes(2));
        return value;
    }

    public bool TryConsume(string? value, string subject, out string returnUrl)
    {
        returnUrl = "/";
        if (string.IsNullOrWhiteSpace(value) || !_tickets.TryRemove(value, out var ticket)) return false;
        if (ticket.ExpiresAt <= DateTimeOffset.UtcNow
            || !string.Equals(ticket.Subject, subject, StringComparison.Ordinal)) return false;
        returnUrl = ticket.ReturnUrl;
        return true;
    }

    private void RemoveExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in _tickets)
        {
            if (pair.Value.ExpiresAt <= now) _tickets.TryRemove(pair.Key, out _);
        }
    }

    private sealed record LogoutTicket(string Subject, string ReturnUrl, DateTimeOffset ExpiresAt);
}
