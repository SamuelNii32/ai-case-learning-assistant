using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Extensions;
using Xunit;

namespace IngestionApi.Tests;

public sealed class DevelopmentAuth0WriteAuthorizationTests
    : IClassFixture<DevelopmentAuth0ApiTestApplication>
{
    private const string TrustedOrigin = "https://localhost:7002";
    private readonly DevelopmentAuth0ApiTestApplication _application;

    public DevelopmentAuth0WriteAuthorizationTests(DevelopmentAuth0ApiTestApplication application) =>
        _application = application;

    [Fact]
    public async Task Linked_owner_with_valid_antiforgery_token_can_rename_upload()
    {
        var session = await CreateSessionAsync("auth0|student-a");
        using var response = await RenameWithCookieAsync(
            DevelopmentAuth0ApiTestApplication.StudentAUploadId,
            "Renamed by BFF",
            session);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "Renamed by BFF",
            _application.GetUploadName(DevelopmentAuth0ApiTestApplication.StudentAUploadId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid-antiforgery-token")]
    public async Task Missing_or_invalid_antiforgery_token_is_rejected_before_mutation(string? token)
    {
        var session = await CreateSessionAsync("auth0|student-a");
        var attemptsBefore = _application.RenameAttempts;
        session = session with { RequestToken = token };

        using var response = await RenameWithCookieAsync(
            DevelopmentAuth0ApiTestApplication.StudentAUploadId,
            "Must not be applied",
            session);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(attemptsBefore, _application.RenameAttempts);
    }

    [Fact]
    public async Task Untrusted_origin_is_rejected_before_mutation()
    {
        var session = await CreateSessionAsync("auth0|student-a");
        var attemptsBefore = _application.RenameAttempts;

        using var response = await RenameWithCookieAsync(
            DevelopmentAuth0ApiTestApplication.StudentAUploadId,
            "Must not be applied",
            session,
            origin: "https://evil.example");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(attemptsBefore, _application.RenameAttempts);
    }

    [Theory]
    [InlineData("auth0|unlinked")]
    [InlineData("auth0|invalid-mapping")]
    public async Task Unlinked_or_invalidly_mapped_user_cannot_mutate(string subject)
    {
        var session = await CreateSessionAsync(subject);
        var attemptsBefore = _application.RenameAttempts;

        using var response = await RenameWithCookieAsync(
            DevelopmentAuth0ApiTestApplication.StudentAUploadId,
            "Must not be applied",
            session);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(attemptsBefore, _application.RenameAttempts);
    }

    [Fact]
    public async Task Deleted_local_user_cannot_mutate_with_existing_cookie()
    {
        var session = await CreateSessionAsync("auth0|deletable");
        _application.RemoveUser("deletable");
        var attemptsBefore = _application.RenameAttempts;

        using var response = await RenameWithCookieAsync(
            DevelopmentAuth0ApiTestApplication.StudentAUploadId,
            "Must not be applied",
            session);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(attemptsBefore, _application.RenameAttempts);
    }

    [Fact]
    public async Task Linked_user_cannot_rename_another_users_upload()
    {
        var session = await CreateSessionAsync("auth0|student-a");
        var originalName = _application.GetUploadName(DevelopmentAuth0ApiTestApplication.StudentBUploadId);

        using var response = await RenameWithCookieAsync(
            DevelopmentAuth0ApiTestApplication.StudentBUploadId,
            "Stolen name",
            session,
            spoofedUserId: "student-b");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(
            originalName,
            _application.GetUploadName(DevelopmentAuth0ApiTestApplication.StudentBUploadId));
    }

    [Fact]
    public async Task Migrated_write_endpoints_accept_linked_cookie_with_csrf()
    {
        var session = await CreateSessionAsync("auth0|instructor-a");
        using var request = CreateCookieRequest(HttpMethod.Post, "/classes", session);
        request.Content = JsonContent.Create(new { name = "Not created" });

        using var response = await _application.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Existing_bearer_owner_can_rename_without_antiforgery_token()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            $"/uploads/{DevelopmentAuth0ApiTestApplication.StudentBUploadId}/name");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            _application.CreateJwt("student-b", "student"));
        request.Content = JsonContent.Create(new { name = "Renamed by Bearer" });

        using var response = await _application.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "Renamed by Bearer",
            _application.GetUploadName(DevelopmentAuth0ApiTestApplication.StudentBUploadId));
    }

    [Fact]
    public async Task Global_migration_bridge_accepts_cookie_and_preserves_bearer()
    {
        var session = await CreateSessionAsync("auth0|student-a");
        using var cookieRequest = CreateCookieRequest(HttpMethod.Patch, "/write-disabled", session);
        using var cookieResponse = await _application.Client.SendAsync(cookieRequest);
        Assert.Equal(HttpStatusCode.OK, cookieResponse.StatusCode);

        using var bearerRequest = new HttpRequestMessage(HttpMethod.Patch, "/write-disabled");
        bearerRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            _application.CreateJwt("student-a", "student"));
        using var bearerResponse = await _application.Client.SendAsync(bearerRequest);
        Assert.Equal(HttpStatusCode.OK, bearerResponse.StatusCode);
    }

    [Fact]
    public async Task Anonymous_mutation_is_rejected()
    {
        using var response = await _application.Client.PatchAsJsonAsync(
            $"/uploads/{DevelopmentAuth0ApiTestApplication.StudentAUploadId}/name",
            new { name = "Anonymous" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<BffSession> CreateSessionAsync(string subject)
    {
        var sessionCookie = await _application.CreateCookieAsync(subject);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/bff/csrf");
        request.Headers.Add("Cookie", sessionCookie);
        using var response = await _application.Client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var requestToken = body.GetProperty("requestToken").GetString()!;
        var antiforgeryCookie = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("__Host-CasePilot.Antiforgery=", StringComparison.Ordinal))
            .Split(';', 2)[0];
        return new BffSession(sessionCookie, antiforgeryCookie, requestToken);
    }

    private async Task<HttpResponseMessage> RenameWithCookieAsync(
        Guid uploadId,
        string name,
        BffSession session,
        string origin = TrustedOrigin,
        string? spoofedUserId = null)
    {
        using var request = CreateCookieRequest(
            HttpMethod.Patch,
            $"/uploads/{uploadId}/name",
            session,
            origin);
        if (spoofedUserId is not null) request.Headers.Add("X-User-Id", spoofedUserId);
        request.Content = JsonContent.Create(new { name });
        return await _application.Client.SendAsync(request);
    }

    private static HttpRequestMessage CreateCookieRequest(
        HttpMethod method,
        string path,
        BffSession session,
        string origin = TrustedOrigin)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Cookie", $"{session.SessionCookie}; {session.AntiforgeryCookie}");
        request.Headers.Add("Origin", origin);
        if (!string.IsNullOrWhiteSpace(session.RequestToken))
        {
            request.Headers.Add(Auth0BffDefaults.AntiforgeryHeaderName, session.RequestToken);
        }
        return request;
    }

    private sealed record BffSession(
        string SessionCookie,
        string AntiforgeryCookie,
        string? RequestToken);
}
