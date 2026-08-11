using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Handler.Account;

public class DeleteMeCommandHandler : IRequestHandler<DeleteMeCommand, AccountActionResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IMessageProducerService _messageProducer;
    private readonly IPublisher _publisher;   // Sprint audit #AUDIT-11

    public DeleteMeCommandHandler(IAuthUnitOfWork unitOfWork, IMessageProducerService messageProducer, IPublisher publisher)
    {
        _unitOfWork = unitOfWork;
        _messageProducer = messageProducer;
        _publisher = publisher;
    }

    public async Task<AccountActionResponse> Handle(DeleteMeCommand request, CancellationToken cancellationToken)
    {
        var account = await _unitOfWork.Accounts
            .GetAllAsync()
            .FirstOrDefaultAsync(a => a.Id == request.AccountId && !a.IsDeleted, cancellationToken);
        if (account == null)
        {
            return new AccountActionResponse
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Account not found."
            };
        }

        var activeTokens = await _unitOfWork.RefreshTokens
            .GetAllAsync()
            .Where(rt => rt.AccountId == account.Id && rt.Status == RefreshTokenStatus.Active)
            .ToListAsync(cancellationToken);

        foreach (var rt in activeTokens)
        {
            rt.Status = RefreshTokenStatus.Revoked;
            rt.RevokedAt = DateTime.UtcNow;
            rt.RevokedReason = "Self deleted account";
            _unitOfWork.RefreshTokens.UpdateAsync(rt);
        }

        // Soft delete — interceptor sẽ set IsDeleted=true + DeletedAt
        _unitOfWork.Accounts.DeleteAsync(account);

        await _messageProducer.PublishAsync(new AccountDeletedEvent(
            account.Id, account.Email, DeletionSource: "SelfDelete"), cancellationToken);

        // #AUDIT-11
        await _publisher.Publish(new AuditTrailNotification(
            AuditActionEnum.AccountDeleted, account.Id, true, TargetEmail: account.Email), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AccountActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Account deleted.",
            Data = account.Id
        };
    }
}
