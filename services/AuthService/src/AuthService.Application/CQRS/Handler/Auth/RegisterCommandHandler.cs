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
    private static readonly Guid CustomerRoleId = Guid.Parse("44444444-4444-4444-4444-444444444444");

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
        var normalizedEmail = EmailNormalizer.Normalize(request.Email);
        var phone = PhoneNormalizer.Normalize(request.PhoneNumber).Length == 0 ? null : PhoneNormalizer.Normalize(request.PhoneNumber);

        var existing = await _unitOfWork.Accounts
            .GetAllAsync()
            .FirstOrDefaultAsync(a => a.Email.ToLower() == normalizedEmail && !a.IsDeleted, cancellationToken);

        if (existing != null && existing.Status != AccountStatusEnum.PendingVerification)
            return Fail(409, "Email đã được sử dụng.");

        if (!string.IsNullOrEmpty(phone))
        {
            var phoneOwnerId = await _unitOfWork.Accounts
                .GetAllAsync()
                .Where(a => a.PhoneNumber == phone && !a.IsDeleted)
                .Select(a => (Guid?)a.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (phoneOwnerId.HasValue && (existing == null || phoneOwnerId.Value != existing.Id))
                return Fail(409, "Số điện thoại đã được sử dụng.");
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
                Provider = null,
                // 1-N refactor: account bắt buộc có 1 role. Self-register default Customer.
                RoleId = CustomerRoleId,
                RoleAssignedAt = DateTime.UtcNow
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
            // #AUTH-69: RoleId nullable. Check cả null lẫn Guid.Empty (legacy row backward-compat).
            if (!existing.RoleId.HasValue || existing.RoleId.Value == Guid.Empty)
            {
                existing.RoleId = CustomerRoleId;
                existing.RoleAssignedAt = DateTime.UtcNow;
            }
            _unitOfWork.Accounts.UpdateAsync(existing);
        }

        // Outbox: publish TRƯỚC SaveChanges để event đi cùng transaction với Account.
        await _messageProducer.PublishAsync(new SendOtpRegisterEvent(normalizedEmail, otp), cancellationToken);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // #AUTH-25: race condition — 2 request đồng thời pass duplicate check rồi cùng INSERT.
            // PG raise 23505 (unique violation). Parse ConstraintName để phân biệt email vs phone.
            // Inner exception là Npgsql.PostgresException; access qua reflection để tránh add Npgsql
            // package vào Application layer.
            var sqlState = ExtractSqlState(ex);
            var constraint = ExtractConstraintName(ex);
            if (sqlState == "23505")
            {
                if (!string.IsNullOrEmpty(constraint))
                {
                    var c = constraint.ToLowerInvariant();
                    if (c.Contains("email"))
                        return Fail(409, "Email đã được sử dụng.");
                    if (c.Contains("phone"))
                        return Fail(409, "Số điện thoại đã được sử dụng.");
                }
                return Fail(409, "Email hoặc số điện thoại đã được sử dụng.");
            }
            // Lỗi DB khác → log + 500.
            _logger.LogError(ex, "Register SaveChanges failed with non-unique-violation DB error.");
            return Fail(500, "Đăng ký thất bại do lỗi hệ thống. Vui lòng thử lại.");
        }

        return BuildSuccess(normalizedEmail);
    }

    private static RegisterResponse BuildSuccess(string normalizedEmail) => new()
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

    // #AUTH-25: trích SqlState + ConstraintName từ Npgsql.PostgresException qua reflection
    // (giữ Application layer không phụ thuộc Npgsql package).
    private static string? ExtractSqlState(Exception ex)
    {
        var inner = ex.InnerException;
        while (inner != null)
        {
            var typeName = inner.GetType().FullName ?? string.Empty;
            if (typeName.Contains("PostgresException", StringComparison.Ordinal))
            {
                var prop = inner.GetType().GetProperty("SqlState");
                return prop?.GetValue(inner) as string;
            }
            inner = inner.InnerException;
        }
        return null;
    }

    private static string? ExtractConstraintName(Exception ex)
    {
        var inner = ex.InnerException;
        while (inner != null)
        {
            var typeName = inner.GetType().FullName ?? string.Empty;
            if (typeName.Contains("PostgresException", StringComparison.Ordinal))
            {
                var prop = inner.GetType().GetProperty("ConstraintName");
                return prop?.GetValue(inner) as string;
            }
            inner = inner.InnerException;
        }
        return null;
    }

    private static RegisterResponse Fail(int statusCode, string message)
    {
        return new RegisterResponse
        {
            IsSuccess = false,
            StatusCode = statusCode,
            Message = message,
        };
    }
}
