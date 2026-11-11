using Api.Extensions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace IngestionApi.Tests;

public sealed class CombinedSpaHostingTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/login")]
    [InlineData("/dashboard")]
    [InlineData("/cases")]
    [InlineData("/sessions")]
    [InlineData("/admin/classes")]
    [InlineData("/admin/sessions/session-1")]
    [InlineData("/workspace/11111111-1111-1111-1111-111111111111")]
    public void HtmlDocumentNavigation_ToKnownReactRoute_UsesSpaShell(string path)
    {
        var request = Request(HttpMethods.Get, path, "text/html,application/xhtml+xml");

        Assert.True(CombinedSpaHostingExtensions.IsSpaNavigationRequest(request));
    }

    [Theory]
    [InlineData("/bff/session")]
    [InlineData("/signin-auth0")]
    [InlineData("/signout-callback-auth0")]
    [InlineData("/api/not-a-route")]
    [InlineData("/health/ready")]
    [InlineData("/uploads/not-a-document.pdf")]
    [InlineData("/unknown-browser-route")]
    public void HtmlRequest_ToBackendOrUnknownRoute_DoesNotUseSpaShell(string path)
    {
        var request = Request(HttpMethods.Get, path, "text/html");

        Assert.False(CombinedSpaHostingExtensions.IsSpaNavigationRequest(request));
    }

    [Theory]
    [InlineData("/cases")]
    [InlineData("/sessions")]
    [InlineData("/admin/sessions")]
    public void ApiGet_ToOverlappingPath_DoesNotUseSpaShell(string path)
    {
        var request = Request(HttpMethods.Get, path, "application/json");

        Assert.False(CombinedSpaHostingExtensions.IsSpaNavigationRequest(request));
    }

    [Fact]
    public void Mutation_ToReactPath_DoesNotUseSpaShell()
    {
        var request = Request(HttpMethods.Post, "/classes", "text/html");

        Assert.False(CombinedSpaHostingExtensions.IsSpaNavigationRequest(request));
    }

    private static HttpRequest Request(string method, string path, string accept)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.Headers.Accept = accept;
        return context.Request;
    }
}
