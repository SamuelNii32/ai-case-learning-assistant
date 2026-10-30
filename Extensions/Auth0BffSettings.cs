using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Api.Extensions;

public sealed record Auth0BffSettings(
    bool Enabled,
    string Domain,
    string ClientId,
    string ClientSecret,
    string CallbackPath,
    string SignedOutCallbackPath,
    string PublicOrigin)
{
    public const string DefaultCallbackPath = "/signin-auth0";
    public const string DefaultSignedOutCallbackPath = "/signout-callback-auth0";

    public Uri Authority => new($"https://{Domain.TrimEnd('/')}/", UriKind.Absolute);
    public Uri CallbackUrl => BuildPublicUrl(CallbackPath);
    public Uri SignedOutCallbackUrl => BuildPublicUrl(SignedOutCallbackPath);

    public static Auth0BffSettings Load(IConfiguration configuration, IHostEnvironment environment)
        => Load(configuration, environment, Environment.GetEnvironmentVariable);

    public static Auth0BffSettings Load(
        IConfiguration configuration,
        IHostEnvironment environment,
        Func<string, string?> environmentReader)
    {
        var enabled = ReadEnabled(configuration, environmentReader);
        var settings = new Auth0BffSettings(
            enabled,
            Read(configuration, environmentReader, "AUTH0_DOMAIN", "Auth0:Domain"),
            Read(configuration, environmentReader, "AUTH0_CLIENT_ID", "Auth0:ClientId"),
            Read(configuration, environmentReader, "AUTH0_CLIENT_SECRET", "Auth0:ClientSecret"),
            Read(configuration, environmentReader, "AUTH0_CALLBACK_PATH", "Auth0:CallbackPath", DefaultCallbackPath),
            Read(configuration, environmentReader, "AUTH0_SIGNED_OUT_CALLBACK_PATH", "Auth0:SignedOutCallbackPath", DefaultSignedOutCallbackPath),
            Read(configuration, environmentReader, "BFF_PUBLIC_ORIGIN", "Bff:PublicOrigin"));

        if (settings.Enabled)
        {
            settings.Validate(environment.IsDevelopment());
        }

        return settings;
    }

    private void Validate(bool isDevelopment)
    {
        Require(Domain, "AUTH0_DOMAIN");
        if (Domain.Contains("://", StringComparison.Ordinal)
            || Domain.Contains('/')
            || Uri.CheckHostName(Domain) == UriHostNameType.Unknown)
        {
            throw new InvalidOperationException(
                "AUTH0_DOMAIN must be a hostname such as tenant.us.auth0.com, without a scheme or path.");
        }

        Require(ClientId, "AUTH0_CLIENT_ID");
        Require(ClientSecret, "AUTH0_CLIENT_SECRET");
        ValidatePath(CallbackPath, "AUTH0_CALLBACK_PATH");
        ValidatePath(SignedOutCallbackPath, "AUTH0_SIGNED_OUT_CALLBACK_PATH");
        Require(PublicOrigin, "BFF_PUBLIC_ORIGIN");

        if (!Uri.TryCreate(PublicOrigin, UriKind.Absolute, out var origin)
            || origin.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(origin.Query)
            || !string.IsNullOrEmpty(origin.Fragment)
            || origin.AbsolutePath != "/")
        {
            throw new InvalidOperationException(
                "BFF_PUBLIC_ORIGIN must contain only an absolute HTTP(S) origin, for example https://localhost:7002.");
        }

        if (!isDevelopment && origin.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("BFF_PUBLIC_ORIGIN must use HTTPS outside Development.");
        }

        if (!isDevelopment && origin.IsLoopback)
        {
            throw new InvalidOperationException("BFF_PUBLIC_ORIGIN cannot use a loopback host outside Development.");
        }

        if (isDevelopment
            && origin.Scheme == Uri.UriSchemeHttp
            && !origin.IsLoopback)
        {
            throw new InvalidOperationException(
                "An HTTP BFF_PUBLIC_ORIGIN is allowed only for a loopback address in Development.");
        }
    }

    private Uri BuildPublicUrl(string path)
    {
        if (!Enabled)
        {
            throw new InvalidOperationException("Auth0 is disabled.");
        }

        return new Uri(new Uri(PublicOrigin.TrimEnd('/') + "/", UriKind.Absolute), path.TrimStart('/'));
    }

    private static bool ReadEnabled(IConfiguration configuration, Func<string, string?> environmentReader)
    {
        var raw = Read(configuration, environmentReader, "AUTH0_ENABLED", "Auth0:Enabled", "false");
        if (!bool.TryParse(raw, out var enabled))
        {
            throw new InvalidOperationException("AUTH0_ENABLED must be 'true' or 'false'.");
        }

        return enabled;
    }

    private static string Read(
        IConfiguration configuration,
        Func<string, string?> environmentReader,
        string environmentVariable,
        string configurationKey,
        string fallback = "") =>
        (environmentReader(environmentVariable)
         ?? configuration[configurationKey]
         ?? fallback).Trim();

    private static void Require(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{name} is required when AUTH0_ENABLED=true.");
        }
    }

    private static void ValidatePath(string path, string name)
    {
        if (!path.StartsWith("/", StringComparison.Ordinal)
            || path.StartsWith("//", StringComparison.Ordinal)
            || path.Contains('?')
            || path.Contains('#'))
        {
            throw new InvalidOperationException($"{name} must be a local absolute path beginning with '/'.");
        }
    }
}
