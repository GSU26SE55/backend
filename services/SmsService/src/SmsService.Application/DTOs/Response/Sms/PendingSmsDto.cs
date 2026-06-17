namespace SmsService.Application.DTOs.Response.Sms;

/// <summary>
/// DTO trả về Flutter app khi claim batch SMS Pending.
/// </summary>
public record PendingSmsDto(Guid Id, string PhoneNumber, string Message);
