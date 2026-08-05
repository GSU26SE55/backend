using MediatR;
using NotificationService.Application.DTOs.Response.Setting;
using NotificationService.Domain.Enums;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace NotificationService.Application.CQRS.Command.Setting;

/// <summary>
/// Đổi đường vận chuyển push cho toàn hệ thống (ADR-0019). Chỉ Admin gọi được.
/// </summary>
public class UpdatePushTransportCommand : IRequest<PushTransportResponse>, IValidatable<PushTransportResponse>
{
    /// <summary>Giá trị mới: 1 = SignalR, 2 = Expo, 3 = Both.</summary>
    public PushTransportEnum Transport { get; set; }

    public Task<PushTransportResponse> ValidateAsync()
    {
        var response = new PushTransportResponse();

        // Enum.IsDefined chặn cả 0 (giá trị mặc định khi body thiếu trường) lẫn số ngoài dải.
        // Không có nó thì `{"transport": 99}` sẽ được ghi thẳng vào database và mọi lần gửi push
        // sau đó đều không khớp nhánh nào.
        if (!Enum.IsDefined(Transport))
            response.ListErrors.Add(new Errors
            {
                Field = "Transport",
                Detail = "Transport không hợp lệ. Chỉ nhận 1 (SignalR), 2 (Expo) hoặc 3 (Both).",
            });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
