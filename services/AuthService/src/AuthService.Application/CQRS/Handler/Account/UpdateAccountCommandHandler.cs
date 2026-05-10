using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Interfaces.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace AuthService.Application.CQRS.Handler.Account;

public class UpdateAccountCommandHandler : IRequestHandler<UpdateAccountCommand, AccountActionResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;

    public UpdateAccountCommandHandler(IAuthUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<AccountActionResponse> Handle(UpdateAccountCommand request, CancellationToken cancellationToken)
    {
        var account = await _unitOfWork.Accounts.GetByIdAsync(request.Id);
        if (account == null)
        {
            return new AccountActionResponse
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy tài khoản."
            };
        }

        if (!string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            var phone = request.PhoneNumber.Trim();
            var duplicated = await _unitOfWork.Accounts
                .GetAllAsync()
                .AnyAsync(a => a.Id != request.Id && a.PhoneNumber == phone, cancellationToken);

            if (duplicated)
            {
                return new AccountActionResponse
                {
                    IsSuccess = false,
                    StatusCode = 409,
                    Message = "Số điện thoại đã được sử dụng.",
                    ListErrors = { new Errors { Field = "PhoneNumber", Detail = "Số điện thoại đã được sử dụng." } }
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
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AccountActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Cập nhật tài khoản thành công.",
            Data = account.Id
        };
    }
}
