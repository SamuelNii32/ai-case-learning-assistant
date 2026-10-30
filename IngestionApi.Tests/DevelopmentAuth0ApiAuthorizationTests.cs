using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Api.Extensions;
using Api.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace IngestionApi.Tests;

public sealed class DevelopmentAuth0ApiAuthorizationTests : IClassFixture<DevelopmentAuth0ApiTestApplication>
{
    private readonly DevelopmentAuth0ApiTestApplication _application;

    public DevelopmentAuth0ApiAuthorizationTests(DevelopmentAuth0ApiTestApplication application) =>
        _application = application;

    [Fact]
    public async Task Linked_students_can_access_only_their_current_local_profile()
    {
        var studentA = await _application.CreateCookieAsync("auth0|student-a");
        var studentB = await _application.CreateCookieAsync("auth0|student-b");

        var a = await GetMeAsync(studentA, "?userId=student-b", "student-b");
        var b = await GetMeAsync(studentB);

        Assert.Equal("student-a", a.GetProperty("userId").GetString());
        Assert.Equal("student-b", b.GetProperty("userId").GetString());
        Assert.Equal("student", a.GetProperty("role").GetString());
    }

    [Fact]
    public async Task Selected_read_endpoints_return_only_the_linked_students_records()
    {
        var studentA = await _application.CreateCookieAsync("auth0|student-a");
        var studentB = await _application.CreateCookieAsync("auth0|student-b");

        foreach (var path in new[] { "/uploads/mine", "/sessions/mine", "/classes/enrolled" })
        {
            var a = await GetItemsAsync(path, studentA);
            var b = await GetItemsAsync(path, studentB);
            Assert.All(a, item => Assert.StartsWith("student-a-", item.GetProperty("id").GetString()));
            Assert.All(b, item => Assert.StartsWith("student-b-", item.GetProperty("id").GetString()));
            Assert.DoesNotContain(a, item => item.GetProperty("id").GetString()!.StartsWith("student-b-"));
        }
    }

    [Fact]
    public async Task Instructor_cannot_select_another_instructors_profile()
    {
        var cookie = await _application.CreateCookieAsync("auth0|instructor-a");
        var me = await GetMeAsync(cookie, "?userId=instructor-b", "instructor-b");

        Assert.Equal("instructor-a", me.GetProperty("userId").GetString());
        Assert.Equal("instructor", me.GetProperty("role").GetString());
    }

    [Fact]
    public async Task Linked_instructors_receive_only_their_own_selected_read_data()
    {
        var instructorA = await _application.CreateCookieAsync("auth0|instructor-a");
        var instructorB = await _application.CreateCookieAsync("auth0|instructor-b");

        var a = await GetItemsAsync("/uploads/mine", instructorA);
        var b = await GetItemsAsync("/uploads/mine", instructorB);

        Assert.Equal("instructor-a-upload", Assert.Single(a).GetProperty("id").GetString());
        Assert.Equal("instructor-b-upload", Assert.Single(b).GetProperty("id").GetString());
    }

    [Theory]
    [InlineData("auth0|unlinked")]
    [InlineData("auth0|invalid-mapping")]
    public async Task Unlinked_or_invalid_mappings_are_denied(string subject)
    {
        var cookie = await _application.CreateCookieAsync(subject);
        using var response = await SendCookieAsync("/me", cookie);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Deleted_local_user_is_denied_even_when_cookie_was_issued_while_valid()
    {
        var cookie = await _application.CreateCookieAsync("auth0|deletable");
        _application.RemoveUser("deletable");

        using var response = await SendCookieAsync("/me", cookie);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Current_local_role_is_reloaded_instead_of_using_cookie_role()
    {
        var cookie = await _application.CreateCookieAsync("auth0|student-a");
        _application.SetRole("student-a", isInstructor: true);
        try
        {
            var me = await GetMeAsync(cookie);
            Assert.Equal("instructor", me.GetProperty("role").GetString());
        }
        finally
        {
            _application.SetRole("student-a", isInstructor: false);
        }
    }

    [Fact]
    public async Task Anonymous_user_is_unauthorized()
    {
        using var response = await _application.Client.GetAsync("/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Existing_bearer_access_is_preserved_and_cookie_reaches_migrated_api_groups()
    {
        using var bearerRequest = new HttpRequestMessage(HttpMethod.Get, "/me");
        bearerRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", _application.CreateJwt("student-b", "student"));
        using var bearerResponse = await _application.Client.SendAsync(bearerRequest);
        Assert.Equal(HttpStatusCode.OK, bearerResponse.StatusCode);

        var cookie = await _application.CreateCookieAsync("auth0|student-a");
        using var jwtOnly = await SendCookieAsync("/bearer-only", cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, jwtOnly.StatusCode);

        using var document = await SendCookieAsync(
            "/uploads/11111111-1111-1111-1111-111111111111.pdf", cookie);
        Assert.Equal(HttpStatusCode.OK, document.StatusCode);

        using var instructorClasses = await SendCookieAsync("/classes/mine", cookie);
        Assert.Equal(HttpStatusCode.OK, instructorClasses.StatusCode);
    }

    [Fact]
    public async Task Suspended_local_user_is_denied_for_cookie_and_existing_bearer()
    {
        var cookie = await _application.CreateCookieAsync("auth0|student-a");
        _application.SetDisabled("student-a", true);
        try
        {
            using var cookieResponse = await SendCookieAsync("/me", cookie);
            Assert.Equal(HttpStatusCode.Forbidden, cookieResponse.StatusCode);

            using var bearerRequest = new HttpRequestMessage(HttpMethod.Get, "/me");
            bearerRequest.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer", _application.CreateJwt("student-a", "student"));
            using var bearerResponse = await _application.Client.SendAsync(bearerRequest);
            Assert.Equal(HttpStatusCode.Forbidden, bearerResponse.StatusCode);
        }
        finally
        {
            _application.SetDisabled("student-a", false);
        }
    }

    [Fact]
    public void Api_opt_in_can_be_explicitly_enabled_outside_development()
    {
        var production = new TestHostEnvironment { EnvironmentName = Environments.Production };
        var bff = DevelopmentAuth0ApiTestApplication.BffSettings;
        Assert.True(production.IsDevelopmentAuth0ApiEnabled(bff, new DevelopmentAuth0ApiSettings(true)));
    }

    [Fact]
    public void Api_opt_in_is_disabled_unless_separately_enabled()
    {
        var development = new TestHostEnvironment { EnvironmentName = Environments.Development };
        var bff = DevelopmentAuth0ApiTestApplication.BffSettings;

        Assert.False(development.IsDevelopmentAuth0ApiEnabled(
            bff, new DevelopmentAuth0ApiSettings(false)));
        Assert.True(development.IsDevelopmentAuth0ApiEnabled(
            bff, new DevelopmentAuth0ApiSettings(true)));
    }

    private async Task<JsonElement> GetMeAsync(
        string cookie,
        string suffix = "",
        string? spoofedUserId = null)
    {
        using var response = await SendCookieAsync("/me" + suffix, cookie, spoofedUserId);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<JsonElement.ArrayEnumerator> GetItemsAsync(string path, string cookie)
    {
        using var response = await SendCookieAsync(path, cookie);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("items").EnumerateArray();
    }

    private async Task<HttpResponseMessage> SendCookieAsync(
        string path,
        string cookie,
        string? spoofedUserId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Cookie", cookie);
        if (spoofedUserId is not null) request.Headers.Add("X-User-Id", spoofedUserId);
        return await _application.Client.SendAsync(request);
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}

public sealed class DevelopmentAuth0ApiTestApplication : IAsyncLifetime
{
    private const string JwtSecret = "task-nine-tests-only-jwt-secret-at-least-32-characters";
    private WebApplication? _app;
    private readonly MutableUserRepository _users = new();
    private readonly TestUploadNames _uploadNames = new();

    public static readonly Guid StudentAUploadId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public static readonly Guid StudentBUploadId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    public static Auth0BffSettings BffSettings { get; } = new(
        true, "auth0-test.example", "development-client-id", "development-client-secret",
        "/signin-auth0", "/signout-callback-auth0", "https://localhost:7002");

    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<IUserIdentityRepository>(new TestIdentityRepository());
        builder.Services.AddSingleton<IUserRepository>(_users);
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = "CasePilot.Task9Tests",
                    ValidAudience = "CasePilot.Task9Tests.Client",
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret))
                };
            });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton(new DevelopmentAuth0ApiSettings(true));
        builder.Services.AddDevelopmentAuth0Bff(
            builder.Environment, BffSettings, new DevelopmentAuth0ApiSettings(true));

        _app = builder.Build();
        _app.Use((context, next) =>
        {
            context.Request.Scheme = Uri.UriSchemeHttps;
            return next();
        });
        _app.UseAuthentication();
        _app.UseMiddleware<CasePilotAuthenticationBridgeMiddleware>();
        _app.UseAuthorization();
        _app.MapAuth0BffEndpoints(builder.Environment, BffSettings);

        _app.MapGet("/_test/sign-in", async (
            HttpContext context,
            Auth0IdentityResolver resolver,
            string subject) =>
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("sub", subject),
                new Claim(ClaimTypes.NameIdentifier, subject)
            ], Auth0BffDefaults.CookieScheme));
            var properties = new AuthenticationProperties();
            await resolver.ResolveAndStoreAsync(principal, properties, context.RequestAborted);
            await context.SignInAsync(Auth0BffDefaults.CookieScheme, principal, properties);
            return Results.NoContent();
        }).AllowAnonymous();

        _app.MapGet("/me", async (HttpContext context, IUserRepository users) =>
        {
            var userId = context.GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userId)) return Results.Unauthorized();
            var profile = await users.GetProfileByIdAsync(userId, context.RequestAborted);
            if (profile is null) return Results.Unauthorized();
            return Results.Ok(new
            {
                userId,
                profile.Email,
                profile.FullName,
                role = profile.IsSuperUser ? "instructor" : "student"
            });
        }).RequireAuthorization(Auth0BffDefaults.ApiPolicy);

        foreach (var path in new[] { "/uploads/mine", "/sessions/mine", "/classes/enrolled" })
        {
            _app.MapGet(path, (HttpContext context) => Results.Ok(new
            {
                items = TestReadData.ForUser(context.GetCurrentUserId()!)
            })).RequireAuthorization(Auth0BffDefaults.ApiPolicy);
        }

        _app.MapGet("/bearer-only", () => Results.Ok()).RequireAuthorization(
            new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme });
        _app.MapGet("/uploads/{uploadId:guid}.pdf", () => Results.Ok()).RequireAuthorization();
        _app.MapGet("/classes/mine", () => Results.Ok()).RequireAuthorization();
        _app.MapPost("/classes", () => Results.Ok()).RequireAuthorization();

        _app.MapPatch("/uploads/{uploadId:guid}/name", (
            Guid uploadId,
            TestRenameRequest input,
            HttpContext context) =>
        {
            var userId = context.GetCurrentUserId() ?? "";
            return _uploadNames.TryRename(uploadId, userId, input.Name)
                ? Results.Ok(new { uploadId, name = input.Name })
                : Results.NotFound(new { error = "not_found" });
        }).RequireDevelopmentAuth0WriteOrBearer(developmentAuth0ApiEnabled: true);

        _app.MapPatch("/write-disabled", () => Results.Ok())
            .RequireDevelopmentAuth0WriteOrBearer(developmentAuth0ApiEnabled: false);

        await _app.StartAsync();
        var address = _app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        Client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(address)
        };
    }

    public async Task<string> CreateCookieAsync(string subject)
    {
        using var response = await Client.GetAsync($"/_test/sign-in?subject={Uri.EscapeDataString(subject)}");
        response.EnsureSuccessStatusCode();
        return response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("__Host-CasePilot.Auth0=", StringComparison.Ordinal))
            .Split(';', 2)[0];
    }

    public string CreateJwt(string userId, string role)
    {
        var token = new JwtSecurityToken(
            issuer: "CasePilot.Task9Tests",
            audience: "CasePilot.Task9Tests.Client",
            claims: [new Claim("sub", userId), new Claim("role", role)],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret)),
                SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public void SetRole(string userId, bool isInstructor) => _users.SetRole(userId, isInstructor);
    public void SetDisabled(string userId, bool disabled) => _users.SetDisabled(userId, disabled);
    public void RemoveUser(string userId) => _users.Remove(userId);
    public string GetUploadName(Guid uploadId) => _uploadNames.GetName(uploadId);
    public int RenameAttempts => _uploadNames.Attempts;

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private sealed class TestIdentityRepository : IUserIdentityRepository
    {
        private static readonly IReadOnlyDictionary<string, string> Mappings =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["auth0|student-a"] = "student-a",
                ["auth0|student-b"] = "student-b",
                ["auth0|instructor-a"] = "instructor-a",
                ["auth0|instructor-b"] = "instructor-b",
                ["auth0|deletable"] = "deletable",
                ["auth0|invalid-mapping"] = "missing-user"
            };

        public Task<string?> ResolveLocalUserIdAsync(string provider, string providerSubject, CancellationToken cancellationToken = default) =>
            Task.FromResult(string.Equals(provider, "auth0", StringComparison.Ordinal)
                && Mappings.TryGetValue(providerSubject, out var id) ? id : null);

        public Task<CreateUserIdentityResult> CreateAsync(string provider, string providerSubject, string userId, DateTime createdAt, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Identity creation is not part of Task 9 tests.");
    }

    private sealed class MutableUserRepository : IUserRepository
    {
        private readonly Dictionary<string, UserProfileRecord> _profiles = new(StringComparer.Ordinal)
        {
            ["student-a"] = Profile("student-a", false),
            ["student-b"] = Profile("student-b", false),
            ["instructor-a"] = Profile("instructor-a", true),
            ["instructor-b"] = Profile("instructor-b", true),
            ["deletable"] = Profile("deletable", false)
        };

        public void SetRole(string id, bool isInstructor)
        {
            lock (_profiles)
            {
                var current = _profiles[id];
                _profiles[id] = current with { IsSuperUser = isInstructor };
            }
        }

        public void Remove(string id) { lock (_profiles) _profiles.Remove(id); }

        public void SetDisabled(string id, bool disabled)
        {
            lock (_profiles)
            {
                var current = _profiles[id];
                _profiles[id] = current with { IsDisabled = disabled };
            }
        }

        public Task<UserProfileRecord?> GetProfileByIdAsync(string userId, CancellationToken cancellationToken = default)
        {
            lock (_profiles)
                return Task.FromResult(_profiles.TryGetValue(userId, out var profile) ? profile : null);
        }

        public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CreateAsync(NewUser user, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<UserCredentialRecord?> GetCredentialsByEmailAsync(string email, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        private static UserProfileRecord Profile(string id, bool instructor) =>
            new(id, $"{id}@example.test", id, instructor);
    }

    private static class TestReadData
    {
        public static object[] ForUser(string userId) =>
        [new { id = $"{userId}-upload" }];
    }

    private sealed record TestRenameRequest(string Name);

    private sealed class TestUploadNames
    {
        private readonly Dictionary<Guid, (string Owner, string Name)> _uploads = new()
        {
            [StudentAUploadId] = ("student-a", "Student A upload"),
            [StudentBUploadId] = ("student-b", "Student B upload")
        };

        public int Attempts { get; private set; }

        public bool TryRename(Guid uploadId, string userId, string name)
        {
            Attempts++;
            if (!_uploads.TryGetValue(uploadId, out var upload)
                || !string.Equals(upload.Owner, userId, StringComparison.Ordinal))
            {
                return false;
            }

            _uploads[uploadId] = (upload.Owner, name);
            return true;
        }

        public string GetName(Guid uploadId) => _uploads[uploadId].Name;
    }
}
