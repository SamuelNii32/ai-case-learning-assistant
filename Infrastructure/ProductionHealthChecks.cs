using System.Data.Common;
using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Api.Infrastructure;

public sealed class DatabaseHealthCheck(DatabaseOptions databaseOptions) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = databaseOptions.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1;";
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy("Database is accepting queries.", new Dictionary<string, object>
            {
                ["provider"] = databaseOptions.Provider
            });
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Database query failed.", exception);
        }
    }
}

public sealed class RedisHealthCheck(IConnectionMultiplexer connection) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var latency = await connection.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy("Redis is accepting commands.", new Dictionary<string, object>
            {
                ["latency_ms"] = Math.Round(latency.TotalMilliseconds, 2)
            });
        }
        catch (Exception exception)
        {
            // Redis rate limiting already fails open to the local limiter, so readiness is degraded,
            // not unhealthy. This keeps a Redis outage from removing every API replica at once.
            return HealthCheckResult.Degraded(
                "Redis is unavailable; per-instance rate limiting remains active.",
                exception);
        }
    }
}

public sealed class IndexQueueHealthCheck(
    DatabaseOptions databaseOptions,
    IConfiguration configuration) : IHealthCheck
{
    private readonly int _degradedQueueDepth = ReadPositiveInt(
        configuration, "INDEX_QUEUE_DEGRADED_DEPTH", "Health:IndexQueueDegradedDepth", 20);
    private readonly TimeSpan _staleAfter = TimeSpan.FromMinutes(ReadPositiveInt(
        configuration, "INDEX_JOB_STALE_MINUTES", "Health:IndexJobStaleMinutes", 35));

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = databaseOptions.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT Status, CreatedAt, UpdatedAt, StartedAt, LastHeartbeatAt
FROM IndexJobs
WHERE Status IN ('queued', 'running')
   OR (Status = 'failed' AND UpdatedAt >= @failedCutoff);";
            var failedCutoff = command.CreateParameter();
            failedCutoff.ParameterName = "@failedCutoff";
            failedCutoff.Value = DateTimeOffset.UtcNow.AddHours(-1).ToString("O");
            command.Parameters.Add(failedCutoff);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var queued = 0;
            var running = 0;
            var staleRunning = 0;
            var failuresLastHour = 0;
            TimeSpan? oldestQueuedAge = null;

            while (await reader.ReadAsync(cancellationToken))
            {
                var status = reader["Status"]?.ToString();
                if (string.Equals(status, "queued", StringComparison.OrdinalIgnoreCase))
                {
                    queued++;
                    if (TryReadTimestamp(reader, "CreatedAt", out var createdAt))
                    {
                        var age = now - createdAt;
                        if (oldestQueuedAge is null || age > oldestQueuedAge)
                            oldestQueuedAge = age;
                    }
                }
                else if (string.Equals(status, "running", StringComparison.OrdinalIgnoreCase))
                {
                    running++;
                    if (TryReadFirstTimestamp(reader, out var heartbeat) && now - heartbeat >= _staleAfter)
                        staleRunning++;
                }
                else if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase) &&
                         TryReadTimestamp(reader, "UpdatedAt", out var failedAt) &&
                         now - failedAt <= TimeSpan.FromHours(1))
                {
                    failuresLastHour++;
                }
            }

            var data = new Dictionary<string, object>
            {
                ["queued"] = queued,
                ["running"] = running,
                ["stale_running"] = staleRunning,
                ["failures_last_hour"] = failuresLastHour,
                ["oldest_queued_seconds"] = Math.Round(oldestQueuedAge?.TotalSeconds ?? 0, 1)
            };

            if (staleRunning > 0)
                return HealthCheckResult.Degraded("One or more index jobs have stale worker leases.", data: data);
            if (queued >= _degradedQueueDepth)
                return HealthCheckResult.Degraded("Index queue depth is above its warning threshold.", data: data);
            if (failuresLastHour >= 5)
                return HealthCheckResult.Degraded("Index job failures are elevated.", data: data);

            return HealthCheckResult.Healthy("Index queue is operating normally.", data);
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Index queue could not be inspected.", exception);
        }
    }

    private static bool TryReadFirstTimestamp(DbDataReader reader, out DateTimeOffset value)
    {
        value = default;
        return TryReadTimestamp(reader, "LastHeartbeatAt", out value) ||
               TryReadTimestamp(reader, "StartedAt", out value) ||
               TryReadTimestamp(reader, "CreatedAt", out value);
    }

    private static bool TryReadTimestamp(DbDataReader reader, string column, out DateTimeOffset value)
    {
        value = default;
        var raw = reader[column];
        return raw is not DBNull && DateTimeOffset.TryParse(raw?.ToString(), out value);
    }

    private static int ReadPositiveInt(
        IConfiguration configuration,
        string environmentName,
        string configurationName,
        int fallback)
    {
        var raw = Environment.GetEnvironmentVariable(environmentName) ?? configuration[configurationName];
        return int.TryParse(raw, out var value) && value > 0 ? value : fallback;
    }
}

public static class HealthResponseWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        var response = new
        {
            status = report.Status.ToString().ToLowerInvariant(),
            durationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 2),
            checks = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new
                {
                    status = entry.Value.Status.ToString().ToLowerInvariant(),
                    description = entry.Value.Description,
                    durationMs = Math.Round(entry.Value.Duration.TotalMilliseconds, 2),
                    data = entry.Value.Data
                })
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(response, JsonOptions));
    }

    public static HealthCheckOptions LivenessOptions { get; } = new()
    {
        Predicate = _ => false,
        ResponseWriter = WriteAsync
    };

    public static HealthCheckOptions ReadinessOptions { get; } = new()
    {
        Predicate = registration => registration.Tags.Contains("ready"),
        ResponseWriter = WriteAsync
    };
}
