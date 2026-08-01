using System.Data.Common;
using Microsoft.Data.Sqlite;
using Npgsql;

namespace Api.Infrastructure;

public sealed record DatabaseOptions(string Provider, string ConnectionString, string? LocalPath)
{
    public static DatabaseOptions Load(IConfiguration configuration)
    {
        var provider = (Environment.GetEnvironmentVariable("DATABASE_PROVIDER")
            ?? configuration["Database:Provider"]
            ?? "sqlite").Trim().ToLowerInvariant();

        return provider switch
        {
            "sqlite" => LoadSqlite(configuration, provider),
            "postgres" or "postgresql" => LoadPostgres(configuration, provider),
            _ => throw new InvalidOperationException(
                $"Unsupported DATABASE_PROVIDER '{provider}'. Use 'sqlite' or 'postgres'.")
        };
    }

    public DbConnection CreateConnection() => Provider switch
    {
        "sqlite" => new SqliteConnection(ConnectionString),
        "postgres" or "postgresql" => new NpgsqlConnection(ConnectionString),
        _ => throw new InvalidOperationException($"Unsupported database provider '{Provider}'.")
    };

    private static DatabaseOptions LoadSqlite(IConfiguration configuration, string provider)
    {
        var configuredConnectionString =
            Environment.GetEnvironmentVariable("SQLITE_CONNECTION_STRING")
            ?? configuration.GetConnectionString("Sqlite")
            ?? configuration["Database:SqliteConnectionString"];

        if (!string.IsNullOrWhiteSpace(configuredConnectionString))
        {
            return new DatabaseOptions(provider, configuredConnectionString, null);
        }

        var home = Environment.GetEnvironmentVariable("HOME")
                   ?? Environment.GetEnvironmentVariable("USERPROFILE")
                   ?? ".";
        var dataDir = Path.Combine(home, "ingestion-data");
        Directory.CreateDirectory(dataDir);

        var dbPath = Path.Combine(dataDir, "ingestion.db");
        return new DatabaseOptions(provider, $"Data Source={dbPath};Cache=Shared", dbPath);
    }

    private static DatabaseOptions LoadPostgres(IConfiguration configuration, string provider)
    {
        var configuredConnectionString =
            Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING")
            ?? configuration.GetConnectionString("Postgres")
            ?? configuration["Database:PostgresConnectionString"];

        if (string.IsNullOrWhiteSpace(configuredConnectionString))
        {
            throw new InvalidOperationException(
                "DATABASE_PROVIDER is postgres but no POSTGRES_CONNECTION_STRING was configured.");
        }

        return new DatabaseOptions(provider, configuredConnectionString, null);
    }
}

