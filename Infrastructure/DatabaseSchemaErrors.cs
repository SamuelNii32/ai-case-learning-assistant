using Microsoft.Data.Sqlite;
using Npgsql;

namespace Api.Infrastructure;

public static class DatabaseSchemaErrors
{
    public static bool IsDuplicateColumn(Exception exception)
    {
        if (exception is PostgresException { SqlState: PostgresErrorCodes.DuplicateColumn })
            return true;

        return exception is SqliteException { SqliteErrorCode: 1 } sqliteException
            && sqliteException.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase);
    }
}
