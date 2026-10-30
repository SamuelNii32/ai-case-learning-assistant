using Api.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace IngestionApi.Tests;

public sealed class Auth0BffSettingsTests
{
    private static readonly Func<string, string?> NoEnvironment = _ => null;

    [Fact]
    public void Auth0_is_disabled_by_default_without_credentials()
    {
        var settings = Auth0BffSettings.Load(
            Configuration(),
            Environment(Environments.Development),
            NoEnvironment);

        Assert.False(settings.Enabled);
        Assert.Empty(settings.ClientSecret);
    }

    [Fact]
    public void Enabled_configuration_is_validated_and_builds_dashboard_urls()
    {
        var settings = Auth0BffSettings.Load(
            ValidConfiguration(),
            Environment(Environments.Development),
            NoEnvironment);

        Assert.True(settings.Enabled);
        Assert.Equal(new Uri("https://casepilot-dev.us.auth0.com/"), settings.Authority);
        Assert.Equal(new Uri("http://localhost:5174/signin-auth0"), settings.CallbackUrl);
        Assert.Equal(new Uri("http://localhost:5174/signout-callback-auth0"), settings.SignedOutCallbackUrl);
    }

    [Theory]
    [InlineData("Auth0:Domain", "AUTH0_DOMAIN")]
    [InlineData("Auth0:ClientId", "AUTH0_CLIENT_ID")]
    [InlineData("Auth0:ClientSecret", "AUTH0_CLIENT_SECRET")]
    [InlineData("Bff:PublicOrigin", "BFF_PUBLIC_ORIGIN")]
    public void Enabled_configuration_requires_every_credential_and_public_origin(
        string missingKey,
        string expectedSettingName)
    {
        var values = ValidValues();
        values[missingKey] = "";

        var exception = Assert.Throws<InvalidOperationException>(() => Auth0BffSettings.Load(
            Configuration(values),
            Environment(Environments.Development),
            NoEnvironment));

        Assert.Contains(expectedSettingName, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("signin-auth0")]
    [InlineData("//attacker.example/callback")]
    [InlineData("/signin-auth0?redirect=attacker")]
    public void Callback_must_be_a_local_absolute_path(string callbackPath)
    {
        var values = ValidValues();
        values["Auth0:CallbackPath"] = callbackPath;

        Assert.Throws<InvalidOperationException>(() => Auth0BffSettings.Load(
            Configuration(values),
            Environment(Environments.Development),
            NoEnvironment));
    }

    [Fact]
    public void Production_public_origin_requires_https()
    {
        Assert.Throws<InvalidOperationException>(() => Auth0BffSettings.Load(
            ValidConfiguration(),
            Environment(Environments.Production),
            NoEnvironment));
    }

    [Fact]
    public void Production_public_origin_rejects_loopback_even_with_https()
    {
        var values = ValidValues();
        values["Bff:PublicOrigin"] = "https://localhost:7002";

        Assert.Throws<InvalidOperationException>(() => Auth0BffSettings.Load(
            Configuration(values),
            Environment(Environments.Production),
            NoEnvironment));
    }

    [Fact]
    public void Environment_variables_override_development_configuration()
    {
        var environmentValues = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AUTH0_ENABLED"] = "true",
            ["AUTH0_DOMAIN"] = "environment.us.auth0.com",
            ["AUTH0_CLIENT_ID"] = "environment-client",
            ["AUTH0_CLIENT_SECRET"] = "environment-secret",
            ["BFF_PUBLIC_ORIGIN"] = "https://casepilot.example"
        };

        var settings = Auth0BffSettings.Load(
            Configuration(new Dictionary<string, string?> { ["Auth0:Enabled"] = "false" }),
            Environment(Environments.Production),
            name => environmentValues.GetValueOrDefault(name));

        Assert.True(settings.Enabled);
        Assert.Equal("environment.us.auth0.com", settings.Domain);
        Assert.Equal("environment-client", settings.ClientId);
        Assert.Equal("https://casepilot.example", settings.PublicOrigin);
    }

    private static IConfiguration ValidConfiguration() => Configuration(ValidValues());

    private static Dictionary<string, string?> ValidValues() => new()
    {
        ["Auth0:Enabled"] = "true",
        ["Auth0:Domain"] = "casepilot-dev.us.auth0.com",
        ["Auth0:ClientId"] = "development-client-id",
        ["Auth0:ClientSecret"] = "development-client-secret",
        ["Auth0:CallbackPath"] = "/signin-auth0",
        ["Auth0:SignedOutCallbackPath"] = "/signout-callback-auth0",
        ["Bff:PublicOrigin"] = "http://localhost:5174"
    };

    private static IConfiguration Configuration(Dictionary<string, string?>? values = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static IHostEnvironment Environment(string name) => new TestHostEnvironment
    {
        EnvironmentName = name
    };

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "CasePilot.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
