using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Handler.Account;

public class UpdateAccountCommandHandler : IRequestHandler<UpdateAccountCommand, AccountActionResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IMessageProducerService _messageProducer;
    private readonly IPublisher _publisher;   // Sprint audit #AUDIT-11

    public UpdateAccountCommandHandler(IAuthUnitOfWork unitOfWork, IMessageProducerService messageProducer, IPublisher publisher)
    {
        _unitOfWork = unitOfWork;
        _messageProducer = messageProducer;
        _publisher = publisher;
    }

    public async Task<AccountActionResponse> Handle(UpdateAccountCommand request, CancellationToken cancellationToken)
    {
        var account = await _unitOfWork.Accounts
            .GetAllAsync()
            .FirstOrDefaultAsync(a => a.Id == request.Id && !a.IsDeleted, cancellationToken);
        if (account == null)
        {
            return new AccountActionResponse
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Account not found."
            };
        }

        if (!string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            var phone = PhoneNormalizer.Normalize(request.PhoneNumber);
            var duplicated = await _unitOfWork.Accounts
                .GetAllAsync()
                .AnyAsync(a => a.Id != request.Id && a.PhoneNumber == phone && !a.IsDeleted, cancellationToken);

            if (duplicated)
            {
                return new AccountActionResponse
                {
                    IsSuccess = false,
                    StatusCode = 409,
                    Message = "Phone number is already in use.",
                };
            }

            if (account.PhoneNumber != phone)
                account.PhoneConfirmed = false;

            account.PhoneNumber = phone;
        }
        else
        {
            account.PhoneNumber = null;
            account.PhoneConfirmed = false;
        }

        account.FullName = request.FullName.Trim();
        account.AvatarUrl = request.AvatarUrl?.Trim();
        account.DateOfBirth = request.DateOfBirth;
        account.Address = request.Address?.Trim();

        _unitOfWork.Accounts.UpdateAsync(account);

        await _messageProducer.PublishAsync(new AccountProfileUpdatedEvent(
            account.Id,
            account.Email,
            account.FullName,
            account.PhoneNumber,
            account.AvatarUrl), cancellationToken);

        // #AUDIT-11
        await _publisher.Publish(new AuditTrailNotification(
            AuditActionEnum.AccountUpdated, account.Id, true, TargetEmail: account.Email), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AccountActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Account updated successfully.",
            Data = account.Id
        };
    }
}
