namespace SmsService.Application.DTOs.Response.Admin;

/// <summary>
/// Response của admin khi tạo gateway device — chứa <c>ApiKey</c> plaintext **HIỂN THỊ 1 LẦN DUY NHẤT**.
/// Lần GET sau chỉ thấy hash trong DB, không bao giờ trả plaintext lại.
/// </summary>
public record CreateGatewayDeviceResponseDto(Guid Id, string DeviceCode, string ApiKey);
