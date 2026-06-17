using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using SmsService.Application.DTOs.Response.Admin;

namespace SmsService.Application.CQRS.Command.Admin;

/// <summary>
/// Admin tạo device gateway mới. Backend sinh API key plaintext (32 byte random base64),
/// trả về <strong>1 lần duy nhất</strong> trong response (DB chỉ lưu BCrypt hash).
/// </summary>
public class CreateGatewayDeviceCommand : IRequest<CommonResponse<CreateGatewayDeviceResponseDto>>, IValidatable<CommonResponse<CreateGatewayDeviceResponseDto>>
{
    public string DeviceName { get; set; } = string.Empty;
    public string DeviceCode { get; set; } = string.Empty;
    public int DailyLimit { get; set; } = 100;

    public Task<CommonResponse<CreateGatewayDeviceResponseDto>> ValidateAsync()
    {
        var response = new CommonResponse<CreateGatewayDeviceResponseDto>();

        if (string.IsNullOrWhiteSpace(DeviceName))
            response.ListErrors.Add(new Errors { Field = nameof(DeviceName), Detail = "Bắt buộc." });
        else if (DeviceName.Length > 64)
            response.ListErrors.Add(new Errors { Field = nameof(DeviceName), Detail = "Tối đa 64 ký tự." });

        if (string.IsNullOrWhiteSpace(DeviceCode))
            response.ListErrors.Add(new Errors { Field = nameof(DeviceCode), Detail = "Bắt buộc." });
        else if (DeviceCode.Length > 64)
            response.ListErrors.Add(new Errors { Field = nameof(DeviceCode), Detail = "Tối đa 64 ký tự." });

        if (DailyLimit < 1 || DailyLimit > 10000)
            response.ListErrors.Add(new Errors { Field = nameof(DailyLimit), Detail = "Phải trong khoảng [1..10000]." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu không hợp lệ.";
        }
        return Task.FromResult(response);
    }
}
