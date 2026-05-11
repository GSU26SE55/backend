using AuthService.Application.CQRS.Query.Audit;
using AuthService.Application.DTOs.Response.Audit;
using AuthService.Application.Interfaces.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace AuthService.Application.CQRS.Handler.Audit;

public class GetAuditLogsQueryHandler : IRequestHandler<GetAuditLogsQuery, AuditLogListResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;

    public GetAuditLogsQueryHandler(IAuthUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<AuditLogListResponse> Handle(GetAuditLogsQuery request, CancellationToken cancellationToken)
    {
        if (request.FromUtc.HasValue && request.ToUtc.HasValue && request.FromUtc.Value >= request.ToUtc.Value)
        {
            return new AuditLogListResponse
            {
                IsSuccess = false,
                StatusCode = 400,
                Message = "FromUtc phải nhỏ hơn ToUtc."
            };
        }

        var query = _unitOfWork.AuditLogs.GetAllAsync().AsNoTracking();

        if (request.Action.HasValue)
            query = query.Where(a => a.Action == request.Action.Value);

        if (request.TargetAccountId.HasValue)
            query = query.Where(a => a.TargetAccountId == request.TargetAccountId.Value);

        if (request.ActorAccountId.HasValue)
            query = query.Where(a => a.ActorAccountId == request.ActorAccountId.Value);

        if (request.IsSuccess.HasValue)
            query = query.Where(a => a.IsSuccess == request.IsSuccess.Value);

        if (request.FromUtc.HasValue)
            query = query.Where(a => a.CreatedAt >= request.FromUtc.Value);

        if (request.ToUtc.HasValue)
            query = query.Where(a => a.CreatedAt < request.ToUtc.Value);

        var totalItems = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(a => new AuditLogDto
            {
                Id = a.Id.ToString(),
                Action = a.Action,
                ActionName = a.Action.ToString(),
                TargetAccountId = a.TargetAccountId,
                TargetEmail = a.TargetEmail,
                ActorAccountId = a.ActorAccountId,
                IsSuccess = a.IsSuccess,
                Reason = a.Reason,
                MetadataJson = a.MetadataJson,
                IpAddress = a.IpAddress,
                UserAgent = a.UserAgent,
                DeviceId = a.DeviceId,
                CorrelationId = a.CorrelationId,
                CreatedAt = a.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return new AuditLogListResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new PaginationResponse<AuditLogDto>
            {
                Items = items,
                TotalItems = totalItems,
                PageNumber = request.PageNumber,
                PageSize = request.PageSize
            }
        };
    }
}
