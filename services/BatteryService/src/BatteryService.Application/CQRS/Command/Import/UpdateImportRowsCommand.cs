using BatteryService.Application.DTOs.Import;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace BatteryService.Application.CQRS.Command.Import;

/// <summary>
/// Sửa trực tiếp giá trị của một hoặc nhiều dòng trong lô rồi kiểm định lại — dùng khi người dùng
/// muốn sửa vài dòng hỏng ngay trên giao diện thay vì tải cả file .xlsx lên lại từ đầu.
/// </summary>
/// <remarks>
/// Kiểm định lại TOÀN BỘ các dòng trong lô (không chỉ những dòng vừa sửa), đúng bằng những quy tắc
/// mà bước tải file .xlsx đã áp dụng: một dòng vừa sửa có thể làm dòng KHÁC hoá valid/invalid (ví dụ
/// sửa lại mã khách hàng cho đúng làm site từng bị "not found" nay tra được), nên phải tính lại toàn
/// bộ tham chiếu chéo và trùng mã trên cả lô, không chỉ vá riêng từng dòng.
/// </remarks>
public class UpdateImportRowsCommand
    : IRequest<CommonResponse<ImportBatchDto>>, IValidatable<CommonResponse<ImportBatchDto>>
{
    public Guid BatchId { get; set; }

    public List<ImportRowEditItem> Rows { get; set; } = new();

    public Task<CommonResponse<ImportBatchDto>> ValidateAsync()
    {
        var response = new CommonResponse<ImportBatchDto>();

        if (Rows.Count == 0)
        {
            response.ListErrors.Add(new Errors { Field = "rows", Detail = "Provide at least one row to update." });
        }
        else
        {
            for (var i = 0; i < Rows.Count; i++)
            {
                if (Rows[i].RowId == Guid.Empty)
                    response.ListErrors.Add(new Errors { Field = $"rows[{i}].rowId", Detail = "Row id is required." });

                if (Rows[i].Fields.Count == 0)
                    response.ListErrors.Add(new Errors { Field = $"rows[{i}].fields", Detail = "Provide at least one field." });
            }

            var duplicateRowIds = Rows.GroupBy(r => r.RowId).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (duplicateRowIds.Count > 0)
            {
                response.ListErrors.Add(new Errors
                {
                    Field = "rows",
                    Detail = $"The same row id appears more than once: {string.Join(", ", duplicateRowIds)}."
                });
            }
        }

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid update request.";
        }

        return Task.FromResult(response);
    }
}

/// <summary>Sửa của một dòng — toàn bộ giá trị các cột, thay thế hoàn toàn dòng gốc (không phải vá từng phần).</summary>
public class ImportRowEditItem
{
    public Guid RowId { get; set; }

    public Dictionary<string, string> Fields { get; set; } = new();
}
