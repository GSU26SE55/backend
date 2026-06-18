using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Interfaces.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace AuthService.Application.CQRS.Handler.Account;

public class DeleteStaffSkillCommandHandler : IRequestHandler<DeleteStaffSkillCommand, AccountActionResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;

    public DeleteStaffSkillCommandHandler(IAuthUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<AccountActionResponse> Handle(DeleteStaffSkillCommand request, CancellationToken cancellationToken)
    {
        // #AUTH-36: ValidationBehavior pipeline đã chạy ValidateAsync TRƯỚC handler.

        var skillCode = request.SkillCode.Trim();
        var skill = await _unitOfWork.StaffSkills
            .GetAllAsync()
            .FirstOrDefaultAsync(s => s.StaffAccountId == request.StaffAccountId && s.SkillCode == skillCode && !s.IsDeleted, cancellationToken);

        if (skill is null)
            return Fail(404, "Không tìm thấy staff skill.");

        _unitOfWork.StaffSkills.DeleteAsync(skill);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AccountActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Xóa staff skill thành công.",
            Data = request.StaffAccountId
        };
    }

    private static AccountActionResponse Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message,
    };
}
