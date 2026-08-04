using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NotificationService.Application.CQRS.Command.NotificationGroup;
using NotificationService.Application.DTOs.Response.Notification;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using NotificationService.Domain.Enums;
using GroupEntity = NotificationService.Domain.Entities.NotificationGroup;

namespace NotificationService.Application.CQRS.Handler.NotificationGroup;

public class NotificationGroupCreateCommandHandler
    : IRequestHandler<NotificationGroupCreateCommand, NotificationGroupActionResponse>
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly INotificationAuditWriter _auditWriter;
    private readonly ILogger<NotificationGroupCreateCommandHandler> _logger;

    public NotificationGroupCreateCommandHandler(
        INotificationUnitOfWork unitOfWork,
        INotificationAuditWriter auditWriter,
        ILogger<NotificationGroupCreateCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _auditWriter = auditWriter;
        _logger = logger;
    }

    public async Task<NotificationGroupActionResponse> Handle(
        NotificationGroupCreateCommand request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        var normalized = NotificationGroupRules.Normalize(name);

        // Kiểm trước để trả 409 có thông báo rõ. Partial unique index vẫn là chốt chặn thật khi hai
        // request tạo cùng tên chạy song song — nhánh catch bên dưới xử lý trường hợp đó.
        var duplicated = await _unitOfWork.NotificationGroups.GetAllAsync(false)
            .AnyAsync(g => !g.IsDeleted && g.NormalizedName == normalized, cancellationToken);

        if (duplicated)
        {
            return new NotificationGroupActionResponse
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "Đã có nhóm trùng tên. Đặt tên khác hoặc sửa nhóm đang có.",
            };
        }

        var entity = new GroupEntity
        {
            Id = Guid.NewGuid(),
            Name = name,
            NormalizedName = normalized,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            // API chỉ tạo nhóm Static — nhóm Role do seeder sinh và đã phủ đủ 4 role.
            Kind = NotificationGroupKindEnum.Static,
            RoleFilter = null,
            IsSystem = false,
        };

        await _unitOfWork.BeginTransactionAsync();
        try
        {
            await _unitOfWork.NotificationGroups.AddAsync(entity);

            await _auditWriter.WriteAsync(
                NotificationAuditActionEnum.GroupCreated,
                entity.Id,
                request.ActorUserId,
                isSuccess: true,
                reason: "Tạo nhóm người nhận",
                metadata: new Dictionary<string, object?>
                {
                    ["name"] = entity.Name,
                    ["kind"] = entity.Kind.ToString(),
                },
                ct: cancellationToken);

            await _unitOfWork.CommitTransactionAsync();
        }
        catch (DbUpdateException ex)
        {
            await _unitOfWork.RollbackTransactionAsync();
            _logger.LogWarning(ex, "Tạo nhóm '{Name}' thất bại — nhiều khả năng trùng tên do race.", name);

            return new NotificationGroupActionResponse
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "Đã có nhóm trùng tên. Đặt tên khác hoặc sửa nhóm đang có.",
            };
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync();
            _logger.LogError(ex, "Tạo nhóm '{Name}' thất bại.", name);

            return new NotificationGroupActionResponse
            {
                IsSuccess = false,
                StatusCode = 500,
                Message = "Không tạo được nhóm.",
            };
        }

        _logger.LogInformation("Đã tạo nhóm '{Name}' (id {Id}).", entity.Name, entity.Id);

        return new NotificationGroupActionResponse
        {
            IsSuccess = true,
            StatusCode = 201,
            Message = "Đã tạo nhóm.",
            Data = entity.Id,
        };
    }
}
