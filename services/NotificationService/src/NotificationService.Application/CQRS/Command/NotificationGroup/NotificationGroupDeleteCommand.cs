using System.Text.Json.Serialization;
using MediatR;
using NotificationService.Application.DTOs.Response.Notification;
using SharedContracts.Interfaces;

namespace NotificationService.Application.CQRS.Command.NotificationGroup;

/// <summary>
/// Sprint 6.4 NOTI4-02 — xoá mềm một nhóm và toàn bộ thành viên của nó.
///
/// <para>Nhóm hệ thống bị từ chối bằng <b>409</b>: 4 nhóm role là chỗ dựa của
/// <c>RecipientResolver</c>, xoá đi thì 13 consumer broadcast mất sạch người nhận và lại rơi vào
/// đúng nhánh "không có người nhận → bỏ qua im lặng" đã từng giấu lỗi suốt một thời gian dài.</para>
///
/// <para><b>Lịch sử gửi KHÔNG bị ảnh hưởng.</b> Khoá ngoại <c>ON DELETE CASCADE</c> chỉ nối
/// nhóm → thành viên; các lần gửi đã thực hiện nằm ở bảng khác và giữ nguyên.</para>
/// </summary>
public class NotificationGroupDeleteCommand
    : IRequest<NotificationGroupActionResponse>, IValidatable<NotificationGroupActionResponse>
{
    [JsonIgnore]
    public Guid Id { get; set; }

    [JsonIgnore]
    public Guid ActorUserId { get; set; }

    public Task<NotificationGroupActionResponse> ValidateAsync()
    {
        var response = new NotificationGroupActionResponse();
        NotificationGroupRules.ValidateActor(response.ListErrors, ActorUserId);

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
        }

        return Task.FromResult(response);
    }
}
