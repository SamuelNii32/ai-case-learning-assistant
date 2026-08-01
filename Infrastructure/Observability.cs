using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenAI.Chat;

namespace Api.Infrastructure;

public static class CasePilotTelemetry
{
    public const string ServiceName = "casepilot-api";
    public const string MeterName = "CasePilot.Api";
    public const string ActivitySourceName = "CasePilot.Api";

    public static readonly Meter Meter = new(MeterName, "1.0.0");
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, "1.0.0");
    public static string ConfiguredAnswerModel =>
        Environment.GetEnvironmentVariable("OPENAI_ANSWER_MODEL") ?? "gpt-5.1";

    private static readonly Counter<long> RequestCount = Meter.CreateCounter<long>(
        "casepilot.http.server.requests", unit: "{request}");
    private static readonly Histogram<double> RequestDuration = Meter.CreateHistogram<double>(
        "casepilot.http.server.duration", unit: "ms");
    private static readonly Counter<long> AiRequestCount = Meter.CreateCounter<long>(
        "casepilot.ai.requests", unit: "{request}");
    private static readonly Histogram<double> AiRequestDuration = Meter.CreateHistogram<double>(
        "casepilot.ai.duration", unit: "ms");
    private static readonly Counter<long> AiInputTokens = Meter.CreateCounter<long>(
        "casepilot.ai.input_tokens", unit: "{token}");
    private static readonly Counter<long> AiOutputTokens = Meter.CreateCounter<long>(
        "casepilot.ai.output_tokens", unit: "{token}");
    private static readonly Counter<long> IndexJobCount = Meter.CreateCounter<long>(
        "casepilot.index.jobs", unit: "{job}");
    private static readonly Histogram<double> IndexJobDuration = Meter.CreateHistogram<double>(
        "casepilot.index.job.duration", unit: "s");
    private static readonly Counter<long> RateLimitRejections = Meter.CreateCounter<long>(
        "casepilot.ratelimit.rejections", unit: "{request}");
    private static readonly Counter<long> RateLimitFallbacks = Meter.CreateCounter<long>(
        "casepilot.ratelimit.redis_fallbacks", unit: "{request}");

    public static void RecordRequest(string method, string route, int statusCode, TimeSpan elapsed)
    {
        var tags = new TagList
        {
            { "http.request.method", method },
            { "http.route", route },
            { "http.response.status_code", statusCode }
        };
        RequestCount.Add(1, tags);
        RequestDuration.Record(elapsed.TotalMilliseconds, tags);
    }

    public static void RecordChatCompletion(
        ChatCompletion completion,
        string operation,
        string model,
        TimeSpan elapsed,
        bool succeeded = true)
    {
        RecordChatUsage(completion.Usage, operation, model, elapsed, succeeded);
    }

    public static void RecordChatUsage(
        ChatTokenUsage? usage,
        string operation,
        string model,
        TimeSpan elapsed,
        bool succeeded = true)
    {
        var tags = AiTags("chat", operation, model, succeeded);
        AiRequestCount.Add(1, tags);
        AiRequestDuration.Record(elapsed.TotalMilliseconds, tags);

        if (usage is not null)
        {
            AiInputTokens.Add(usage.InputTokenCount, tags);
            AiOutputTokens.Add(usage.OutputTokenCount, tags);
        }
    }

    public static void RecordAiFailure(string kind, string operation, string model, TimeSpan elapsed)
    {
        var tags = AiTags(kind, operation, model, succeeded: false);
        AiRequestCount.Add(1, tags);
        AiRequestDuration.Record(elapsed.TotalMilliseconds, tags);
    }

    public static void RecordEmbeddingBatch(string model, int inputTokens, TimeSpan elapsed)
    {
        var tags = AiTags("embedding", "index", model, succeeded: true);
        AiRequestCount.Add(1, tags);
        AiRequestDuration.Record(elapsed.TotalMilliseconds, tags);
        AiInputTokens.Add(inputTokens, tags);
    }

    public static void RecordIndexJob(string status, TimeSpan elapsed, bool cached)
    {
        var tags = new TagList
        {
            { "job.status", status },
            { "job.cached", cached }
        };
        IndexJobCount.Add(1, tags);
        IndexJobDuration.Record(elapsed.TotalSeconds, tags);
    }

    public static void RecordRateLimitRejection(string policy) =>
        RateLimitRejections.Add(1, new TagList { { "ratelimit.policy", policy } });

    public static void RecordRedisRateLimitFallback(string policy) =>
        RateLimitFallbacks.Add(1, new TagList { { "ratelimit.policy", policy } });

    private static TagList AiTags(string kind, string operation, string model, bool succeeded) => new()
    {
        { "gen_ai.operation.name", operation },
        { "gen_ai.request.model", model },
        { "gen_ai.system", "openai" },
        { "casepilot.ai.kind", kind },
        { "casepilot.outcome", succeeded ? "success" : "failure" }
    };
}

public sealed class RequestTelemetryMiddleware(
    RequestDelegate next,
    ILogger<RequestTelemetryMiddleware> logger,
    IConfiguration configuration)
{
    private readonly double _slowRequestMilliseconds = ReadPositiveDouble(
        configuration,
        "SLOW_REQUEST_THRESHOLD_MS",
        "Observability:SlowRequestThresholdMs",
        1_500);

    public async Task InvokeAsync(HttpContext context)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            await next(context);
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(started);
            var route = context.GetEndpoint() is RouteEndpoint endpoint
                ? endpoint.RoutePattern.RawText ?? "unknown"
                : "unmatched";

            CasePilotTelemetry.RecordRequest(
                context.Request.Method,
                route,
                context.Response.StatusCode,
                elapsed);

            if (elapsed.TotalMilliseconds >= _slowRequestMilliseconds || context.Response.StatusCode >= 500)
            {
                logger.LogWarning(
                    "Slow or failed request {Method} {Route} returned {StatusCode} in {ElapsedMs:0.0} ms (request {RequestId}).",
                    context.Request.Method,
                    route,
                    context.Response.StatusCode,
                    elapsed.TotalMilliseconds,
                    context.TraceIdentifier);
            }
        }
    }

    private static double ReadPositiveDouble(
        IConfiguration configuration,
        string environmentName,
        string configurationName,
        double fallback)
    {
        var raw = Environment.GetEnvironmentVariable(environmentName) ?? configuration[configurationName];
        return double.TryParse(raw, out var value) && value > 0 ? value : fallback;
    }
}

public static class ObservabilityServiceCollectionExtensions
{
    public static IServiceCollection AddCasePilotObservability(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName)
    {
        var serviceVersion = typeof(ObservabilityServiceCollectionExtensions).Assembly
            .GetName().Version?.ToString() ?? "unknown";
        var otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")
            ?? configuration["Observability:OtlpEndpoint"];

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName: serviceName,
                serviceVersion: serviceVersion,
                serviceInstanceId: Environment.GetEnvironmentVariable("RENDER_INSTANCE_ID")
                    ?? Environment.MachineName))
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(CasePilotTelemetry.ActivitySourceName)
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.RecordException = true;
                        options.Filter = context =>
                            !context.Request.Path.StartsWithSegments("/health/live");
                    })
                    .AddHttpClientInstrumentation(options => options.RecordException = true);

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                    tracing.AddOtlpExporter();
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddMeter(CasePilotTelemetry.MeterName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                    metrics.AddOtlpExporter();
            });

        return services;
    }
}
