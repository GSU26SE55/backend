using AuthService.Application.DTOs.Response.Account;
using MediatR;

namespace AuthService.Application.CQRS.Query.Account;

/// <summary>
/// Snapshot thống kê account (total + count theo role) cho Dashboard Admin — không nhận tham số (snapshot hiện tại).
/// </summary>
public class GetAccountStatsQuery : IRequest<AccountStatsResponse>
{
}
