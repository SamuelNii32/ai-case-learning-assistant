using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Api.Infrastructure;

public static class RuntimeStateVerification
{
    public static async Task RunAsync(IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        VerifyBoundedCache();
        VerifyRedisConnectionUrlParsing();
        await VerifyRedisRateLimiterAsync(configuration, cancellationToken);
        Console.WriteLine("[RUNTIME STATE VERIFY] All checks passed.");
    }

    private static void VerifyRedisConnectionUrlParsing()
    {
        var options = RedisConnectionOptions.Parse(
            "rediss://default:p%40ss@example.test:6380/2",
            abortOnConnectFail: false,
            clientName: "verification");

        Ensure(options.Ssl && options.User == "default" && options.Password == "p@ss",
            "A TLS Redis URL was not parsed with its credentials.");
        Ensure(options.DefaultDatabase == 2 && options.EndPoints.Count == 1,
            "A Redis URL was not parsed with its endpoint and database.");
    }

    private static void VerifyBoundedCache()
    {
        var cache = new BoundedCache<string, string>(
            maxSize: 2,
            slidingExpiration: TimeSpan.FromMinutes(1));

        cache["first"] = "one";
        cache["second"] = "two";
        Ensure(cache.TryGetValue("first", out var first) && first == "one",
            "A cached value could not be read.");

        cache["third"] = "three";
        Ensure(!cache.TryGetValue("second", out _),
            "The least-recently-used value was not evicted at the configured bound.");
        Ensure(cache.TryGetValue("first", out _) && cache.TryGetValue("third", out _),
            "Cache eviction removed the wrong value.");

        var weighted = new BoundedCache<string, string>(
            maxSize: 3,
            slidingExpiration: TimeSpan.FromMinutes(1),
            sizeCalculator: value => value.Length);
        weighted["oversized"] = "four";
        Ensure(!weighted.TryGetValue("oversized", out _),
            "A value larger than the entire cache budget was retained.");
    }

    private static async Task VerifyRedisRateLimiterAsync(
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var connectionString = Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING")
            ?? configuration["Redis:ConnectionString"];
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("REDIS_CONNECTION_STRING is required for runtime-state verification.");

        var options = RedisConnectionOptions.Parse(
            connectionString,
            abortOnConnectFail: true,
            clientName: "casepilot-verification");

        await using var connection = await ConnectionMultiplexer.ConnectAsync(options);
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var limiter = new RedisRateLimiter(connection, loggerFactory.CreateLogger<RedisRateLimiter>());
        var partition = $"verification:{Guid.NewGuid():N}";

        var first = await limiter.CheckAsync(partition, "verification", 2, TimeSpan.FromMinutes(1), cancellationToken);
        var second = await limiter.CheckAsync(partition, "verification", 2, TimeSpan.FromMinutes(1), cancellationToken);
        var third = await limiter.CheckAsync(partition, "verification", 2, TimeSpan.FromMinutes(1), cancellationToken);

        Ensure(first.IsAllowed && second.IsAllowed && !third.IsAllowed,
            "Redis did not enforce an atomic limit shared across calls.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException($"Runtime-state verification failed: {message}");
    }
}
