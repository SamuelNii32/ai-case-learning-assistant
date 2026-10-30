using System.Security.Claims;
using Api.Infrastructure;
using Microsoft.AspNetCore.Authentication;

namespace Api.Extensions;

public enum Auth0IdentityLinkStatus
{
    Linked,
    AccountNotLinked,
    InvalidMapping,
    InvalidIdentity,
    AccountDisabled
}

public sealed record Auth0IdentityResolution(
    Auth0IdentityLinkStatus Status,
    string? LocalUserId = null,
    string? Role = null);

public sealed class Auth0IdentityResolver
{
    private const string Provider = "auth0";
    private const string StatusItem = "casepilot.auth0.link_status";
    private const string UserIdItem = "casepilot.auth0.local_user_id";
    private const string RoleItem = "casepilot.auth0.local_role";

    private readonly IUserIdentityRepository _identities;
    private readonly IUserRepository _users;

    public Auth0IdentityResolver(IUserIdentityRepository identities, IUserRepository users)
    {
        _identities = identities;
        _users = users;
    }

    public async Task<Auth0IdentityResolution> ResolveAndStoreAsync(
        ClaimsPrincipal principal,
        AuthenticationProperties properties,
        CancellationToken cancellationToken = default)
    {
        var resolution = await ResolveAsync(principal, cancellationToken);
        properties.Items[StatusItem] = resolution.Status.ToString();

        if (resolution.Status == Auth0IdentityLinkStatus.Linked)
        {
            properties.Items[UserIdItem] = resolution.LocalUserId!;
            properties.Items[RoleItem] = resolution.Role!;
        }
        else
        {
            properties.Items.Remove(UserIdItem);
            properties.Items.Remove(RoleItem);
        }

        return resolution;
    }

    public async Task<Auth0IdentityResolution> ResolveAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        var providerSubject = principal.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(providerSubject))
        {
            return new Auth0IdentityResolution(Auth0IdentityLinkStatus.InvalidIdentity);
        }

        var localUserId = await _identities.ResolveLocalUserIdAsync(
            Provider,
            providerSubject,
            cancellationToken);
        if (localUserId is null)
        {
            return new Auth0IdentityResolution(Auth0IdentityLinkStatus.AccountNotLinked);
        }

        var localUser = await _users.GetProfileByIdAsync(localUserId, cancellationToken);
        if (localUser is null)
        {
            return new Auth0IdentityResolution(Auth0IdentityLinkStatus.InvalidMapping);
        }

        if (localUser.IsDisabled)
        {
            return new Auth0IdentityResolution(Auth0IdentityLinkStatus.AccountDisabled);
        }

        return new Auth0IdentityResolution(
            Auth0IdentityLinkStatus.Linked,
            localUser.Id,
            localUser.IsSuperUser ? "instructor" : "student");
    }

    public static Auth0IdentityResolution ReadStored(AuthenticationProperties? properties)
    {
        if (properties?.Items.TryGetValue(StatusItem, out var statusText) != true
            || !Enum.TryParse<Auth0IdentityLinkStatus>(statusText, out var status))
        {
            return new Auth0IdentityResolution(Auth0IdentityLinkStatus.InvalidIdentity);
        }

        if (status != Auth0IdentityLinkStatus.Linked)
        {
            return new Auth0IdentityResolution(status);
        }

        if (!properties.Items.TryGetValue(UserIdItem, out var localUserId)
            || string.IsNullOrWhiteSpace(localUserId)
            || !properties.Items.TryGetValue(RoleItem, out var role)
            || role is not ("student" or "instructor"))
        {
            return new Auth0IdentityResolution(Auth0IdentityLinkStatus.InvalidMapping);
        }

        return new Auth0IdentityResolution(status, localUserId, role);
    }
}
