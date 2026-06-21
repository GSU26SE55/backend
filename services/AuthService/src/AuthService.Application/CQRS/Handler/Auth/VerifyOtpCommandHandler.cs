using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using SharedInfrastructure.Metrics;

namespace AuthService.Application.CQRS.Handler.Auth;

public class VerifyOtpCommandHandler : IRequestHandler<VerifyOtpCommand, CommonResponse<string>>
{
    private const int MaxFailedAttempts = 5;
    private const int LockoutDurationMinutes = 15;

    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IMessageProducerService _messageProducer;

    public VerifyOtpCommandHandler(
        IAuthUnitOfWork unitOfWork,
        IMessageProducerService messageProducer)
    {
        _unitOfWork = unitOfWork;
        _messageProducer = messageProducer;
    }

    public async Task<CommonResponse<string>> Handle(VerifyOtpCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = EmailNormalizer.Normalize(request.Email);

        var account = await _unitOfWork.Accounts
            .GetAllAsync()
            .Include(a => a.Role)
            .FirstOrDefaultAsync(a => a.Email.ToLower() == normalizedEmail && !a.IsDeleted, cancellationToken);

        if (account == null)
            return Fail(404, "Tài khoản không tồn tại.");

        if (account.Status != AccountStatusEnum.PendingVerification)
            return Fail(409, "Tài khoản đã được xác thực hoặc không ở trạng thái chờ verify.");

        if (account.LockoutEndAt.HasValue && account.LockoutEndAt.Value > DateTime.UtcNow)
        {
            var minutesLeft = (int)Math.Ceiling((account.LockoutEndAt.Value - DateTime.UtcNow).TotalMinutes);
            return Fail(423, $"Tài khoản đang bị khóa. Vui lòng thử lại sau {minutesLeft} phút.");
        }

        // #AUTH-27: dùng <= để chặn edge case on-exact-expiry.
        if (!account.OtpExpiredAt.HasValue || account.OtpExpiredAt.Value <= DateTime.UtcNow)
            return Fail(401, "OTP đã hết hạn. Vui lòng yêu cầu gửi lại.");

        if (account.OtpPurpose != OtpPurposeEnum.Register)
            return Fail(422, "OTP không phải dành cho đăng ký.");

        if (!SecureCompareHelper.FixedTimeEquals(account.OtpCode, request.Otp.Trim()))
        {
            account.FailedLoginAttempts += 1;
            if (account.FailedLoginAttempts >= MaxFailedAttempts)
                account.LockoutEndAt = DateTime.UtcNow.AddMinutes(LockoutDurationMinutes);

            _unitOfWork.Accounts.UpdateAsync(account);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            AppMetrics.AuthOtpUsageTotal.WithLabels("register", "wrong").Inc(); // #AUTH-78

            if (account.LockoutEndAt.HasValue && account.LockoutEndAt.Value > DateTime.UtcNow)
                return Fail(423, $"Sai OTP quá {MaxFailedAttempts} lần. Tài khoản bị khóa {LockoutDurationMinutes} phút.");

            var remaining = MaxFailedAttempts - account.FailedLoginAttempts;
            return Fail(401, $"OTP không chính xác. Còn {remaining} lần thử.");
        }

        AppMetrics.AuthOtpUsageTotal.WithLabels("register", "verified").Inc(); // #AUTH-78

        account.EmailConfirmed = true;
        account.OtpCode = null;
        account.OtpExpiredAt = null;
        account.OtpPurpose = null;
        account.FailedLoginAttempts = 0;
        account.LockoutEndAt = null;
        account.Status = AccountStatusEnum.Active;

        // #AUTH-69: RoleId giờ nullable (Guid?). Self-register: chưa gán role → default Customer.
        // Check cả null lẫn Guid.Empty cho backward-compat với row legacy có RoleId=Empty.
        // Resolve Customer role theo NormalizedName tại runtime — KHÔNG hardcode GUID (xem SystemRoleResolver).
        Guid? assignedCustomerRoleId = null;
        if (!account.RoleId.HasValue || account.RoleId.Value == Guid.Empty)
        {
            assignedCustomerRoleId = await SystemRoleResolver.ResolveCustomerRoleIdAsync(_unitOfWork, cancellationToken);
            if (assignedCustomerRoleId is null)
                return Fail(500, "Xác thực thất bại do lỗi hệ thống. Vui lòng thử lại.");

            account.RoleId = assignedCustomerRoleId.Value;
            account.RoleAssignedAt = DateTime.UtcNow;
        }
        _unitOfWork.Accounts.UpdateAsync(account);

        var roleName = account.Role?.Name
                       ?? (assignedCustomerRoleId.HasValue ? "Customer" : string.Empty);

        // Outbox: publish AccountActivatedEvent TRƯỚC SaveChanges để event đi cùng transaction.
        await _messageProducer.PublishAsync(new AccountActivatedEvent(
            account.Id,
            account.Email,
            account.FullName,
            account.PhoneNumber,
            roleName,
            CreationSource: "SelfRegister"), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CommonResponse<string>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Xác thực OTP thành công. Tài khoản đã kích hoạt. Vui lòng đăng nhập."
        };
    }

    private static CommonResponse<string> Fail(int statusCode, string message)
    {
        return new CommonResponse<string>
        {
            IsSuccess = false,
            StatusCode = statusCode,
            Message = message,
        };
    }
}
