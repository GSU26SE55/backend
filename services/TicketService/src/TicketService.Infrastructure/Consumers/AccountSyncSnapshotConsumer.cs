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
/// Dựng lại bản sao account của TicketService từ snapshot do AuthService phát.
/// </summary>
/// <remarks>
/// <para>
/// Trước đây TicketService KHÔNG nghe <c>AccountSyncSnapshotEvent</c> (chỉ Notification và
/// Battery nghe), nên <c>staff_accounts</c> là read-model duy nhất không có đường đối soát:
/// mỗi service một database, sai lệch ở đây không thể tự sửa từ phía TicketService.
/// </para>
/// <para>
/// Cụ thể cột <c>Role</c>: migration <c>AddTicketAiSuggestionAndStaffRole</c> thêm cột với
/// <c>defaultValue: "Staff"</c>, nên MỌI bản ghi có trước 2026-08-08 — gồm cả Manager/Admin —
/// bị đóng dấu "Staff". Hệ quả nhìn thấy được: panel gợi ý phân công
/// (<c>TicketStaffSuggestionsQueryHandler</c> lọc <c>Role == "Staff"</c>) đề xuất cả Manager.
/// Ghi chú trên <c>StaffAccount.Role</c> giả định "Manager/Admin sẽ được ghi đè ở lần đồng bộ
/// kế tiếp", nhưng lần đồng bộ đó không tồn tại: đường DUY NHẤT ghi cột này là
/// <c>TicketAccountActivatedConsumer</c>, chỉ chạy lúc kích hoạt account.
/// </para>
/// <para>
/// Consumer này lấp đúng chỗ đó. Gọi <c>POST /api/admin/accounts/resync</c> bên AuthService là
/// mọi bản ghi được ghi đè bằng role thật — an toàn khi gọi lại nhiều lần vì snapshot là upsert
/// thuần, không kèm tác dụng phụ nghiệp vụ.
/// </para>
/// </remarks>
public class TicketAccountSyncSnapshotConsumer : IConsumer<AccountSyncSnapshotEvent>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IInboxStore _inbox;
    private readonly ILogger<TicketAccountSyncSnapshotConsumer> _logger;

    public TicketAccountSyncSnapshotConsumer(
        ITicketUnitOfWork uow,
        IInboxStore inbox,
        ILogger<TicketAccountSyncSnapshotConsumer> logger)
    {
        _uow = uow;
        _inbox = inbox;
        _logger = logger;
    }

    /// <summary>
    /// Vai trò nội bộ dùng CHUNG bảng <c>staff_accounts</c> — khớp đúng cách
    /// <c>TicketAccountActivatedConsumer</c> và <c>TicketAccountRoleChangedConsumer</c> phân loại.
    /// Lệch nhau ở đây là một account nằm ở cả hai bảng, hoặc không bảng nào.
    /// </summary>
    private static bool IsStaffRole(string role)
        => role.Equals("Staff", StringComparison.OrdinalIgnoreCase)
           || role.Equals("Manager", StringComparison.OrdinalIgnoreCase)
           || role.Equals("Admin", StringComparison.OrdinalIgnoreCase);

    public async Task Consume(ConsumeContext<AccountSyncSnapshotEvent> context)
    {
        await context.ProcessOnceAsync(_inbox, nameof(TicketAccountSyncSnapshotConsumer), async () =>
        {
            var evt = context.Message;
            var ct = context.CancellationToken;
            var isStaffRole = IsStaffRole(evt.Role);

            var staff = await _uow.StaffAccounts.GetAllAsync()
                .FirstOrDefaultAsync(s => s.AccountId == evt.AccountId, ct);
            var customer = await _uow.CustomerAccounts.GetAllAsync()
                .FirstOrDefaultAsync(c => c.AccountId == evt.AccountId, ct);

            // Account đã xoá / không phải vai trò nội bộ mà chưa từng có bản sao thì không cần
            // dựng row mới ở đây — TicketService chỉ soi chiếu người có liên quan tới ticket.
            if (staff is null && customer is null && (!isStaffRole || evt.IsDeleted))
                return;

            // Snapshot tới muộn KHÔNG được kéo lùi bản sao: các consumer vòng đời ghi thời điểm
            // xử lý của chúng vào cùng cột này.
            if (staff is not null && staff.LastSyncedAt >= evt.SnapshotAtUtc)
                return;

            var active = evt.IsActive && !evt.IsDeleted;

            if (isStaffRole)
            {
                if (staff is null)
                {
                    staff = new StaffAccount
                    {
                        Id = evt.AccountId,
                        AccountId = evt.AccountId,
                        Email = evt.Email.Trim(),
                        FullName = evt.FullName.Trim(),
                        Role = evt.Role.Trim(),
                        Status = active ? AccountStatusEnum.Active : AccountStatusEnum.Inactive,
                        LastSyncedAt = evt.SnapshotAtUtc,
                    };
                    await _uow.StaffAccounts.AddAsync(staff);
                }
                else
                {
                    staff.Email = evt.Email.Trim();
                    staff.FullName = evt.FullName.Trim();
                    // Điểm mấu chốt của việc đối soát: đây là nơi role bị migration gán sai
                    // được ghi đè bằng giá trị thật từ AuthService.
                    staff.Role = evt.Role.Trim();
                    staff.Status = active ? AccountStatusEnum.Active : AccountStatusEnum.Inactive;
                    staff.LastSyncedAt = evt.SnapshotAtUtc;
                    _uow.StaffAccounts.UpdateAsync(staff);
                }

                // Đình chỉ chứ không xoá bản sao đối diện: ticket lịch sử còn tham chiếu tới nó.
                if (customer is not null && customer.Status != AccountStatusEnum.Inactive)
                {
                    customer.Status = AccountStatusEnum.Inactive;
                    customer.LastSyncedAt = evt.SnapshotAtUtc;
                    _uow.CustomerAccounts.UpdateAsync(customer);
                }
            }
            else
            {
                if (customer is null)
                {
                    customer = new CustomerAccount
                    {
                        Id = evt.AccountId,
                        AccountId = evt.AccountId,
                        Email = evt.Email.Trim(),
                        FullName = evt.FullName.Trim(),
                        PhoneNumber = string.IsNullOrWhiteSpace(evt.PhoneNumber) ? null : evt.PhoneNumber.Trim(),
                        Status = active ? AccountStatusEnum.Active : AccountStatusEnum.Inactive,
                        LastSyncedAt = evt.SnapshotAtUtc,
                    };
                    await _uow.CustomerAccounts.AddAsync(customer);
                }
                else
                {
                    customer.Email = evt.Email.Trim();
                    customer.FullName = evt.FullName.Trim();
                    customer.PhoneNumber = string.IsNullOrWhiteSpace(evt.PhoneNumber) ? null : evt.PhoneNumber.Trim();
                    customer.Status = active ? AccountStatusEnum.Active : AccountStatusEnum.Inactive;
                    customer.LastSyncedAt = evt.SnapshotAtUtc;
                    _uow.CustomerAccounts.UpdateAsync(customer);
                }

                // Account rời khỏi vai trò nội bộ: chặn nhận việc mới, giữ nguyên lịch sử.
                if (staff is not null && staff.Status != AccountStatusEnum.Inactive)
                {
                    staff.Status = AccountStatusEnum.Inactive;
                    staff.LastSyncedAt = evt.SnapshotAtUtc;
                    _uow.StaffAccounts.UpdateAsync(staff);
                }
            }

            await _uow.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Đối soát bản sao account {AccountId}: role={Role} active={IsActive} deleted={IsDeleted} reason={Reason}.",
                evt.AccountId, evt.Role, evt.IsActive, evt.IsDeleted, evt.Reason);
        });
    }
}
