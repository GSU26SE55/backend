using AuthService.Application.CQRS.Query.Login;
using AuthService.Application.DTOs.Response.Login;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Requests;
using SharedContracts.Common.Responses;

namespace AuthService.Application.CQRS.Handler.Login;

public class GetLoginHistoryQueryHandler : IRequestHandler<GetLoginHistoryQuery, LoginAttemptListResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;

    public GetLoginHistoryQueryHandler(IAuthUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<LoginAttemptListResponse> Handle(GetLoginHistoryQuery request, CancellationToken cancellationToken)
    {
        if (request.AccountId == Guid.Empty)
        {
            return new LoginAttemptListResponse
            {
                IsSuccess = false,
                StatusCode = 400,
                Message = "AccountId không hợp lệ."
            };
        }

        if (request.FromUtc.HasValue && request.ToUtc.HasValue && request.FromUtc.Value >= request.ToUtc.Value)
        {
            return new LoginAttemptListResponse
            {
                IsSuccess = false,
                StatusCode = 422,
                Message = "FromUtc phải nhỏ hơn ToUtc."
            };
        }

        var query = _unitOfWork.LoginAttempts
            .GetAllAsync()
            .AsNoTracking()
            .Where(la => la.AccountId == request.AccountId);

        if (request.Result.HasValue)
            query = query.Where(la => la.Result == request.Result.Value);
        else if (request.OnlyFailed == true)
            query = query.Where(la => la.Result != LoginAttemptResult.Success);

        if (request.FromUtc.HasValue)
            query = query.Where(la => la.CreatedAt >= request.FromUtc.Value);

        if (request.ToUtc.HasValue)
            query = query.Where(la => la.CreatedAt < request.ToUtc.Value);

        var totalItems = await query.CountAsync(cancellationToken);

        var descending = SortHelper.IsDescending(request.SortDir);
        // Whitelist switch-case: createdAt (default) | result | method | ipAddress. Không dynamic LINQ.
        var ordered = (request.SortBy?.Trim().ToLowerInvariant()) switch
        {
            "result" => descending ? query.OrderByDescending(la => la.Result) : query.OrderBy(la => la.Result),
            "method" => descending ? query.OrderByDescending(la => la.Method) : query.OrderBy(la => la.Method),
            "ipaddress" => descending ? query.OrderByDescending(la => la.IpAddress) : query.OrderBy(la => la.IpAddress),
            _ => descending ? query.OrderByDescending(la => la.CreatedAt) : query.OrderBy(la => la.CreatedAt),
        };

        var items = await ordered
            .ThenBy(la => la.Id) // tie-breaker cố định — pagination ổn định
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(la => new LoginAttemptDto
            {
                Id = la.Id.ToString(),
                AccountId = la.AccountId,
                AttemptedEmail = la.AttemptedEmail,
                Result = la.Result,
                ResultName = la.Result.ToString(),
                Method = la.Method,
                IpAddress = la.IpAddress,
                UserAgent = la.UserAgent,
                DeviceId = la.DeviceId,
                Note = la.Note,
                CreatedAt = la.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return new LoginAttemptListResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new PaginationResponse<LoginAttemptDto>
            {
                Items = items,
                TotalItems = totalItems,
                PageNumber = request.PageNumber,
                PageSize = request.PageSize
            }
        };
    }
}
