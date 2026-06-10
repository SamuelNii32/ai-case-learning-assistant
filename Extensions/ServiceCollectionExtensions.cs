using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using OpenAI.Chat;
using System;
using System.Text;

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

        // Swagger (optional)
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();

        services.AddCors(options =>
        {
            options.AddPolicy("FrontendDev", p => p
                .WithOrigins("http://localhost:5174", "http://localhost:3000", "http://localhost:4173", "https://ai-case-learning-assistant.vercel.app", "https://ai-case-learning-assistant-rku540uom.vercel.app")
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials());
        });

        return services;
    }
}
