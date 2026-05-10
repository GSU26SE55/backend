using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace SharedInfrastructure.Extensions;

public static class QueryableExtensions
{
    public static async Task<PaginationResponse<T>> ToPagedEntityListAsync<T>
    (
        this IQueryable<T> source,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default
    )
    {
        pageNumber = pageNumber <= 0 ? 1 : pageNumber;
        pageSize = pageSize <= 0 ? 10 : pageSize;

        var count = await source.CountAsync(cancellationToken);

        var item = await source
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PaginationResponse<T>
        {
            Items = item,
            TotalItems = count,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
    }

    public static async Task<PaginationResponse<object>> ToPagedListAsync<TSource, TResponse>(
        this IQueryable<TSource> source,
        int pageNumber,
        int pageSize,
        Func<TSource, TResponse> mapper,
        string? fields,
        CancellationToken cancellationToken = default)
    {
        pageNumber = pageNumber <= 0 ? 1 : pageNumber;
        pageSize = pageSize <= 0 ? 10 : pageSize;

        var count = await source.CountAsync(cancellationToken);
        var items = await source.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);

        var dtoItems = items.Select(mapper);

        var shapedItems = dtoItems.Select(x => DataShaper.ShapeData(x, fields)).ToList();

        return new PaginationResponse<object>
        {
            Items = shapedItems,
            TotalItems = count,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
    }
}
