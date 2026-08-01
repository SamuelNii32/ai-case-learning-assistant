using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using OpenAI.Chat;
using System;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Api.Infrastructure;
using Microsoft.OpenApi.Models;
using StackExchange.Redis;

namespace Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAppServices(
        this IServiceCollection services,
        IConfiguration configuration,
        AuthSettings authSettings)
    {
        // Read OpenAI config (API key + models)
        var openAiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new InvalidOperationException("OPENAI_API_KEY not set.");

        // Answer model: big brain for actual answers (default gpt-5.1)
        var answerModel = Environment.GetEnvironmentVariable("OPENAI_ANSWER_MODEL")
            ?? "gpt-5.1";

        // OpenAI Chat client for answers (we'll also new up a separate client for the classifier later)
        services.AddSingleton<ChatClient>(_ =>
        {
            return new ChatClient(model: answerModel, openAiApiKey);
        });

        AddDocumentStorage(services, configuration);
        services.AddSingleton(DatabaseOptions.Load(configuration));
        services.AddSingleton<IUploadRepository, SqliteUploadRepository>();
        services.AddSingleton<IUserRepository, SqliteUserRepository>();
        services.AddSingleton<ISessionRepository, SqliteSessionRepository>();
        services.AddSingleton<IMessageRepository, SqliteMessageRepository>();
        services.AddSingleton<IClassRepository, SqliteClassRepository>();
        services.AddSingleton<ITutorRepository, SqliteTutorRepository>();
        services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();
        services.AddSingleton<IndexJobStore>();
        services.AddSingleton<IndexingService>();

        if (ShouldRunBackgroundWorker(configuration))
        {
            services.AddHostedService<IndexJobWorkerHostedService>();
        }

        services
            .AddAuthentication("Bearer").AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,

                    ValidIssuer = authSettings.JwtIssuer,
                    ValidAudience = authSettings.JwtAudience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authSettings.JwtSecret)),
                    ClockSkew = TimeSpan.FromMinutes(1)
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("InstructorOnly", p => p.RequireClaim("role", "instructor"));
            options.AddPolicy("StudentOnly", p => p.RequireClaim("role", "student"));
        });

        services.AddHealthChecks();

        var redisConnectionString = Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING")
            ?? configuration["Redis:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            services.AddSingleton<IConnectionMultiplexer>(_ =>
            {
                var options = RedisConnectionOptions.Parse(
                    redisConnectionString,
                    abortOnConnectFail: false,
                    clientName: "casepilot-api");
                return ConnectionMultiplexer.Connect(options);
            });
            services.AddSingleton<RedisRateLimiter>();
        }

        // Swagger (optional)
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Ingestion API",
                Version = "v1",
                Description = "Case learning and supervision API."
            });
        });

        services.AddCors(options =>
        {
            options.AddPolicy("FrontendDev", p => p
                .WithOrigins("http://localhost:5174", "http://localhost:3000", "http://localhost:4173", "https://ai-case-learning-assistant.vercel.app", "https://ai-case-learning-assistant-rku540uom.vercel.app")
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials());
        });

        var maxUploadBytes = GetLong(configuration, "MAX_UPLOAD_BYTES", "Upload:MaxBytes", 25L * 1024L * 1024L);

        services.Configure<FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = maxUploadBytes;
            options.ValueLengthLimit = 1024 * 1024;
            options.MultipartHeadersLengthLimit = 64 * 1024;
        });

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
            {
                var key = GetClientPartitionKey(ctx);
                return RateLimitPartition.GetTokenBucketLimiter(key, _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = 180,
                    TokensPerPeriod = 180,
                    ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                    QueueLimit = 20,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    AutoReplenishment = true
                });
            });

            options.AddPolicy("Auth", ctx =>
                RateLimitPartition.GetFixedWindowLimiter(GetClientIp(ctx), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));

            options.AddPolicy("Upload", ctx =>
                RateLimitPartition.GetFixedWindowLimiter(GetClientPartitionKey(ctx), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 8,
                    Window = TimeSpan.FromMinutes(10),
                    QueueLimit = 0
                }));

            options.AddPolicy("Ai", ctx =>
                RateLimitPartition.GetTokenBucketLimiter(GetClientPartitionKey(ctx), _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = 30,
                    TokensPerPeriod = 30,
                    ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                    QueueLimit = 5,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    AutoReplenishment = true
                }));
        });

        return services;
    }

    public static IServiceCollection AddIndexWorkerServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY")))
        {
            throw new InvalidOperationException("OPENAI_API_KEY must be configured for the index worker.");
        }

        AddDocumentStorage(services, configuration);
        services.AddSingleton(DatabaseOptions.Load(configuration));
        services.AddSingleton<IndexJobStore>();
        services.AddSingleton<IndexingService>();
        services.AddHostedService<IndexJobWorkerHostedService>();
        return services;
    }

    private static void AddDocumentStorage(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IDocumentStorage>(sp =>
        {
            var provider = (Environment.GetEnvironmentVariable("DOCUMENT_STORAGE_PROVIDER")
                ?? configuration["DocumentStorage:Provider"]
                ?? "local").Trim().ToLowerInvariant();

            return provider switch
            {
                "local" => ActivatorUtilities.CreateInstance<LocalDocumentStorage>(sp),
                "azureblob" => ActivatorUtilities.CreateInstance<AzureBlobDocumentStorage>(sp),
                _ => throw new InvalidOperationException($"Unsupported DOCUMENT_STORAGE_PROVIDER '{provider}'.")
            };
        });
    }

    private static long GetLong(IConfiguration configuration, string envName, string configKey, long fallback)
    {
        var raw = Environment.GetEnvironmentVariable(envName) ?? configuration[configKey];
        return long.TryParse(raw, out var value) && value > 0 ? value : fallback;
    }

    private static bool ShouldRunBackgroundWorker(IConfiguration configuration)
    {
        var raw = Environment.GetEnvironmentVariable("RUN_BACKGROUND_WORKER")
            ?? configuration["BackgroundWorker:Enabled"];

        return raw is null || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetClientPartitionKey(HttpContext ctx)
    {
        var userId = ctx.User?.FindFirst("sub")?.Value;
        return string.IsNullOrWhiteSpace(userId) ? GetClientIp(ctx) : $"user:{userId}";
    }

    private static string GetClientIp(HttpContext ctx)
    {
        return $"ip:{ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
    }
}
