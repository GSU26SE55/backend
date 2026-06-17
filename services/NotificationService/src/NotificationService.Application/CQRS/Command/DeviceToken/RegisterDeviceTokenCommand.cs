using MediatR;
using NotificationService.Application.DTOs.Response.DeviceToken;
using NotificationService.Domain.Enums;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace NotificationService.Application.CQRS.Command.DeviceToken;

/// <summary>
/// Đăng ký push token của thiết bị (Expo/FCM) cho user hiện tại. UserId set từ JWT claim ở controller.
/// </summary>
public class RegisterDeviceTokenCommand : IRequest<DeviceTokenActionResponse>, IValidatable<DeviceTokenActionResponse>
{
    /// <summary>Set từ JWT claim, không nhận từ body.</summary>
    public Guid UserId { get; set; }

    public string Token { get; set; } = string.Empty;
    public DevicePlatformEnum Platform { get; set; }
    public string? DeviceInfo { get; set; }

    public Task<DeviceTokenActionResponse> ValidateAsync()
    {
        var response = new DeviceTokenActionResponse();

        if (UserId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "UserId", Detail = "Không xác định được user." });

        if (string.IsNullOrWhiteSpace(Token))
            response.ListErrors.Add(new Errors { Field = "Token", Detail = "Token không được trống." });
        else if (Token.Length > 500)
            response.ListErrors.Add(new Errors { Field = "Token", Detail = "Token tối đa 500 ký tự." });

        if (!Enum.IsDefined(typeof(DevicePlatformEnum), Platform))
            response.ListErrors.Add(new Errors { Field = "Platform", Detail = "Platform không hợp lệ." });

        if (!string.IsNullOrEmpty(DeviceInfo) && DeviceInfo.Length > 500)
            response.ListErrors.Add(new Errors { Field = "DeviceInfo", Detail = "DeviceInfo tối đa 500 ký tự." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
