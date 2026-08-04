namespace SharedContracts.Common.Responses;

/// <summary>
/// Đổi kiểu phần tử của một trang mà GIỮ NGUYÊN metadata phân trang.
///
/// <para>Vì sao cần: nhiều handler không thể chiếu thẳng sang DTO trong SQL — mapper là method call
/// (<c>Mapper.ToDto(x)</c>) nên EF chỉ chạy được ở phía client, hoặc phải truy vấn phụ để làm giàu
/// dữ liệu (reactions của chat, SLA timer của ticket). Những chỗ đó phân trang trên <i>entity</i>
/// bằng <c>ToPagedEntityListAsync</c> rồi đổi kiểu ở đây, thay vì dựng lại
/// <see cref="PaginationResponse{T}"/> bằng tay — mỗi lần dựng tay là một cơ hội gán nhầm
/// <c>TotalItems</c> hoặc quên <c>PageNumber</c> đã được kẹp.</para>
/// </summary>
public static class PaginationResponseExtensions
{
    /// <summary>Map từng phần tử độc lập.</summary>
    public static PaginationResponse<TOut> Map<TIn, TOut>(
        this PaginationResponse<TIn> source,
        Func<TIn, TOut> mapper)
        => source.WithItems(source.Items.Select(mapper).ToList());

    /// <summary>
    /// Thay nguyên danh sách phần tử. Dùng khi việc map cần cả trang cùng lúc (gom Id để truy vấn phụ
    /// một lượt rồi mới dựng DTO) nên không tách được thành hàm map từng phần tử.
    /// </summary>
    public static PaginationResponse<TOut> WithItems<TIn, TOut>(
        this PaginationResponse<TIn> source,
        List<TOut> items)
        => new()
        {
            Items = items,
            TotalItems = source.TotalItems,
            PageNumber = source.PageNumber,
            PageSize = source.PageSize,
        };
}
