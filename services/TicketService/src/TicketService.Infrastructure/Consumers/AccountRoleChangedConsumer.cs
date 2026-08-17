using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedContracts.Events;
using SharedInfrastructure.Idempotency;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Infrastructure.Consumers;

/// <summary>
/// GH-769 — đồng bộ bản sao account khi role đổi.
/// </summary>
/// <remarks>
/// <para>
/// TicketService giữ hai bảng tách biệt: <c>StaffAccounts</c> và <c>CustomerAccounts</c>. Cả hai
/// chỉ được dựng ĐÚNG MỘT LẦN — lúc account kích hoạt. Đổi Customer → Staff sau đó thì không có
/// <c>StaffAccount</c> nào được tạo, nên người vừa lên Staff không thể được giao ticket; đổi
/// ngược lại thì <c>StaffAccount</c> cũ vẫn nằm đó và vẫn nhận việc.
/// </para>
/// <para>
/// Vì sao ĐÌNH CHỈ chứ không XOÁ bản sao cũ: ticket lịch sử tham chiếu tới nó (người xử lý,
/// người bình luận). Xoá đi là làm hỏng lịch sử; đặt <c>Status = Inactive</c> vừa chặn việc mới
/// vừa giữ nguyên dữ liệu đã có.
/// </para>
/// </remarks>
public class TicketAccountRoleChangedConsumer : IConsumer<AccountRoleChangedEvent>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IInboxStore _inbox;
    private readonly ILogger<TicketAccountRoleChangedConsumer> _logger;

    public TicketAccountRoleChangedConsumer(
        ITicketUnitOfWork uow,
        IInboxStore inbox,
        ILogger<TicketAccountRoleChangedConsumer> logger)
    {
        _uow = uow;
        _inbox = inbox;
        _logger = logger;
    }

    /// <summary>
    /// Vai trò nội bộ dùng CHUNG một bảng <c>StaffAccounts</c> — khớp đúng cách
    /// <c>TicketAccountActivatedConsumer</c> phân loại lúc kích hoạt. Lệch nhau ở đây là một
    /// account có thể nằm ở cả hai bảng, hoặc không bảng nào.
    /// </summary>
    private static bool IsStaffRole(string role)
        => role.Equals("Staff", StringComparison.OrdinalIgnoreCase)
           || role.Equals("Manager", StringComparison.OrdinalIgnoreCase)
           || role.Equals("Admin", StringComparison.OrdinalIgnoreCase);

    public async Task Consume(ConsumeContext<AccountRoleChangedEvent> context)
    {
        var evt = context.Message;

        await context.ProcessOnceAsync(_inbox, nameof(TicketAccountRoleChangedConsumer), async () =>
        {
            var now = DateTime.UtcNow;
            var staff = await _uow.StaffAccounts.GetAllAsync()
                .FirstOrDefaultAsync(s => s.AccountId == evt.AccountId, context.CancellationToken);
            var customer = await _uow.CustomerAccounts.GetAllAsync()
                .FirstOrDefaultAsync(c => c.AccountId == evt.AccountId, context.CancellationToken);

            if (IsStaffRole(evt.NewRole))
            {
                if (staff is null)
                {
                    await _uow.StaffAccounts.AddAsync(new StaffAccount
                    {
                        Id = Guid.NewGuid(),
                        AccountId = evt.AccountId,
                        Email = evt.Email,
                        FullName = evt.FullName,
                        // Bỏ sót Role ở đây là bản ghi rơi vào mặc định "Staff" của entity, nên
                        // Manager/Admin lọt vào mọi truy vấn "danh sách kỹ thuật viên" — điển hình
                        // là gợi ý phân công (TicketStaffSuggestionsQueryHandler lọc Role == "Staff").
                        Role = evt.NewRole,
                        Status = AccountStatusEnum.Active,
                        LastSyncedAt = now,
                    });
                }
                else
                {
                    staff.Email = evt.Email;
                    staff.FullName = evt.FullName;
                    // Staff → Manager giữ nguyên bản ghi cũ, nên không ghi đè Role là vai trò cũ
                    // ở lại vĩnh viễn: không có luồng đồng bộ nào khác sửa cột này về sau.
                    staff.Role = evt.NewRole;
                    staff.Status = AccountStatusEnum.Active;
                    staff.LastSyncedAt = now;
                    _uow.StaffAccounts.UpdateAsync(staff);
                }

                if (customer is not null && customer.Status != AccountStatusEnum.Inactive)
                {
                    customer.Status = AccountStatusEnum.Inactive;
                    customer.LastSyncedAt = now;
                    _uow.CustomerAccounts.UpdateAsync(customer);
                }
            }
            else
            {
                if (customer is null)
                {
                    await _uow.CustomerAccounts.AddAsync(new CustomerAccount
                    {
                        Id = Guid.NewGuid(),
                        AccountId = evt.AccountId,
                        Email = evt.Email,
                        FullName = evt.FullName,
                        PhoneNumber = evt.PhoneNumber,
                        Status = AccountStatusEnum.Active,
                        LastSyncedAt = now,
                    });
                }
                else
                {
                    customer.Email = evt.Email;
                    customer.FullName = evt.FullName;
                    customer.PhoneNumber = evt.PhoneNumber;
                    customer.Status = AccountStatusEnum.Active;
                    customer.LastSyncedAt = now;
                    _uow.CustomerAccounts.UpdateAsync(customer);
                }

                if (staff is not null && staff.Status != AccountStatusEnum.Inactive)
                {
                    staff.Status = AccountStatusEnum.Inactive;
                    staff.LastSyncedAt = now;
                    _uow.StaffAccounts.UpdateAsync(staff);
                }
            }

            await _uow.SaveChangesAsync(context.CancellationToken);

            _logger.LogInformation(
                "Account {AccountId} đổi role {OldRole} → {NewRole}; bản sao ticket đã đồng bộ.",
                evt.AccountId, evt.OldRole, evt.NewRole);
        });
    }
}
