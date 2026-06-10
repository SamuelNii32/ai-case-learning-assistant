using Microsoft.AspNetCore.Http;

namespace Api.Extensions;

public static class HttpContextAuthExtensions
{
    public static string? GetCurrentUserId(this HttpContext ctx)
    {
        return ctx.Items["userId"] as string
            ?? ctx.User.FindFirst("sub")?.Value;
    }

    public static bool IsCurrentUserSuperUser(this HttpContext ctx)
    {
        if (ctx.Items.TryGetValue("isSuperUser", out var itemValue) &&
            itemValue is bool itemBool &&
            itemBool)
        {
            return true;
        }

        return string.Equals(
            ctx.User.FindFirst("isSuperUser")?.Value,
            "true",
            StringComparison.OrdinalIgnoreCase);
    }
}
