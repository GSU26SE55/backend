using AuditAggregatorService.Application.DTOs;
using SharedContracts.Audit;
using SharedContracts.Common.Responses;

namespace AuditAggregatorService.Application.CQRS.Query.Audit;

/// <summary>
/// Validate giá trị filter tập-đóng (<c>severity</c> / <c>category</c>) cho <c>/search</c> + <c>/export</c>
/// (Sprint audit <c>#AUDIT-17</c> — giải pháp A+E cho UX admin non-tech).
/// </summary>
/// <remarks>
/// <para><b>Đối chiếu EXACT</b> với danh sách canonical <see cref="Severities.All"/> / <see cref="AuditCategories.All"/>
/// (cùng nguồn FE dùng dựng dropdown — cách A). Sai value HOẶC sai hoa-thường → trả về lỗi field-level để controller
/// đáp <c>400</c> + <c>listErrors</c> kèm danh sách giá trị đúng, thay vì trả <c>200</c> rỗng âm thầm (foot-gun điều tra forensic).</para>
/// <para><b>KHÔNG</b> validate <c>service</c> (tên service tự do, không có catalog <c>.All</c>) và <c>action</c>
/// (catalog <see cref="ActionCodes"/> nested, không có <c>.All</c> phẳng) — hai field này dựa vào dropdown/typeahead FE.
/// <c>targetType</c> không phải filter param của search/export nên không cần validate.</para>
/// </remarks>
public static class AuditSearchValidator
{
    /// <summary>Trả danh sách lỗi field-level (rỗng = hợp lệ).</summary>
    public static List<Errors> Validate(AuditSearchRequest request)
    {
        var errors = new List<Errors>();

        if (!string.IsNullOrWhiteSpace(request.Severity) && !Severities.All.Contains(request.Severity))
        {
            errors.Add(new Errors
            {
                Field = "severity",
                Detail = $"Value '{request.Severity}' is invalid. Valid values (case-sensitive): {string.Join(", ", Severities.All)}.",
            });
        }

        if (!string.IsNullOrWhiteSpace(request.Category) && !AuditCategories.All.Contains(request.Category))
        {
            errors.Add(new Errors
            {
                Field = "category",
                Detail = $"Value '{request.Category}' is invalid. Valid values (case-sensitive): {string.Join(", ", AuditCategories.All)}.",
            });
        }

        return errors;
    }
}
