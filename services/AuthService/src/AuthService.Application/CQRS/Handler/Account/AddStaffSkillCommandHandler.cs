using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Handler.Account;

public class AddStaffSkillCommandHandler : IRequestHandler<AddStaffSkillCommand, AccountActionResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IMessageProducerService _messageProducer;

    public AddStaffSkillCommandHandler(IAuthUnitOfWork unitOfWork, IMessageProducerService messageProducer)
    {
        _unitOfWork = unitOfWork;
        _messageProducer = messageProducer;
    }

    public async Task<AccountActionResponse> Handle(AddStaffSkillCommand request, CancellationToken cancellationToken)
    {
        // #AUTH-36: ValidationBehavior pipeline đã chạy ValidateAsync TRƯỚC handler.

        var accountExists = await _unitOfWork.Accounts
            .GetAllAsync()
            .AnyAsync(a => a.Id == request.StaffAccountId && !a.IsDeleted, cancellationToken);

        if (!accountExists)
            return Fail(404, "Không tìm thấy tài khoản.");

        var staffProfile = await _unitOfWork.StaffProfiles
            .GetAllAsync()
            .FirstOrDefaultAsync(profile => profile.AccountId == request.StaffAccountId && !profile.IsDeleted, cancellationToken);

        if (staffProfile is null)
        {
            staffProfile = new StaffProfile
            {
                Id = Guid.NewGuid(),
                AccountId = request.StaffAccountId
            };
            await _unitOfWork.StaffProfiles.AddAsync(staffProfile);
        }

        var skillCode = request.SkillCode.Trim();
        var skill = await _unitOfWork.StaffSkills
            .GetAllAsync()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.StaffAccountId == request.StaffAccountId && s.SkillCode == skillCode, cancellationToken);

        var createdSkill = skill is null;
        if (createdSkill)
        {
            skill = new StaffSkill
            {
                Id = Guid.NewGuid(),
                StaffAccountId = request.StaffAccountId,
                SkillCode = skillCode
            };
            await _unitOfWork.StaffSkills.AddAsync(skill);
        }
        else if (skill is not null)
        {
            skill.IsDeleted = false;
            skill.DeletedAt = null;
        }

        if (skill is null)
            throw new InvalidOperationException("StaffSkill could not be created.");

        skill.SkillLevel = request.SkillLevel;
        skill.CertifiedUntil = request.CertifiedUntil;

        if (!createdSkill)
            _unitOfWork.StaffSkills.UpdateAsync(skill);

        // GH-770 — TicketService có sẵn consumer StaffSkillsUpdatedEvent nhưng KHÔNG NƠI NÀO phát,
        // nên định tuyến/giao việc theo kỹ năng dùng dữ liệu cũ vĩnh viễn.
        //
        // Phát TOÀN BỘ tập kỹ năng sau thay đổi, không phát "vừa thêm mã X": một event rơi giữa
        // chừng thì tập đầy đủ ở lần sau vẫn tự chữa được, còn danh sách gia giảm thì lệch mãi.
        //
        // Tự hợp nhất trong bộ nhớ vì thay đổi CHƯA được lưu — truy vấn lúc này còn thấy trạng
        // thái cũ. Chỉ chiếu SkillCode nên không dính identity map của EF.
        var skillCodes = await _unitOfWork.StaffSkills
            .GetAllAsync()
            .Where(s => s.StaffAccountId == request.StaffAccountId && !s.IsDeleted)
            .Select(s => s.SkillCode)
            .ToListAsync(cancellationToken);
        if (!skillCodes.Contains(skillCode, StringComparer.Ordinal))
            skillCodes.Add(skillCode);

        // Outbox: publish TRƯỚC SaveChangesAsync ⇒ nguyên tử với chính thay đổi vừa làm.
        await _messageProducer.PublishAsync(
            new StaffSkillsUpdatedEvent(request.StaffAccountId, skillCodes), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AccountActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Cập nhật staff skill thành công.",
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
