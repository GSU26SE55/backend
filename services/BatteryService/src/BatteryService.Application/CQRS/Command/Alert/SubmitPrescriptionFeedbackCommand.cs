using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace BatteryService.Application.CQRS.Command.Alert;

/// <summary>
/// GH-778 — kỹ thuật viên phản hồi về prescription mà AI đã đưa ra cho một alert.
/// </summary>
/// <remarks>
/// Prescription được AI chấp nhận sẽ thành ví dụ few-shot cho các ca tương tự sau. Không có đường
/// phản hồi thì AI lặp lại cùng một lời khuyên sai mãi mà không ai sửa được — đó là tình trạng
/// trước issue này: <c>prescription_id</c> bị bỏ ngay lúc map response, và không có endpoint nào gọi
/// <c>POST /prescribe/feedback</c>.
/// </remarks>
public class SubmitPrescriptionFeedbackCommand : IRequest<CommonResponse<string>>, IValidatable<CommonResponse<string>>
{
    /// <summary>Alert mang prescription cần phản hồi. Controller gán từ route.</summary>
    public Guid AlertId { get; set; }

    /// <summary>Chỉ nhận <c>accepted</c> | <c>edited</c> | <c>rejected</c> — theo hợp đồng AI.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Các bước đã sửa; chỉ có nghĩa khi <see cref="Status"/> = <c>edited</c>.</summary>
    public List<string>? EditedSteps { get; set; }

    public string? Note { get; set; }

    /// <summary>Ba trạng thái AI chấp nhận (<c>schemas/prescribe.py</c>: <c>Literal[...]</c>).</summary>
    public static readonly string[] AllowedStatuses = ["accepted", "edited", "rejected"];

    public Task<CommonResponse<string>> ValidateAsync()
    {
        var response = new CommonResponse<string>();

        if (string.IsNullOrWhiteSpace(Status))
        {
            response.ListErrors.Add(new Errors { Field = nameof(Status), Detail = "Bắt buộc." });
        }
        else if (!AllowedStatuses.Contains(Status.Trim().ToLowerInvariant()))
        {
            // Chặn ở đây thay vì để AI trả 422: thông báo của mình nói rõ ba giá trị hợp lệ, còn
            // lỗi từ AI thì người dùng cuối không đọc được.
            response.ListErrors.Add(new Errors
            {
                Field = nameof(Status),
                Detail = $"Chỉ nhận: {string.Join(", ", AllowedStatuses)}."
            });
        }

        // `edited` mà không kèm bước nào thì AI không có gì để học — nhận vào chỉ tạo bản ghi rỗng.
        if (Status.Trim().Equals("edited", StringComparison.OrdinalIgnoreCase)
            && (EditedSteps is null || EditedSteps.Count == 0 || EditedSteps.All(string.IsNullOrWhiteSpace)))
        {
            response.ListErrors.Add(new Errors
            {
                Field = nameof(EditedSteps),
                Detail = "Bắt buộc khi status = edited."
            });
        }

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            // PHẢI set StatusCode. Controller trả `StatusCode(result.StatusCode, result)`, mà
            // CommonResponseBase.StatusCode mặc định 0 → Kestrel ghi ra dòng status "HTTP/1.1 0",
            // client nhận BadStatusLine và gateway dịch thành 502 "Upstream không phản hồi hợp lệ".
            // Người dùng gõ sai `status` sẽ thấy y hệt lúc AI sập, còn listErrors thì không bao giờ
            // tới nơi. 26/28 command khác trong service này đều set 400 — đây là chỗ sót.
            response.StatusCode = 400;
        }
        return Task.FromResult(response);
    }
}
