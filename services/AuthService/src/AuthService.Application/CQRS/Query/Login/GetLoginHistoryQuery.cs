using AuthService.Application.DTOs.Response.Login;
using AuthService.Domain.Enums;
using MediatR;
using SharedContracts.Common.Requests;

namespace AuthService.Application.CQRS.Query.Login;

/// <summary>
/// Truy vấn login history của 1 account, có phân trang.
/// User chỉ xem được của mình; admin xem được của bất kỳ account.
/// Controller chịu trách nhiệm gán AccountId từ JWT (self) hoặc route (admin).
/// </summary>
public class GetLoginHistoryQuery : PaginationRequest, IRequest<LoginAttemptListResponse>
{
    /// <summary>Account id cần xem login history.</summary>
    public Guid AccountId { get; set; }

    /// <summary>Lọc theo kết quả (Success/WrongPassword/...).</summary>
    public LoginAttemptResult? Result { get; set; }

    /// <summary>Chỉ lấy fail attempts (Result != Success). Override nếu <see cref="Result"/> được set.</summary>
    public bool? OnlyFailed { get; set; }

    /// <summary>Từ thời điểm (UTC inclusive).</summary>
    public DateTime? FromUtc { get; set; }

    /// <summary>Đến thời điểm (UTC exclusive).</summary>
    public DateTime? ToUtc { get; set; }
}
