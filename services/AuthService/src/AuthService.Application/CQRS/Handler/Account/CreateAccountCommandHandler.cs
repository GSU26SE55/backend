using AuthService.Application.CQRS.Command.Account;
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

    public CreateAccountCommandHandler(
        IAuthUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        IMessageProducerService messageProducer)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _messageProducer = messageProducer;
    }

    public async Task<AccountActionResponse> Handle(CreateAccountCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var emailExists = await _unitOfWork.Accounts
            .GetAllAsync()
            .AnyAsync(a => a.Email.ToLower() == normalizedEmail && !a.IsDeleted, cancellationToken);

        if (emailExists)
        {
            return new AccountActionResponse
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "Email đã được sử dụng.",
            };
        }

        if (!string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            var phone = request.PhoneNumber.Trim();
            var phoneExists = await _unitOfWork.Accounts
                .GetAllAsync()
                .AnyAsync(a => a.PhoneNumber == phone && !a.IsDeleted, cancellationToken);

            if (phoneExists)
            {
                return new AccountActionResponse
                {
                    IsSuccess = false,
                    StatusCode = 409,
                    Message = "Số điện thoại đã được sử dụng.",
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
                Message = "Role không tồn tại hoặc đã bị vô hiệu hóa.",
            };
        }

        var account = new Domain.Entities.Account
        {
            Id = Guid.NewGuid(),
            Email = request.Email.Trim(),
            PasswordHash = _passwordHasher.Hash(request.Password),
            FullName = request.FullName.Trim(),
            PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim(),
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

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AccountActionResponse
        {
            IsSuccess = true,
            StatusCode = 201,
            Message = "Tạo tài khoản thành công.",
            Data = account.Id
        };
    }
}
