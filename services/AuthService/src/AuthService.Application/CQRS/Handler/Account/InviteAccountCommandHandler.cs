using System.Security.Cryptography;
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

public class InviteAccountCommandHandler : IRequestHandler<InviteAccountCommand, AccountActionResponse>
{
    private const int InvitationLifetimeHours = 72;
    private const int TokenByteLength = 48; // 64 chars base64url after encode

    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IMessageProducerService _messageProducer;
    private readonly IPublisher _publisher;

    public InviteAccountCommandHandler(
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

    public async Task<AccountActionResponse> Handle(InviteAccountCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var emailExists = await _unitOfWork.Accounts
            .GetAllAsync()
            .AnyAsync(a => a.Email.ToLower() == normalizedEmail, cancellationToken);

        if (emailExists)
        {
            return new AccountActionResponse
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "Email đã được sử dụng.",
                ListErrors = { new Errors { Field = "Email", Detail = "Email đã được sử dụng." } }
            };
        }

        if (!string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            var phone = request.PhoneNumber.Trim();
            var phoneExists = await _unitOfWork.Accounts
                .GetAllAsync()
                .AnyAsync(a => a.PhoneNumber == phone, cancellationToken);

            if (phoneExists)
            {
                return new AccountActionResponse
                {
                    IsSuccess = false,
                    StatusCode = 409,
                    Message = "Số điện thoại đã được sử dụng.",
                    ListErrors = { new Errors { Field = "PhoneNumber", Detail = "Số điện thoại đã được sử dụng." } }
                };
            }
        }

        var validRoles = await _unitOfWork.Roles
            .GetAllAsync()
            .Where(r => request.RoleIds.Contains(r.Id) && r.Status == RoleStatusEnum.Active)
            .Select(r => new { r.Id, r.Name })
            .ToListAsync(cancellationToken);

        var validRoleIds = validRoles.Select(r => r.Id).ToList();
        var missing = request.RoleIds.Except(validRoleIds).ToList();
        if (missing.Count > 0)
        {
            return new AccountActionResponse
            {
                IsSuccess = false,
                StatusCode = 400,
                Message = $"Có {missing.Count} role không tồn tại hoặc đã bị vô hiệu hóa."
            };
        }

        var invitationToken = GenerateUrlSafeToken(TokenByteLength);
        var expiresAt = DateTime.UtcNow.AddHours(InvitationLifetimeHours);

        // Tạo password placeholder ngẫu nhiên — user PHẢI set password mới khi accept invite,
        // placeholder không bao giờ được dùng để login (account ở PendingVerification).
        var placeholderPassword = _passwordHasher.Hash(Guid.NewGuid().ToString("N"));

        var account = new Domain.Entities.Account
        {
            Id = Guid.NewGuid(),
            Email = normalizedEmail,
            PasswordHash = placeholderPassword,
            FullName = request.FullName.Trim(),
            PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim(),
            EmailConfirmed = false,
            Status = AccountStatusEnum.PendingVerification,
            InvitationToken = invitationToken,
            InvitationExpiredAt = expiresAt
        };

        await _unitOfWork.Accounts.AddAsync(account);

        foreach (var roleId in validRoleIds)
        {
            await _unitOfWork.AccountRoles.AddAsync(new AccountRole
            {
                Id = Guid.NewGuid(),
                AccountId = account.Id,
                RoleId = roleId,
                AssignedAt = DateTime.UtcNow,
                IsActive = true
            });
        }

        var roleNames = validRoles.Select(r => r.Name).ToList();

        await _messageProducer.PublishAsync(new SendAdminInviteEvent(
            account.Id,
            account.Email,
            account.FullName,
            roleNames,
            invitationToken,
            expiresAt), cancellationToken);

        await _publisher.Publish(new AuditTrailNotification(
            AuditActionEnum.AccountInviteSent, account.Id, IsSuccess: true,
            TargetEmail: account.Email,
            Metadata: new Dictionary<string, object?>
            {
                ["roles"] = roleNames,
                ["expiresAt"] = expiresAt
            }), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AccountActionResponse
        {
            IsSuccess = true,
            StatusCode = 201,
            Message = "Đã gửi email invite. User cần accept để kích hoạt tài khoản.",
            Data = account.Id
        };
    }

    /// <summary>
    /// Sinh token base64url an toàn để dùng trong URL (no padding, no +/= chars).
    /// </summary>
    private static string GenerateUrlSafeToken(int byteLength)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteLength);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
