using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.DTOs.Response.Auth;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedContracts.Common.Responses;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Handler.Auth;

public class RegisterCommandHandler : IRequestHandler<RegisterCommand, RegisterResponse>
{
    private const int OtpLifetimeMinutes = 5;

    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IMessageProducerService _messageProducer;
    private readonly ILogger<RegisterCommandHandler> _logger;

    public RegisterCommandHandler(
        IAuthUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        IMessageProducerService messageProducer,
        ILogger<RegisterCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _messageProducer = messageProducer;
        _logger = logger;
    }

    public async Task<RegisterResponse> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var phone = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim();

        var existing = await _unitOfWork.Accounts
            .GetAllAsync()
            .FirstOrDefaultAsync(a => a.Email.ToLower() == normalizedEmail && !a.IsDeleted, cancellationToken);

        if (existing != null && existing.Status != AccountStatusEnum.PendingVerification)
            return Fail(409, "Email", "Email đã được sử dụng.");

        if (!string.IsNullOrEmpty(phone))
        {
            var phoneOwnerId = await _unitOfWork.Accounts
                .GetAllAsync()
                .Where(a => a.PhoneNumber == phone && !a.IsDeleted)
                .Select(a => (Guid?)a.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (phoneOwnerId.HasValue && (existing == null || phoneOwnerId.Value != existing.Id))
                return Fail(409, "PhoneNumber", "Số điện thoại đã được sử dụng.");
        }

        var otp = OtpHelper.GenerateOtp(6);
        var otpExpiredAt = DateTime.UtcNow.AddMinutes(OtpLifetimeMinutes);

        if (existing == null)
        {
            var account = new Domain.Entities.Account
            {
                Id = Guid.NewGuid(),
                Email = normalizedEmail,
                PhoneNumber = phone,
                PasswordHash = _passwordHasher.Hash(request.Password),
                FullName = request.FullName.Trim(),
                AvatarUrl = null,
                DateOfBirth = request.DateOfBirth,
                Address = request.Address?.Trim(),
                EmailConfirmed = false,
                PhoneConfirmed = false,
                TwoFactorEnabled = false,
                OtpCode = otp,
                OtpExpiredAt = otpExpiredAt,
                OtpPurpose = OtpPurposeEnum.Register,
                FailedLoginAttempts = 0,
                LockoutEndAt = null,
                LastLoginAt = null,
                LastLoginIp = null,
                Status = AccountStatusEnum.PendingVerification,
                GoogleId = null,
                Provider = null
            };

            await _unitOfWork.Accounts.AddAsync(account);
        }
        else
        {
            existing.PhoneNumber = phone;
            existing.PasswordHash = _passwordHasher.Hash(request.Password);
            existing.FullName = request.FullName.Trim();
            existing.DateOfBirth = request.DateOfBirth;
            existing.Address = request.Address?.Trim();
            existing.OtpCode = otp;
            existing.OtpExpiredAt = otpExpiredAt;
            existing.OtpPurpose = OtpPurposeEnum.Register;
            existing.FailedLoginAttempts = 0;
            existing.LockoutEndAt = null;
            _unitOfWork.Accounts.UpdateAsync(existing);
        }

        // Outbox: publish TRƯỚC SaveChanges để event đi cùng transaction với Account.
        await _messageProducer.PublishAsync(new SendOtpRegisterEvent(normalizedEmail, otp), cancellationToken);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Fail(409, "Email", "Email hoặc số điện thoại đã được sử dụng.");
        }

        return new RegisterResponse
        {
            IsSuccess = true,
            StatusCode = 201,
            Message = "Đăng ký thành công. Vui lòng kiểm tra email để xác thực OTP.",
            Data = new RegisterResponseData
            {
                Email = normalizedEmail,
                OtpExpiresInSeconds = OtpLifetimeMinutes * 60
            }
        };
    }

    private static RegisterResponse Fail(int statusCode, string field, string message)
    {
        return new RegisterResponse
        {
            IsSuccess = false,
            StatusCode = statusCode,
            Message = message,
            ListErrors = { new Errors { Field = field, Detail = message } }
        };
    }
}
