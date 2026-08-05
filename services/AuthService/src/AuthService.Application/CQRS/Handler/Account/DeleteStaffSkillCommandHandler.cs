using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Interfaces.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Handler.Account;

public class DeleteStaffSkillCommandHandler : IRequestHandler<DeleteStaffSkillCommand, AccountActionResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IMessageProducerService _messageProducer;

    public DeleteStaffSkillCommandHandler(IAuthUnitOfWork unitOfWork, IMessageProducerService messageProducer)
    {
        _unitOfWork = unitOfWork;
        _messageProducer = messageProducer;
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

        // GH-770 — xem AddStaffSkillCommandHandler: phát TOÀN BỘ tập còn lại, không phát "vừa xoá
        // mã X". Xoá mà không phát thì kỹ năng đã gỡ vẫn được dùng để giao việc mãi mãi.
        var remainingCodes = await _unitOfWork.StaffSkills
            .GetAllAsync()
            .Where(s => s.StaffAccountId == request.StaffAccountId && !s.IsDeleted && s.SkillCode != skillCode)
            .Select(s => s.SkillCode)
            .ToListAsync(cancellationToken);

        await _messageProducer.PublishAsync(
            new StaffSkillsUpdatedEvent(request.StaffAccountId, remainingCodes), cancellationToken);

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
