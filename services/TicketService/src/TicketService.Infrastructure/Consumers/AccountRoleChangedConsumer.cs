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

    private static bool IsCustomerRole(string role)
        => role.Equals("Customer", StringComparison.OrdinalIgnoreCase);

    public async Task Consume(ConsumeContext<AccountRoleChangedEvent> context)
    {
        var evt = context.Message;

        await context.ProcessOnceAsync(_inbox, nameof(TicketAccountRoleChangedConsumer), async () =>
        {
            if (!IsStaffRole(evt.NewRole) && !IsCustomerRole(evt.NewRole))
            {
                _logger.LogWarning(
                    "Bỏ qua account role không được TicketService hỗ trợ: account={AccountId}, role={Role}.",
                    evt.AccountId, evt.NewRole);
                return;
            }

            var now = DateTime.UtcNow;
            var staff = await _uow.StaffAccounts.GetAllAsync()
                .FirstOrDefaultAsync(s => s.AccountId == evt.AccountId, context.CancellationToken);
            var customer = await _uow.CustomerAccounts.GetAllAsync()
                .FirstOrDefaultAsync(c => c.AccountId == evt.AccountId, context.CancellationToken);

            var latest = new[] { staff?.LastSourceEventAtUtc, customer?.LastSourceEventAtUtc }
                .Where(value => value.HasValue)
                .Max();
            if (latest is { } applied && applied >= evt.ChangedAtUtc)
                return;

            var currentStatus = AuthAccountStatusMapper.FromAuthStatus(evt.AccountStatus);

            if (IsStaffRole(evt.NewRole))
            {
                if (staff is null)
                {
                    await _uow.StaffAccounts.AddAsync(new StaffAccount
                    {
                        Id = evt.AccountId,
                        AccountId = evt.AccountId,
                        Email = evt.Email.Trim().ToLowerInvariant(),
                        FullName = evt.FullName.Trim(),
                        // Bỏ sót Role ở đây là bản ghi rơi vào mặc định "Staff" của entity, nên
                        // Manager/Admin lọt vào mọi truy vấn "danh sách kỹ thuật viên" — điển hình
                        // là gợi ý phân công (TicketStaffSuggestionsQueryHandler lọc Role == "Staff").
                        Role = evt.NewRole,
                        Status = currentStatus,
                        IsDeleted = false,
                        DeletedAt = null,
                        LastSyncedAt = now,
                        LastSourceEventAtUtc = evt.ChangedAtUtc,
                    });
                }
                else
                {
                    staff.Email = evt.Email.Trim().ToLowerInvariant();
                    staff.FullName = evt.FullName.Trim();
                    // Staff → Manager giữ nguyên bản ghi cũ, nên không ghi đè Role là vai trò cũ
                    // ở lại vĩnh viễn: không có luồng đồng bộ nào khác sửa cột này về sau.
                    staff.Role = evt.NewRole;
                    staff.Status = currentStatus;
                    staff.IsDeleted = false;
                    staff.DeletedAt = null;
                    staff.LastSyncedAt = now;
                    staff.LastSourceEventAtUtc = evt.ChangedAtUtc;
                    _uow.StaffAccounts.UpdateAsync(staff);
                }

                if (customer is not null)
                {
                    customer.Status = AccountStatusEnum.Inactive;
                    customer.LastSyncedAt = now;
                    customer.LastSourceEventAtUtc = evt.ChangedAtUtc;
                    _uow.CustomerAccounts.UpdateAsync(customer);
                }
            }
            else
            {
                if (customer is null)
                {
                    await _uow.CustomerAccounts.AddAsync(new CustomerAccount
                    {
                        Id = evt.AccountId,
                        AccountId = evt.AccountId,
                        Email = evt.Email.Trim().ToLowerInvariant(),
                        FullName = evt.FullName.Trim(),
                        PhoneNumber = Normalize(evt.PhoneNumber),
                        Status = currentStatus,
                        IsDeleted = false,
                        DeletedAt = null,
                        LastSyncedAt = now,
                        LastSourceEventAtUtc = evt.ChangedAtUtc,
                    });
                }
                else
                {
                    customer.Email = evt.Email.Trim().ToLowerInvariant();
                    customer.FullName = evt.FullName.Trim();
                    customer.PhoneNumber = Normalize(evt.PhoneNumber);
                    customer.Status = currentStatus;
                    customer.IsDeleted = false;
                    customer.DeletedAt = null;
                    customer.LastSyncedAt = now;
                    customer.LastSourceEventAtUtc = evt.ChangedAtUtc;
                    _uow.CustomerAccounts.UpdateAsync(customer);
                }

                if (staff is not null)
                {
                    staff.Status = AccountStatusEnum.Inactive;
                    staff.LastSyncedAt = now;
                    staff.LastSourceEventAtUtc = evt.ChangedAtUtc;
                    _uow.StaffAccounts.UpdateAsync(staff);
                }
            }

            await _uow.SaveChangesAsync(context.CancellationToken);

            _logger.LogInformation(
                "Account {AccountId} đổi role {OldRole} → {NewRole}; bản sao ticket đã đồng bộ.",
                evt.AccountId, evt.OldRole, evt.NewRole);
        });
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
