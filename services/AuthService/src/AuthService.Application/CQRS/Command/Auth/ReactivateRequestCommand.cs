using MediatR;
using SharedContracts.Common.Responses;

namespace AuthService.Application.CQRS.Command.Auth;

/// <summary>
/// #AUTH-50: bước 1 reactivate — user submit email của soft-deleted account.
/// Server tìm account trong soft-delete window 90 ngày, gửi OTP nếu hợp lệ.
/// Trả message generic kể cả khi không tìm thấy để chống enumeration.
/// </summary>
public class ReactivateRequestCommand : IRequest<CommonResponse<string>>
{
    public string Email { get; set; } = string.Empty;
}
