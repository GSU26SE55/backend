using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Handler.Account;

public class CreateAccountCommandHandler : IRequestHandler<CreateAccountCommand, AccountActionResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IMessageProducerService _messageProducer;
    private readonly IPublisher _publisher;   // Sprint audit #AUDIT-11

    public CreateAccountCommandHandler(
        IAuthUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        IMessageProducerService messageProducer,
        IPublisher publisher)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _messageProducer = messageProducer;
        _publisher = publisher;
    }

    public async Task<AccountActionResponse> Handle(CreateAccountCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = EmailNormalizer.Normalize(request.Email);

        var emailExists = await _unitOfWork.Accounts
            .GetAllAsync()
            .AnyAsync(a => a.Email.ToLower() == normalizedEmail && !a.IsDeleted, cancellationToken);

        if (emailExists)
        {
            return new AccountActionResponse
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "Email is already in use.",
            };
        }

        if (!string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            var phone = PhoneNormalizer.Normalize(request.PhoneNumber);
            var phoneExists = await _unitOfWork.Accounts
                .GetAllAsync()
                .AnyAsync(a => a.PhoneNumber == phone && !a.IsDeleted, cancellationToken);

            if (phoneExists)
            {
                return new AccountActionResponse
                {
                    IsSuccess = false,
                    StatusCode = 409,
                    Message = "Phone number is already in use.",
                };
            }
        }

        var role = await _unitOfWork.Roles
            .GetAllAsync()
            .Where(r => r.Id == request.RoleId && r.Status == RoleStatusEnum.Active && !r.IsDeleted)
            .Select(r => new { r.Id, r.Name })
            .FirstOrDefaultAsync(cancellationToken);

        if (role == null)
        {
            return new AccountActionResponse
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Role does not exist or has been deactivated.",
            };
        }

        var account = new Domain.Entities.Account
        {
            Id = Guid.NewGuid(),
            Email = request.Email.Trim(),
            PasswordHash = _passwordHasher.Hash(request.Password),
            FullName = request.FullName.Trim(),
            PhoneNumber = PhoneNormalizer.Normalize(request.PhoneNumber).Length == 0 ? null : PhoneNormalizer.Normalize(request.PhoneNumber),
            DateOfBirth = request.DateOfBirth,
            Address = request.Address?.Trim(),
            EmailConfirmed = true,
            Status = AccountStatusEnum.Active,
            RoleId = role.Id,
            RoleAssignedAt = DateTime.UtcNow
        };

        await _unitOfWork.Accounts.AddAsync(account);

        // Outbox: publish AccountActivatedEvent TRƯỚC SaveChanges → atomic với business data.
        await _messageProducer.PublishAsync(new AccountActivatedEvent(
            account.Id,
            account.Email,
            account.FullName,
            account.PhoneNumber,
            role.Name,
            CreationSource: "AdminCreate"), cancellationToken);

        // #AUDIT-11
        await _publisher.Publish(new AuditTrailNotification(
            AuditActionEnum.AccountCreatedByAdmin, account.Id, true, TargetEmail: account.Email), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AccountActionResponse
        {
            IsSuccess = true,
            StatusCode = 201,
            Message = "Account created successfully.",
            Data = account.Id
        };
    }
}
