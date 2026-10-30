using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Api.Extensions;
using Api.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace IngestionApi.Tests;

public sealed class Auth0BffIntegrationTests : IClassFixture<Auth0BffTestApplication>
{
    private readonly Auth0BffTestApplication _application;

    public Auth0BffIntegrationTests(Auth0BffTestApplication application)
    {
        _application = application;
    }

    [Fact]
    public async Task Login_starts_authorization_code_with_pkce_and_secure_protocol_cookies()
    {
        using var response = await _application.Client.GetAsync("/bff/login?returnUrl=%2Fsafe");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = Assert.IsType<Uri>(response.Headers.Location);
        Assert.Equal("https", location.Scheme);
        Assert.Equal("auth0-test.example", location.Host);
        Assert.Equal("/authorize", location.AbsolutePath);

        var query = System.Web.HttpUtility.ParseQueryString(location.Query);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("https://localhost:7002/signin-auth0", query["redirect_uri"]);
        Assert.False(string.IsNullOrWhiteSpace(query["code_challenge"]));
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.False(string.IsNullOrWhiteSpace(query["state"]));
        Assert.False(string.IsNullOrWhiteSpace(query["nonce"]));
        Assert.DoesNotContain("development-client-secret", location.OriginalString, StringComparison.Ordinal);
        Assert.DoesNotContain("evil.example", location.OriginalString, StringComparison.OrdinalIgnoreCase);

        var protocolCookies = response.Headers.GetValues("Set-Cookie").ToArray();
        Assert.Contains(protocolCookies, value => IsSecureProtocolCookie(value, "Correlation"));
        Assert.Contains(protocolCookies, value => IsSecureProtocolCookie(value, "Nonce"));
    }

    [Fact]
    public void Explicit_auth0_schemes_do_not_replace_bearer_defaults()
    {
        var options = _application.Services.GetRequiredService<IOptions<AuthenticationOptions>>().Value;

        Assert.Equal(JwtBearerDefaults.AuthenticationScheme, options.DefaultScheme);
        Assert.Null(options.DefaultAuthenticateScheme);
        Assert.Null(options.DefaultChallengeScheme);
    }

    [Fact]
    public void Cookie_and_oidc_options_apply_session_and_protocol_security()
    {
        var cookie = _application.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(Auth0BffDefaults.CookieScheme);
        Assert.Equal("__Host-CasePilot.Auth0", cookie.Cookie.Name);
        Assert.True(cookie.Cookie.HttpOnly);
        Assert.Equal(CookieSecurePolicy.Always, cookie.Cookie.SecurePolicy);
        Assert.Equal(SameSiteMode.Lax, cookie.Cookie.SameSite);

        var oidc = _application.Services
            .GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(Auth0BffDefaults.OpenIdConnectScheme);
        Assert.Equal("/signin-auth0", oidc.CallbackPath);
        Assert.Equal("/signout-callback-auth0", oidc.SignedOutCallbackPath);
        Assert.Equal(OpenIdConnectResponseType.Code, oidc.ResponseType);
        Assert.True(oidc.UsePkce);
        Assert.False(oidc.SaveTokens);
        Assert.True(oidc.ProtocolValidator.RequireNonce);
        Assert.False(oidc.ProtocolValidator.RequireStateValidation);
    }

    [Fact]
    public async Task Form_post_callback_with_returned_state_completes_cookie_sign_in()
    {
        using var loginResponse = await _application.Client.GetAsync("/bff/login?returnUrl=%2Fsafe");
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);
        var query = System.Web.HttpUtility.ParseQueryString(loginResponse.Headers.Location!.Query);
        var state = query["state"];
        var nonce = query["nonce"];
        Assert.Equal(OpenIdConnectResponseMode.FormPost, query["response_mode"]);
        Assert.False(string.IsNullOrWhiteSpace(state));
        Assert.False(string.IsNullOrWhiteSpace(nonce));
        _application.SetCallbackNonce(nonce!);

        var protocolCookies = string.Join(
            "; ",
            loginResponse.Headers.GetValues("Set-Cookie").Select(value => value.Split(';', 2)[0]));
        using var callbackRequest = new HttpRequestMessage(HttpMethod.Post, "/signin-auth0")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = "disposable-test-code",
                ["state"] = state!
            })
        };
        callbackRequest.Headers.Add("Cookie", protocolCookies);

        using var callbackResponse = await _application.Client.SendAsync(callbackRequest);

        Assert.Equal(HttpStatusCode.Redirect, callbackResponse.StatusCode);
        Assert.Equal("/safe", callbackResponse.Headers.Location?.OriginalString);
        var sessionCookie = callbackResponse.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("__Host-CasePilot.Auth0=", StringComparison.Ordinal))
            .Split(';', 2)[0];
        using var sessionRequest = new HttpRequestMessage(HttpMethod.Get, "/bff/session");
        sessionRequest.Headers.Add("Cookie", sessionCookie);
        using var sessionResponse = await _application.Client.SendAsync(sessionRequest);
        var session = await sessionResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(session.GetProperty("authenticated").GetBoolean());
        Assert.True(session.GetProperty("accountLinked").GetBoolean());
        Assert.Equal("linked", session.GetProperty("status").GetString());
        Assert.Equal("local-student-id", session.GetProperty("localUserId").GetString());
        Assert.Equal("student", session.GetProperty("role").GetString());
    }

    [Fact]
    public async Task Mapped_identity_reports_local_user_and_cannot_access_jwt_api()
    {
        var cookie = await _application.CreateAuth0CookieAsync();

        using var sessionRequest = new HttpRequestMessage(HttpMethod.Get, "/bff/session");
        sessionRequest.Headers.Add("Cookie", cookie);
        using var sessionResponse = await _application.Client.SendAsync(sessionRequest);
        Assert.Equal(HttpStatusCode.OK, sessionResponse.StatusCode);

        var bodyText = await sessionResponse.Content.ReadAsStringAsync();
        var status = JsonDocument.Parse(bodyText).RootElement;
        Assert.True(status.GetProperty("authenticated").GetBoolean());
        Assert.True(status.GetProperty("accountLinked").GetBoolean());
        Assert.Equal("linked", status.GetProperty("status").GetString());
        Assert.Equal("local-student-id", status.GetProperty("localUserId").GetString());
        Assert.Equal("student", status.GetProperty("role").GetString());
        Assert.DoesNotContain("auth0|disposable-test-user", bodyText, StringComparison.Ordinal);
        Assert.DoesNotContain("token", bodyText, StringComparison.OrdinalIgnoreCase);

        using var jwtRequest = new HttpRequestMessage(HttpMethod.Get, "/_test/jwt-only");
        jwtRequest.Headers.Add("Cookie", cookie);
        using var jwtResponse = await _application.Client.SendAsync(jwtRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, jwtResponse.StatusCode);
    }

    [Fact]
    public async Task Unmapped_identity_reports_account_not_linked_without_local_identity()
    {
        var cookie = await _application.CreateAuth0CookieAsync("auth0|unmapped-test-user");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/bff/session");
        request.Headers.Add("Cookie", cookie);
        using var response = await _application.Client.SendAsync(request);
        var bodyText = await response.Content.ReadAsStringAsync();
        var status = JsonDocument.Parse(bodyText).RootElement;

        Assert.True(status.GetProperty("authenticated").GetBoolean());
        Assert.False(status.GetProperty("accountLinked").GetBoolean());
        Assert.Equal("account_not_linked", status.GetProperty("status").GetString());
        Assert.False(status.TryGetProperty("localUserId", out _));
        Assert.False(status.TryGetProperty("role", out _));
        Assert.DoesNotContain("auth0|unmapped-test-user", bodyText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mapping_to_missing_local_user_reports_invalid_mapping()
    {
        var cookie = await _application.CreateAuth0CookieAsync("auth0|invalid-mapping");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/bff/session");
        request.Headers.Add("Cookie", cookie);
        using var response = await _application.Client.SendAsync(request);
        var status = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(status.GetProperty("authenticated").GetBoolean());
        Assert.False(status.GetProperty("accountLinked").GetBoolean());
        Assert.Equal("invalid_mapping", status.GetProperty("status").GetString());
        Assert.False(status.TryGetProperty("localUserId", out _));
        Assert.False(status.TryGetProperty("role", out _));
    }

    [Fact]
    public async Task Session_cookie_is_secure_http_only_and_same_site_lax()
    {
        using var response = await _application.Client.GetAsync("/_test/sign-in");
        var sessionCookie = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("__Host-CasePilot.Auth0=", StringComparison.Ordinal));

        Assert.Contains("secure", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("development-client-secret", sessionCookie, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Logout_rejects_requests_without_antiforgery_token()
    {
        var cookie = await _application.CreateAuth0CookieAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/logout");
        request.Headers.Add("Cookie", cookie);

        using var response = await _application.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Logout_with_antiforgery_token_clears_cookie_and_starts_oidc_logout()
    {
        var sessionCookie = await _application.CreateAuth0CookieAsync();
        using var csrfRequest = new HttpRequestMessage(HttpMethod.Get, "/bff/csrf");
        csrfRequest.Headers.Add("Cookie", sessionCookie);
        using var csrfResponse = await _application.Client.SendAsync(csrfRequest);
        csrfResponse.EnsureSuccessStatusCode();
        var csrfBody = await csrfResponse.Content.ReadFromJsonAsync<JsonElement>();
        var requestToken = csrfBody.GetProperty("requestToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(requestToken));
        var antiforgeryCookie = csrfResponse.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("__Host-CasePilot.Antiforgery=", StringComparison.Ordinal))
            .Split(';', 2)[0];

        using var logoutRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/bff/logout?returnUrl=https%3A%2F%2Fevil.example");
        logoutRequest.Headers.Add("Cookie", $"{sessionCookie}; {antiforgeryCookie}");
        logoutRequest.Headers.Add(Auth0BffDefaults.AntiforgeryHeaderName, requestToken);
        using var logoutResponse = await _application.Client.SendAsync(logoutRequest);

        Assert.Equal(HttpStatusCode.Redirect, logoutResponse.StatusCode);
        Assert.Equal("auth0-test.example", logoutResponse.Headers.Location?.Host);
        var logoutQuery = System.Web.HttpUtility.ParseQueryString(
            logoutResponse.Headers.Location?.Query ?? "");
        Assert.Equal("development-client-id", logoutQuery["client_id"]);
        Assert.Equal(
            "https://localhost:7002/signout-callback-auth0",
            logoutQuery["post_logout_redirect_uri"]);
        Assert.Null(logoutQuery["id_token_hint"]);
        Assert.DoesNotContain(
            "evil.example",
            logoutResponse.Headers.Location?.OriginalString ?? "",
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            logoutResponse.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("__Host-CasePilot.Auth0=", StringComparison.Ordinal)
                     && value.Contains("expires=", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Disabled_feature_does_not_register_bff_endpoints()
    {
        await using var disabled = await Auth0BffTestApplication.StartAsync(enabled: false);

        using var response = await disabled.Client.GetAsync("/bff/session");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static bool IsSecureProtocolCookie(string value, string purpose) =>
        value.Contains(purpose, StringComparison.Ordinal)
        && value.Contains("secure", StringComparison.OrdinalIgnoreCase)
        && value.Contains("httponly", StringComparison.OrdinalIgnoreCase)
        && value.Contains("samesite=none", StringComparison.OrdinalIgnoreCase);
}

public sealed class Auth0BffTestApplication : IAsyncLifetime, IAsyncDisposable
{
    private const string JwtSecret = "auth0-bff-tests-only-jwt-secret-at-least-32-characters";
    private WebApplication? _app;
    private readonly SymmetricSecurityKey _oidcSigningKey = new(Encoding.UTF8.GetBytes(
        "auth0-bff-callback-tests-only-signing-key-32-chars"))
    {
        KeyId = "auth0-bff-test-key"
    };
    private string? _callbackNonce;

    public HttpClient Client { get; private set; } = null!;
    public IServiceProvider Services => _app?.Services
        ?? throw new InvalidOperationException("The test application has not started.");

    public Task InitializeAsync() => StartInternalAsync(enabled: true);

    public static async Task<Auth0BffTestApplication> StartAsync(bool enabled)
    {
        var application = new Auth0BffTestApplication();
        await application.StartInternalAsync(enabled);
        return application;
    }

    private async Task StartInternalAsync(bool enabled)
    {
        var settings = new Auth0BffSettings(
            enabled,
            "auth0-test.example",
            "development-client-id",
            "development-client-secret",
            "/signin-auth0",
            "/signout-callback-auth0",
            "https://localhost:7002");

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<IUserIdentityRepository>(new TestUserIdentityRepository());
        builder.Services.AddSingleton<IUserRepository>(new TestUserRepository());
        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = "CasePilot.Auth0BffTests",
                    ValidAudience = "CasePilot.Auth0BffTests.Client",
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret))
                };
            });
        builder.Services.AddAuthorization();
        builder.Services.AddDevelopmentAuth0Bff(builder.Environment, settings);
        if (enabled)
        {
            builder.Services.PostConfigure<OpenIdConnectOptions>(
                Auth0BffDefaults.OpenIdConnectScheme,
                options =>
                {
                    var configuration = new OpenIdConnectConfiguration
                    {
                        Issuer = "https://auth0-test.example/",
                        AuthorizationEndpoint = "https://auth0-test.example/authorize",
                        TokenEndpoint = "https://auth0-test.example/oauth/token",
                        EndSessionEndpoint = "https://auth0-test.example/oidc/logout"
                    };
                    configuration.SigningKeys.Add(_oidcSigningKey);
                    options.Configuration = configuration;
                    options.ConfigurationManager =
                        new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                    options.Events.OnAuthorizationCodeReceived = context =>
                    {
                        context.HandleCodeRedemption(
                            "test-only-access-token",
                            CreateTestIdToken(_callbackNonce
                                ?? throw new InvalidOperationException("The callback nonce was not captured.")));
                        return Task.CompletedTask;
                    };
                });
        }

        _app = builder.Build();
        // Kestrel listens on an ephemeral HTTP loopback port in this isolated test.
        // Model the HTTPS reverse-proxy request that the production pipeline sees.
        _app.Use((context, next) =>
        {
            context.Request.Scheme = Uri.UriSchemeHttps;
            return next();
        });
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapAuth0BffEndpoints(_app.Environment, settings);
        _app.MapGet("/_test/jwt-only", () => Results.Ok()).RequireAuthorization();
        if (enabled)
        {
            _app.MapGet("/_test/sign-in", async (
                HttpContext context,
                Auth0IdentityResolver resolver,
                string? subject) =>
            {
                subject = string.IsNullOrWhiteSpace(subject)
                    ? "auth0|disposable-test-user"
                    : subject;
                var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, subject),
                    new Claim("sub", subject)
                ],
                Auth0BffDefaults.CookieScheme);
                var principal = new ClaimsPrincipal(identity);
                var properties = new AuthenticationProperties();
                await resolver.ResolveAndStoreAsync(principal, properties, context.RequestAborted);
                await context.SignInAsync(
                    Auth0BffDefaults.CookieScheme,
                    principal,
                    properties);
                return Results.NoContent();
            }).AllowAnonymous();
        }

        await _app.StartAsync();
        var server = _app.Services.GetRequiredService<IServer>();
        var address = server.Features.Get<IServerAddressesFeature>()?.Addresses.Single()
            ?? throw new InvalidOperationException("The Auth0 BFF test server did not publish an address.");
        Client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(address)
        };
    }

    public async Task<string> CreateAuth0CookieAsync(string? subject = null)
    {
        var path = subject is null
            ? "/_test/sign-in"
            : $"/_test/sign-in?subject={Uri.EscapeDataString(subject)}";
        using var response = await Client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        var setCookie = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("__Host-CasePilot.Auth0=", StringComparison.Ordinal));
        return setCookie.Split(';', 2)[0];
    }

    public void SetCallbackNonce(string nonce)
    {
        _callbackNonce = nonce;
    }

    private string CreateTestIdToken(string nonce)
    {
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: "https://auth0-test.example/",
            audience: "development-client-id",
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, "auth0|disposable-test-user"),
                new Claim(JwtRegisteredClaimNames.Nonce, nonce),
                new Claim(
                    JwtRegisteredClaimNames.Iat,
                    new DateTimeOffset(now).ToUnixTimeSeconds().ToString(),
                    ClaimValueTypes.Integer64)
            ],
            notBefore: now.AddMinutes(-1),
            expires: now.AddMinutes(5),
            signingCredentials: new SigningCredentials(_oidcSigningKey, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    async ValueTask IAsyncDisposable.DisposeAsync() => await DisposeAsync();

    private sealed class TestUserIdentityRepository : IUserIdentityRepository
    {
        private static readonly IReadOnlyDictionary<string, string> Mappings =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["auth0|disposable-test-user"] = "local-student-id",
                ["auth0|invalid-mapping"] = "missing-local-user-id"
            };

        public Task<string?> ResolveLocalUserIdAsync(
            string provider,
            string providerSubject,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                string.Equals(provider, "auth0", StringComparison.Ordinal)
                    && Mappings.TryGetValue(providerSubject, out var userId)
                        ? userId
                        : null);

        public Task<CreateUserIdentityResult> CreateAsync(
            string provider,
            string providerSubject,
            string userId,
            DateTime createdAt,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Identity creation is not allowed in Auth0 BFF tests.");
    }

    private sealed class TestUserRepository : IUserRepository
    {
        private static readonly UserProfileRecord Student = new(
            "local-student-id",
            "student@example.test",
            "Mapped Student",
            false);

        public Task<UserProfileRecord?> GetProfileByIdAsync(
            string userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<UserProfileRecord?>(
                string.Equals(userId, Student.Id, StringComparison.Ordinal) ? Student : null);

        public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Email lookup is not allowed during Auth0 identity resolution.");

        public Task CreateAsync(NewUser user, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("User creation is not allowed during Auth0 identity resolution.");

        public Task<UserCredentialRecord?> GetCredentialsByEmailAsync(
            string email,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Credential lookup is not allowed during Auth0 identity resolution.");
    }
}
