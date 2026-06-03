using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Handler.Auth;

public class VerifyOtpCommandHandler : IRequestHandler<VerifyOtpCommand, CommonResponse<string>>
{
    private const int MaxFailedAttempts = 5;
    private const int LockoutDurationMinutes = 15;
    private static readonly Guid CustomerRoleId = Guid.Parse("44444444-4444-4444-4444-444444444444");

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
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var account = await _unitOfWork.Accounts
            .GetAllAsync()
            .Include(a => a.Role)
            .FirstOrDefaultAsync(a => a.Email.ToLower() == normalizedEmail && !a.IsDeleted, cancellationToken);

        if (account == null)
            return Fail(404, "Tài khoản không tồn tại.");

        if (account.Status != AccountStatusEnum.PendingVerification)
            return Fail(400, "Tài khoản đã được xác thực hoặc không ở trạng thái chờ verify.");

        if (account.LockoutEndAt.HasValue && account.LockoutEndAt.Value > DateTime.UtcNow)
        {
            var minutesLeft = (int)Math.Ceiling((account.LockoutEndAt.Value - DateTime.UtcNow).TotalMinutes);
            return Fail(423, $"Tài khoản đang bị khóa. Vui lòng thử lại sau {minutesLeft} phút.");
        }

        if (!account.OtpExpiredAt.HasValue || account.OtpExpiredAt.Value < DateTime.UtcNow)
            return Fail(400, "OTP đã hết hạn. Vui lòng yêu cầu gửi lại.");

        if (account.OtpPurpose != OtpPurposeEnum.Register)
            return Fail(400, "OTP không phải dành cho đăng ký.");

        if (!string.Equals(account.OtpCode, request.Otp.Trim(), StringComparison.Ordinal))
        {
            account.FailedLoginAttempts += 1;
            if (account.FailedLoginAttempts >= MaxFailedAttempts)
                account.LockoutEndAt = DateTime.UtcNow.AddMinutes(LockoutDurationMinutes);

            _unitOfWork.Accounts.UpdateAsync(account);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (account.LockoutEndAt.HasValue && account.LockoutEndAt.Value > DateTime.UtcNow)
                return Fail(423, $"Sai OTP quá {MaxFailedAttempts} lần. Tài khoản bị khóa {LockoutDurationMinutes} phút.");

            var remaining = MaxFailedAttempts - account.FailedLoginAttempts;
            return Fail(401, $"OTP không chính xác. Còn {remaining} lần thử.");
        }

        account.EmailConfirmed = true;
        account.OtpCode = null;
        account.OtpExpiredAt = null;
        account.OtpPurpose = null;
        account.FailedLoginAttempts = 0;
        account.LockoutEndAt = null;
        account.Status = AccountStatusEnum.Active;

        // Self-register: nếu account chưa gán role (RoleId rỗng) → default Customer.
        if (account.RoleId == Guid.Empty)
        {
            account.RoleId = CustomerRoleId;
            account.RoleAssignedAt = DateTime.UtcNow;
        }
        _unitOfWork.Accounts.UpdateAsync(account);

        var roleName = account.Role?.Name
                       ?? (account.RoleId == CustomerRoleId ? "Customer" : string.Empty);

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

    private static CommonResponse<string> Fail(int statusCode, string message, string field = "Auth")
    {
        return new CommonResponse<string>
        {
            IsSuccess = false,
            StatusCode = statusCode,
            Message = message,
            ListErrors = new List<Errors> { new Errors { Field = field, Detail = message } }
        };
    }
}
