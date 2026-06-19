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
                Message = "Email đã được sử dụng.",
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
            PhoneNumber = PhoneNormalizer.Normalize(request.PhoneNumber).Length == 0 ? null : PhoneNormalizer.Normalize(request.PhoneNumber),
            EmailConfirmed = false,
            Status = AccountStatusEnum.PendingVerification,
            InvitationToken = invitationToken,
            InvitationExpiredAt = expiresAt,
            RoleId = role.Id,
            RoleAssignedAt = DateTime.UtcNow
        };

        await _unitOfWork.Accounts.AddAsync(account);

        await _messageProducer.PublishAsync(new SendAdminInviteEvent(
            account.Id,
            account.Email,
            account.FullName,
            role.Name,
            invitationToken,
            expiresAt), cancellationToken);

        await _publisher.Publish(new AuditTrailNotification(
            AuditActionEnum.AccountInviteSent, account.Id, IsSuccess: true,
            TargetEmail: account.Email,
            Metadata: new Dictionary<string, object?>
            {
                ["role"] = role.Name,
                ["roleId"] = role.Id.ToString(),
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
