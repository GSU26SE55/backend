using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Application.CQRS.Handler.Account;

public class RevokeRoleCommandHandler : IRequestHandler<RevokeRoleCommand, AccountActionResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IPublisher _publisher;

    public RevokeRoleCommandHandler(IAuthUnitOfWork unitOfWork, IPublisher publisher)
    {
        _unitOfWork = unitOfWork;
        _publisher = publisher;
    }

    public async Task<AccountActionResponse> Handle(RevokeRoleCommand request, CancellationToken cancellationToken)
    {
        var assignment = await _unitOfWork.AccountRoles
            .GetAllAsync()
            .FirstOrDefaultAsync(ar => ar.AccountId == request.AccountId && ar.RoleId == request.RoleId, cancellationToken);

        if (assignment == null)
        {
            return new AccountActionResponse
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy gán role này cho tài khoản."
            };
        }

        _unitOfWork.AccountRoles.DeleteAsync(assignment);

        await _publisher.Publish(new AuditTrailNotification(
            AuditActionEnum.RoleRevoked, request.AccountId, IsSuccess: true,
            Metadata: new Dictionary<string, object?>
            {
                ["roleId"] = request.RoleId.ToString(),
                ["assignmentId"] = assignment.Id.ToString()
            }), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AccountActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Thu hồi role thành công.",
            Data = request.AccountId
        };
    }
}
