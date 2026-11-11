using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Api.Endpoints;
using Api.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using OpenAI.Chat;
using System.Threading.RateLimiting;
using Xunit;

namespace IngestionApi.Tests;

public sealed class AuthenticationBaselineTests : IClassFixture<AuthenticationTestApplication>
{
    private readonly AuthenticationTestApplication _application;

    public AuthenticationBaselineTests(AuthenticationTestApplication application)
    {
        _application = application;
    }

    [Fact]
    public async Task StudentLogin_ReturnsJwtContainingExistingLocalUserId()
    {
        var login = await _application.LoginAsync(
            AuthenticationTestApplication.StudentEmail,
            AuthenticationTestApplication.StudentPassword);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(login.Token));
        Assert.Equal("student", login.Role);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(login.Token);
        Assert.Equal(AuthenticationTestApplication.StudentUserId, jwt.Subject);
        Assert.Contains(jwt.Claims, claim => claim.Type == "role" && claim.Value == "student");
    }

    [Fact]
    public async Task InstructorLogin_ReturnsJwtContainingExistingLocalUserId()
    {
        var login = await _application.LoginAsync(
            AuthenticationTestApplication.InstructorEmail,
            AuthenticationTestApplication.InstructorPassword);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(login.Token));
        Assert.Equal("instructor", login.Role);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(login.Token);
        Assert.Equal(AuthenticationTestApplication.InstructorUserId, jwt.Subject);
        Assert.Contains(jwt.Claims, claim => claim.Type == "role" && claim.Value == "instructor");
    }

    [Fact]
    public async Task InvalidCredentials_ReturnUnauthorized()
    {
        var login = await _application.LoginAsync(
            AuthenticationTestApplication.StudentEmail,
            "incorrect-password");

        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.Null(login.Token);
    }

    [Fact]
    public async Task ValidJwt_AllowsAccessToMe()
    {
        var login = await _application.LoginAsync(
            AuthenticationTestApplication.StudentEmail,
            AuthenticationTestApplication.StudentPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
        using var response = await _application.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            AuthenticationTestApplication.StudentUserId,
            body.RootElement.GetProperty("userId").GetString());
        Assert.Equal("student", body.RootElement.GetProperty("role").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-valid-jwt")]
    public async Task MissingOrInvalidJwt_CannotAccessMe(string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/me");
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await _application.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

public sealed class AuthenticationTestApplication : IAsyncLifetime
{
    public const string StudentUserId = "existing-student-user-id";
    public const string StudentEmail = "student@example.test";
    public const string StudentPassword = "student-password";
    public const string InstructorUserId = "existing-instructor-user-id";
    public const string InstructorEmail = "instructor@example.test";
    public const string InstructorPassword = "instructor-password";

    private const string JwtSecret = "local-test-only-jwt-secret-at-least-32-characters";
    private const string JwtIssuer = "CasePilot.AuthenticationTests";
    private const string JwtAudience = "CasePilot.AuthenticationTests.Client";

    private WebApplication? _app;

    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var users = new InMemoryUserRepository(
        [
            CreateUser(StudentUserId, StudentEmail, StudentPassword, "Student User", isSuperUser: false),
            CreateUser(InstructorUserId, InstructorEmail, InstructorPassword, "Instructor User", isSuperUser: true)
        ]);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing"
        });
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");

        builder.Services.AddSingleton<IUserRepository>(users);
        builder.Services.AddSingleton(new ChatClient("test-model", "test-api-key"));
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
                    ValidateLifetime = true,
                    ValidIssuer = JwtIssuer,
                    ValidAudience = JwtAudience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret)),
                    ClockSkew = TimeSpan.FromMinutes(1)
                };
            });
        builder.Services.AddAuthorization();
        builder.Services.AddRateLimiter(options =>
        {
            options.AddFixedWindowLimiter("Auth", limiter =>
            {
                limiter.PermitLimit = 100;
                limiter.Window = TimeSpan.FromMinutes(1);
                limiter.QueueLimit = 0;
                limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            });
        });

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseRateLimiter();
        _app.UseAuthorization();
        _app.MapAuthEndpoints(JwtSecret, JwtIssuer, JwtAudience);
        _app.MapDebugEndpoints(
            new DatabaseOptions("sqlite", "Data Source=:memory:", null),
            uploadsRepository: null!,
            sessionsRepository: null!);

        await _app.StartAsync();

        var server = _app.Services.GetRequiredService<IServer>();
        var address = server.Features.Get<IServerAddressesFeature>()?.Addresses.Single()
            ?? throw new InvalidOperationException("The isolated authentication test server did not publish an address.");
        Client = new HttpClient { BaseAddress = new Uri(address) };
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

    public async Task<LoginResult> LoginAsync(string email, string password)
    {
        using var response = await Client.PostAsJsonAsync("/auth/login", new { email, password });
        if (!response.IsSuccessStatusCode)
        {
            return new LoginResult(response.StatusCode, null, null);
        }

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return new LoginResult(
            response.StatusCode,
            body.RootElement.GetProperty("token").GetString(),
            body.RootElement.GetProperty("role").GetString());
    }

    private static UserCredentialRecord CreateUser(
        string id,
        string email,
        string password,
        string fullName,
        bool isSuperUser) =>
        new(id, email, BCrypt.Net.BCrypt.HashPassword(password), fullName, isSuperUser);

    public sealed record LoginResult(HttpStatusCode StatusCode, string? Token, string? Role);

    private sealed class InMemoryUserRepository(IEnumerable<UserCredentialRecord> users) : IUserRepository
    {
        private readonly Dictionary<string, UserCredentialRecord> _usersByEmail = users.ToDictionary(
            user => user.Email,
            StringComparer.OrdinalIgnoreCase);

        public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default) =>
            Task.FromResult(_usersByEmail.ContainsKey(email));

        public Task CreateAsync(NewUser user, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Signup is outside this baseline login test fixture.");

        public Task<UserCredentialRecord?> GetCredentialsByEmailAsync(
            string email,
            CancellationToken cancellationToken = default)
        {
            _usersByEmail.TryGetValue(email, out var user);
            return Task.FromResult(user);
        }

        public Task<UserProfileRecord?> GetProfileByIdAsync(
            string userId,
            CancellationToken cancellationToken = default)
        {
            var user = _usersByEmail.Values.SingleOrDefault(candidate => candidate.Id == userId);
            return Task.FromResult(
                user is null
                    ? null
                    : new UserProfileRecord(user.Id, user.Email, user.FullName, user.IsSuperUser));
        }
    }
}
