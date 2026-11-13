using Api.Infrastructure;
using Xunit;

namespace IngestionApi.Tests;

public sealed class StagingAuth0TestUserBootstrapperTests
{
    private const string StudentSubject = "auth0|staging-student";
    private const string InstructorSubject = "auth0|staging-instructor";

    [Fact]
    public async Task Bootstrap_creates_fixed_role_disposable_users_and_is_repeatable()
    {
        var users = new MemoryUserRepository();
        var identities = new MemoryIdentityRepository(users);
        var bootstrapper = CreateBootstrapper(users, identities);

        var first = await bootstrapper.BootstrapAsync(EnabledOptions());
        var repeated = await bootstrapper.BootstrapAsync(EnabledOptions());

        Assert.Collection(first,
            student =>
            {
                Assert.Equal("student", student.Role);
                Assert.Equal(StagingAuth0BootstrapStatus.CreatedAndLinked, student.Status);
            },
            instructor =>
            {
                Assert.Equal("instructor", instructor.Role);
                Assert.Equal(StagingAuth0BootstrapStatus.CreatedAndLinked, instructor.Status);
            });
        Assert.All(repeated, result => Assert.Equal(StagingAuth0BootstrapStatus.AlreadyLinked, result.Status));

        var studentId = StagingAuth0TestUserBootstrapper.GetDisposableLocalUserId("student", StudentSubject);
        var instructorId = StagingAuth0TestUserBootstrapper.GetDisposableLocalUserId("instructor", InstructorSubject);
        var student = await users.GetProfileByIdAsync(studentId);
        var instructor = await users.GetProfileByIdAsync(instructorId);
        Assert.NotNull(student);
        Assert.NotNull(instructor);
        Assert.False(student.IsSuperUser);
        Assert.True(instructor.IsSuperUser);
        Assert.EndsWith("@example.invalid", student.Email, StringComparison.Ordinal);
        Assert.EndsWith("@example.invalid", instructor.Email, StringComparison.Ordinal);
        Assert.Equal(studentId, await identities.ResolveLocalUserIdAsync("auth0", StudentSubject));
        Assert.Equal(instructorId, await identities.ResolveLocalUserIdAsync("auth0", InstructorSubject));
    }

    [Fact]
    public async Task Bootstrap_reports_conflicting_subject_without_claiming_existing_account()
    {
        var users = new MemoryUserRepository();
        await users.CreateAsync(new NewUser(
            "unrelated-user", "unrelated@example.invalid", "hash", "Unrelated", false, DateTime.UtcNow));
        var identities = new MemoryIdentityRepository(users);
        Assert.Equal(
            CreateUserIdentityResult.Created,
            await identities.CreateAsync("auth0", StudentSubject, "unrelated-user", DateTime.UtcNow));
        var bootstrapper = CreateBootstrapper(users, identities);

        var results = await bootstrapper.BootstrapAsync(EnabledOptions());

        Assert.Equal(StagingAuth0BootstrapStatus.Conflict, results[0].Status);
        Assert.Equal("unrelated-user", await identities.ResolveLocalUserIdAsync("auth0", StudentSubject));
        Assert.Null(await users.GetProfileByIdAsync(
            StagingAuth0TestUserBootstrapper.GetDisposableLocalUserId("student", StudentSubject)));
    }

    [Theory]
    [InlineData(false, "staging", StagingAuth0BootstrapOptions.RequiredConfirmation, "casepilot_staging")]
    [InlineData(true, "production", StagingAuth0BootstrapOptions.RequiredConfirmation, "casepilot_staging")]
    [InlineData(true, "staging", "wrong-confirmation", "casepilot_staging")]
    [InlineData(true, "staging", StagingAuth0BootstrapOptions.RequiredConfirmation, "casepilot")]
    public async Task Bootstrap_rejects_untrusted_or_nonstaging_execution(
        bool enabled,
        string deploymentEnvironment,
        string confirmation,
        string databaseName)
    {
        var users = new MemoryUserRepository();
        var identities = new MemoryIdentityRepository(users);
        var bootstrapper = CreateBootstrapper(users, identities, databaseName);
        var options = new StagingAuth0BootstrapOptions(
            enabled, deploymentEnvironment, confirmation, StudentSubject, InstructorSubject);

        await Assert.ThrowsAsync<InvalidOperationException>(() => bootstrapper.BootstrapAsync(options));

        Assert.Empty(users.Profiles);
        Assert.Empty(identities.Mappings);
    }

    [Fact]
    public async Task Bootstrap_rejects_same_subject_for_both_roles()
    {
        var users = new MemoryUserRepository();
        var identities = new MemoryIdentityRepository(users);
        var bootstrapper = CreateBootstrapper(users, identities);
        var options = EnabledOptions() with { InstructorSubject = StudentSubject };

        await Assert.ThrowsAsync<InvalidOperationException>(() => bootstrapper.BootstrapAsync(options));

        Assert.Empty(users.Profiles);
        Assert.Empty(identities.Mappings);
    }

    private static StagingAuth0TestUserBootstrapper CreateBootstrapper(
        MemoryUserRepository users,
        MemoryIdentityRepository identities,
        string databaseName = "casepilot_staging") =>
        new(
            new DatabaseOptions(
                "postgres",
                $"Host=staging.invalid;Database={databaseName};Username=not-used;Password=not-used",
                null),
            users,
            identities);

    private static StagingAuth0BootstrapOptions EnabledOptions() => new(
        true,
        "staging",
        StagingAuth0BootstrapOptions.RequiredConfirmation,
        StudentSubject,
        InstructorSubject);

    private sealed class MemoryUserRepository : IUserRepository
    {
        public Dictionary<string, UserProfileRecord> Profiles { get; } = new(StringComparer.Ordinal);

        public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default) =>
            Task.FromResult(Profiles.Values.Any(profile =>
                string.Equals(profile.Email, email, StringComparison.OrdinalIgnoreCase)));

        public Task CreateAsync(NewUser user, CancellationToken cancellationToken = default)
        {
            Profiles.Add(user.Id, new UserProfileRecord(
                user.Id, user.Email, user.FullName ?? "", user.IsSuperUser));
            return Task.CompletedTask;
        }

        public Task<UserCredentialRecord?> GetCredentialsByEmailAsync(
            string email,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<UserProfileRecord?> GetProfileByIdAsync(
            string userId,
            CancellationToken cancellationToken = default)
        {
            Profiles.TryGetValue(userId, out var profile);
            return Task.FromResult(profile);
        }
    }

    private sealed class MemoryIdentityRepository(MemoryUserRepository users) : IUserIdentityRepository
    {
        public Dictionary<(string Provider, string Subject), string> Mappings { get; } = new();

        public Task<string?> ResolveLocalUserIdAsync(
            string provider,
            string providerSubject,
            CancellationToken cancellationToken = default)
        {
            Mappings.TryGetValue((provider.ToLowerInvariant(), providerSubject), out var userId);
            return Task.FromResult(userId);
        }

        public Task<CreateUserIdentityResult> CreateAsync(
            string provider,
            string providerSubject,
            string userId,
            DateTime createdAt,
            CancellationToken cancellationToken = default)
        {
            var key = (provider.ToLowerInvariant(), providerSubject);
            if (Mappings.TryGetValue(key, out var existing))
            {
                return Task.FromResult(string.Equals(existing, userId, StringComparison.Ordinal)
                    ? CreateUserIdentityResult.AlreadyExists
                    : CreateUserIdentityResult.Conflict);
            }
            if (!users.Profiles.ContainsKey(userId))
                return Task.FromResult(CreateUserIdentityResult.UserNotFound);
            Mappings.Add(key, userId);
            return Task.FromResult(CreateUserIdentityResult.Created);
        }
    }
}
