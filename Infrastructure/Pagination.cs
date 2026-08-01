namespace Api.Infrastructure;

public sealed record PagedResult<T>(
    List<T> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNextPage => Page * PageSize < TotalCount;
    public bool HasPreviousPage => Page > 1;
}

public static class Pagination
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    public static int NormalizePage(int page) => page < 1 ? 1 : page;

    public static int NormalizePageSize(int pageSize, int defaultPageSize = DefaultPageSize)
    {
        if (pageSize < 1)
        {
            return Math.Clamp(defaultPageSize, 1, MaxPageSize);
        }

        return Math.Clamp(pageSize, 1, MaxPageSize);
    }

    public static int Offset(int page, int pageSize) => (NormalizePage(page) - 1) * NormalizePageSize(pageSize);

    public static string BuildLikePattern(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant();

        normalized = normalized.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

        return $"%{normalized}%";
    }
}
