using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Api.Infrastructure;

public static class RedisConnectionOptions
{
    public static ConfigurationOptions Parse(string connectionString, bool abortOnConnectFail, string clientName)
    {
        ConfigurationOptions options;
        if (Uri.TryCreate(connectionString, UriKind.Absolute, out var uri) &&
            (string.Equals(uri.Scheme, "redis", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(uri.Scheme, "rediss", StringComparison.OrdinalIgnoreCase)))
        {
            options = new ConfigurationOptions
            {
                Ssl = string.Equals(uri.Scheme, "rediss", StringComparison.OrdinalIgnoreCase),
                SslHost = uri.Host
            };
            options.EndPoints.Add(uri.Host, uri.IsDefaultPort ? (options.Ssl ? 6380 : 6379) : uri.Port);

            if (!string.IsNullOrWhiteSpace(uri.UserInfo))
            {
                var separator = uri.UserInfo.IndexOf(':');
                if (separator >= 0)
                {
                    var user = Uri.UnescapeDataString(uri.UserInfo[..separator]);
                    options.User = string.IsNullOrWhiteSpace(user) ? null : user;
                    options.Password = Uri.UnescapeDataString(uri.UserInfo[(separator + 1)..]);
                }
                else
                {
                    options.Password = Uri.UnescapeDataString(uri.UserInfo);
                }
            }

            var databasePath = uri.AbsolutePath.Trim('/');
            if (int.TryParse(databasePath, out var database) && database >= 0)
                options.DefaultDatabase = database;
        }
        else
        {
            options = ConfigurationOptions.Parse(connectionString);
        }

        options.AbortOnConnectFail = abortOnConnectFail;
        options.ClientName = clientName;
        return options;
    }
}

public sealed class RedisRateLimiter(IConnectionMultiplexer connection, ILogger<RedisRateLimiter> logger)
{
    private const string IncrementScript = """
        local current = redis.call('INCR', KEYS[1])
        if current == 1 then
            redis.call('PEXPIRE', KEYS[1], ARGV[1])
        end
        return current
        """;

    private readonly IDatabase _database = connection.GetDatabase();
    private long _lastFailureLogTicks;

    public async Task<RedisRateLimitResult> CheckAsync(
        string partitionKey,
        string policy,
        int permitLimit,
        TimeSpan window,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var windowMilliseconds = Math.Max(1L, (long)window.TotalMilliseconds);
        var windowNumber = now.ToUnixTimeMilliseconds() / windowMilliseconds;
        var keyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(partitionKey)));
        var redisKey = (RedisKey)$"casepilot:ratelimit:{policy}:{keyHash}:{windowNumber}";

        try
        {
            var count = (long)await _database.ScriptEvaluateAsync(
                IncrementScript,
                new[] { redisKey },
                new RedisValue[] { windowMilliseconds + 1_000 });

            var remaining = Math.Max(0, permitLimit - count);
            var retryAfter = TimeSpan.FromMilliseconds(
                Math.Max(1, windowMilliseconds - now.ToUnixTimeMilliseconds() % windowMilliseconds));
            return new RedisRateLimitResult(count <= permitLimit, remaining, retryAfter);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            LogFailureAtMostOncePerMinute(ex);
            // The existing in-process limiter remains active as a safe fallback.
            return RedisRateLimitResult.Fallback;
        }
    }

    private void LogFailureAtMostOncePerMinute(Exception exception)
    {
        var nowTicks = DateTimeOffset.UtcNow.UtcTicks;
        var previousTicks = Interlocked.Read(ref _lastFailureLogTicks);
        if (nowTicks - previousTicks < TimeSpan.FromMinutes(1).Ticks)
            return;

        if (Interlocked.CompareExchange(ref _lastFailureLogTicks, nowTicks, previousTicks) == previousTicks)
        {
            logger.LogWarning(exception,
                "Redis rate limiting is unavailable; the API is temporarily using per-instance limits only.");
        }
    }

    public static string GetPartitionKey(HttpContext context)
    {
        var userId = context.User.FindFirst("sub")?.Value;
        return string.IsNullOrWhiteSpace(userId)
            ? $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}"
            : $"user:{userId}";
    }
}

public readonly record struct RedisRateLimitResult(bool IsAllowed, long Remaining, TimeSpan RetryAfter)
{
    public static RedisRateLimitResult Fallback { get; } = new(true, -1, TimeSpan.Zero);
}

public static class RedisRateLimiterApplicationExtensions
{
    public static IApplicationBuilder UseRedisRateLimiter(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            var limiter = context.RequestServices.GetService<RedisRateLimiter>();
            if (limiter is null)
            {
                await next();
                return;
            }

            var partitionKey = RedisRateLimiter.GetPartitionKey(context);
            var global = await limiter.CheckAsync(
                partitionKey, "global", 180, TimeSpan.FromMinutes(1), context.RequestAborted);
            if (!global.IsAllowed)
            {
                await RejectAsync(context, global);
                return;
            }

            var policyName = context.GetEndpoint()?
                .Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
            var policy = policyName switch
            {
                "Auth" => (Limit: 10, Window: TimeSpan.FromMinutes(1)),
                "Upload" => (Limit: 8, Window: TimeSpan.FromMinutes(10)),
                "Ai" => (Limit: 30, Window: TimeSpan.FromMinutes(1)),
                _ => ((int Limit, TimeSpan Window)?)null
            };

            if (policy is not null)
            {
                var endpoint = await limiter.CheckAsync(
                    partitionKey,
                    policyName!,
                    policy.Value.Limit,
                    policy.Value.Window,
                    context.RequestAborted);
                if (!endpoint.IsAllowed)
                {
                    await RejectAsync(context, endpoint);
                    return;
                }
            }

            await next();
        });
    }

    private static async Task RejectAsync(HttpContext context, RedisRateLimitResult result)
    {
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(result.RetryAfter.TotalSeconds)).ToString();
        await context.Response.WriteAsJsonAsync(new { error = "Too many requests. Please retry later." });
    }
}
