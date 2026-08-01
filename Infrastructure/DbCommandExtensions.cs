using System.Data.Common;

namespace Api.Infrastructure;

public static class DbCommandExtensions
{
    public static DbParameter AddWithValue(this DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = NormalizeName(name);
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
        return parameter;
    }

    private static string NormalizeName(string name)
    {
        return name.StartsWith('@') || name.StartsWith(':')
            ? name
            : '@' + name.TrimStart('$');
    }
}
