using Microsoft.AspNetCore.Http;

namespace Api.Extensions;

public static class CombinedSpaHostingExtensions
{
    private static readonly HashSet<string> ExactClientRoutes = new(StringComparer.OrdinalIgnoreCase)
    {
        "/",
        "/about",
        "/account",
        "/cases",
        "/classes",
        "/contact",
        "/dashboard",
        "/features",
        "/forgot-password",
        "/login",
        "/privacy",
        "/profile",
        "/session-history",
        "/sessions",
        "/settings",
        "/signin",
        "/signup",
        "/support",
        "/upload"
    };

    private static readonly PathString[] ClientRoutePrefixes =
    [
        new PathString("/admin"),
        new PathString("/workspace")
    ];

    public static IApplicationBuilder UseCombinedSpaNavigationFallback(
        this IApplicationBuilder app,
        IWebHostEnvironment environment)
    {
        return app.Use(async (context, next) =>
        {
            if (!IsSpaNavigationRequest(context.Request))
            {
                await next();
                return;
            }

            var webRoot = environment.WebRootPath
                ?? Path.Combine(environment.ContentRootPath, "wwwroot");
            var indexPath = Path.Combine(webRoot, "index.html");
            if (!File.Exists(indexPath))
            {
                // Vite serves the shell separately during local development.
                await next();
                return;
            }

            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.Headers.CacheControl = "no-cache, no-store";
            await context.Response.SendFileAsync(indexPath, context.RequestAborted);
        });
    }

    internal static bool IsSpaNavigationRequest(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method)) return false;

        var acceptsHtml = request.Headers.Accept.Any(value =>
            value?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true);
        if (!acceptsHtml) return false;

        var path = request.Path.Value ?? "/";
        if (path.Length > 1) path = path.TrimEnd('/');

        if (ExactClientRoutes.Contains(path)) return true;

        var pathString = new PathString(path);
        return ClientRoutePrefixes.Any(prefix => pathString.StartsWithSegments(prefix));
    }
}
