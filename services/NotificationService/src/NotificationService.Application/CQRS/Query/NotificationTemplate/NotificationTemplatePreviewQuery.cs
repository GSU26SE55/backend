using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using NotificationService.Application.DTOs.Response.Notification;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace NotificationService.Application.CQRS.Query.NotificationTemplate;

/// <summary>
/// Dựng thử template với dữ liệu mẫu — <b>KHÔNG gửi đi đâu cả</b>, không đổi gì trong DB.
///
/// <para>Placeholder không có trong <see cref="SampleData"/> sẽ render ra rỗng — đó chính là cách
/// phát hiện template gọi sai tên biến.</para>
/// </summary>
public class NotificationTemplatePreviewQuery
    : IRequest<NotificationTemplatePreviewResponse>, IValidatable<NotificationTemplatePreviewResponse>
{
    /// <summary>Set từ route, không nhận từ body.</summary>
    [JsonIgnore]
    public Guid Id { get; set; }

    /// <summary>
    /// Cặp khoá–giá trị ứng với placeholder trong template
    /// (vd <c>{ "ticketCode": "TK-001", "priority": "P1" }</c>). Không gửi ⇒ render với model rỗng.
    /// </summary>
    public JsonElement? SampleData { get; set; }

    public Task<NotificationTemplatePreviewResponse> ValidateAsync()
    {
        var response = new NotificationTemplatePreviewResponse();

        if (Id == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "Id", Detail = "Invalid template Id." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
        }

        return Task.FromResult(response);
    }
}
