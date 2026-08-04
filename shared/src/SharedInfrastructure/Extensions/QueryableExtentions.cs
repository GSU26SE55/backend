using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace SharedInfrastructure.Extensions;

public static class QueryableExtensions
{
    /// <summary>
    /// Phân trang một <see cref="IQueryable{T}"/> đã lọc + sắp xếp xong: đếm tổng, cắt trang, gói vào
    /// <see cref="PaginationResponse{T}"/>. <b>Đây là nơi duy nhất được phép làm phép tính Skip/Take</b> —
    /// mọi handler tự viết <c>.Skip((page-1)*size).Take(size)</c> đều phải đổi sang gọi hàm này.
    ///
    /// <para><b>Sắp xếp phải xong TRƯỚC khi gọi.</b> Không có <c>ORDER BY</c> toàn phần thì Postgres được
    /// phép trả thứ tự khác nhau giữa các lần chạy — khi đó một dòng có thể xuất hiện ở 2 trang hoặc
    /// biến mất hẳn. Luôn kết thúc chuỗi sắp xếp bằng một khoá duy nhất (thường là <c>Id</c>).</para>
    /// </summary>
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

        var totalItems = await source.CountAsync(cancellationToken);

        // Ép long TRƯỚC khi nhân. Trước đây phép nhân chạy bằng int: pageNumber lớn (người dùng sửa tay
        // trên URL) làm (pageNumber-1)*pageSize tràn và quấn thành số ÂM → Postgres ném
        // "2201X: OFFSET must not be negative" → HTTP 500. Tái hiện được ngày 02/08/2026 với
        // ?pageNumber=300000000&pageSize=10 trên 7 endpoint đang chạy.
        var skip = (long)(pageNumber - 1) * pageSize;

        // Trang vượt quá dữ liệu → trả rỗng, khỏi chạm DB. Nhánh này cũng bảo đảm ép (int)skip luôn an
        // toàn: đã lọt xuống đây thì skip < totalItems, mà totalItems là int.
        var items = skip >= totalItems
            ? new List<T>()
            : await source
                .Skip((int)skip)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

        return new PaginationResponse<T>
        {
            Items = items,
            TotalItems = totalItems,
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
        var page = await source.ToPagedEntityListAsync(pageNumber, pageSize, cancellationToken);

        var shapedItems = page.Items
            .Select(mapper)
            .Select(x => DataShaper.ShapeData(x, fields))
            .ToList();

        return page.WithItems(shapedItems);
    }
}
