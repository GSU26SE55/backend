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
/// Consumer này lấp đúng chỗ đó. Worker đối soát định kỳ bên AuthService tự phát snapshot đầy đủ;
/// có thể gọi <c>POST /api/admin/accounts/resync</c> khi cần chạy ngay. Mọi bản ghi được ghi đè
/// bằng trạng thái authoritative — an toàn khi gọi lại nhiều lần vì snapshot là upsert thuần,
/// không kèm tác dụng phụ nghiệp vụ.
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

    private static bool IsCustomerRole(string role)
        => role.Equals("Customer", StringComparison.OrdinalIgnoreCase);

    public async Task Consume(ConsumeContext<AccountSyncSnapshotEvent> context)
    {
        await context.ProcessOnceAsync(_inbox, nameof(TicketAccountSyncSnapshotConsumer), async () =>
        {
            var evt = context.Message;
            var ct = context.CancellationToken;
            var isStaffRole = IsStaffRole(evt.Role);
            var isCustomerRole = IsCustomerRole(evt.Role);
            var normalizedEmail = evt.Email.Trim().ToLowerInvariant();
            var normalizedEmployeeCode = Normalize(evt.EmployeeCode);
            var normalizedAvatarUrl = Normalize(evt.AvatarUrl);

            // Dữ liệu dev/production cũ có thể chứa seed/read-model với AccountId lỗi thời nhưng
            // cùng email hoặc employee code. Chỉ tìm alias cho snapshot account còn sống; snapshot
            // của account đã xoá không được phép đụng một account mới tái sử dụng cùng email.
            var staffMatches = await _uow.StaffAccounts.GetAllAsync()
                .Where(s => s.AccountId == evt.AccountId
                    || (!evt.IsDeleted && s.Email.ToLower() == normalizedEmail)
                    || (!evt.IsDeleted
                        && normalizedEmployeeCode != null
                        && s.EmployeeCode == normalizedEmployeeCode))
                .ToListAsync(ct);
            var customerMatches = await _uow.CustomerAccounts.GetAllAsync()
                .Where(c => c.AccountId == evt.AccountId
                    || (!evt.IsDeleted && c.Email.ToLower() == normalizedEmail))
                .ToListAsync(ct);

            var staff = staffMatches.FirstOrDefault(s => s.AccountId == evt.AccountId);
            var customer = customerMatches.FirstOrDefault(c => c.AccountId == evt.AccountId);
            var legacyStaffAliases = staffMatches
                .Where(s => s.AccountId != evt.AccountId)
                .ToList();
            var legacyCustomerAliases = customerMatches
                .Where(c => c.AccountId != evt.AccountId)
                .ToList();

            var latestAccountEventAt = new[]
                {
                    staff?.LastSourceEventAtUtc,
                    customer?.LastSourceEventAtUtc,
                    legacyStaffAliases.Max(s => s.LastSourceEventAtUtc),
                    legacyCustomerAliases.Max(c => c.LastSourceEventAtUtc),
                }
                .Where(value => value.HasValue)
                .Max();
            var applyAccountSnapshot = latestAccountEventAt is null || latestAccountEventAt < evt.SnapshotAtUtc;
            var applyStaffProfileSnapshot = evt.HasStaffProfileSnapshot
                && (staff?.LastStaffProfileSourceEventAtUtc is null
                    || staff.LastStaffProfileSourceEventAtUtc < evt.SnapshotAtUtc);

            if (!applyAccountSnapshot && !applyStaffProfileSnapshot)
                return;

            var now = DateTime.UtcNow;
            var changed = false;
            var staffWasAdded = false;

            if (applyAccountSnapshot && !evt.IsDeleted)
            {
                // Không xoá alias: ticket lịch sử có thể vẫn tham chiếu AccountId cũ. Chỉ vô hiệu
                // hoá nó cho nghiệp vụ mới. EmployeeCode phải được nhả trước khi insert canonical
                // vì index IX_staff_accounts_employee_code là unique kể cả với row inactive.
                foreach (var alias in legacyStaffAliases)
                {
                    alias.Status = AccountStatusEnum.Inactive;
                    alias.IsAvailable = false;
                    if (normalizedEmployeeCode != null
                        && string.Equals(alias.EmployeeCode, normalizedEmployeeCode, StringComparison.Ordinal))
                    {
                        alias.EmployeeCode = null;
                    }
                    alias.LastSyncedAt = now;
                    alias.LastSourceEventAtUtc = evt.SnapshotAtUtc;
                    _uow.StaffAccounts.UpdateAsync(alias);
                    changed = true;
                }

                foreach (var alias in legacyCustomerAliases)
                {
                    alias.Status = AccountStatusEnum.Inactive;
                    alias.LastSyncedAt = now;
                    alias.LastSourceEventAtUtc = evt.SnapshotAtUtc;
                    _uow.CustomerAccounts.UpdateAsync(alias);
                    changed = true;
                }

                // PostgreSQL có thể INSERT canonical trước UPDATE alias trong cùng SaveChanges.
                // Flush riêng khi employee code đang xung đột để loại hoàn toàn phụ thuộc thứ tự.
                if (staff is null
                    && isStaffRole
                    && legacyStaffAliases.Any(alias => alias.EmployeeCode is null)
                    && changed)
                {
                    await _uow.SaveChangesAsync(ct);
                    changed = false;
                }
            }

            if (applyAccountSnapshot && evt.IsDeleted)
            {
                if (staff is not null)
                {
                    staff.Status = AccountStatusEnum.Inactive;
                    staff.LastSyncedAt = now;
                    staff.LastSourceEventAtUtc = evt.SnapshotAtUtc;
                    _uow.StaffAccounts.DeleteAsync(staff);
                    changed = true;
                }

                if (customer is not null)
                {
                    customer.Status = AccountStatusEnum.Inactive;
                    customer.LastSyncedAt = now;
                    customer.LastSourceEventAtUtc = evt.SnapshotAtUtc;
                    _uow.CustomerAccounts.DeleteAsync(customer);
                    changed = true;
                }

                if (changed)
                    await _uow.SaveChangesAsync(ct);

                return;
            }

            if (applyAccountSnapshot)
            {
                var status = AuthAccountStatusMapper.FromAuthStatus(evt.AccountStatus);

                if (isStaffRole)
                {
                    if (staff is null)
                    {
                        staff = new StaffAccount
                        {
                            Id = evt.AccountId,
                            AccountId = evt.AccountId,
                            Email = normalizedEmail,
                            FullName = evt.FullName.Trim(),
                            AvatarUrl = evt.HasAvatarSnapshot ? normalizedAvatarUrl : null,
                            Role = evt.Role.Trim(),
                            Status = status,
                            IsDeleted = false,
                            DeletedAt = null,
                            LastSyncedAt = now,
                            LastSourceEventAtUtc = evt.SnapshotAtUtc,
                        };
                        await _uow.StaffAccounts.AddAsync(staff);
                        staffWasAdded = true;
                    }
                    else
                    {
                        staff.Email = normalizedEmail;
                        staff.FullName = evt.FullName.Trim();
                        if (evt.HasAvatarSnapshot)
                            staff.AvatarUrl = normalizedAvatarUrl;
                        staff.Role = evt.Role.Trim();
                        staff.Status = status;
                        staff.IsDeleted = false;
                        staff.DeletedAt = null;
                        staff.LastSyncedAt = now;
                        staff.LastSourceEventAtUtc = evt.SnapshotAtUtc;
                        _uow.StaffAccounts.UpdateAsync(staff);
                    }

                    changed = true;

                    // Giữ row đối diện để ticket lịch sử còn tham chiếu, nhưng chặn nghiệp vụ mới.
                    if (customer is not null)
                    {
                        customer.Status = AccountStatusEnum.Inactive;
                        customer.LastSyncedAt = now;
                        customer.LastSourceEventAtUtc = evt.SnapshotAtUtc;
                        _uow.CustomerAccounts.UpdateAsync(customer);
                    }
                }
                else if (isCustomerRole)
                {
                    // Đây là lỗi gốc: bản cũ return khi cả hai row null và role=Customer, khiến
                    // customer_accounts không bao giờ đầy đủ sau khi mất event Activated.
                    if (customer is null)
                    {
                        customer = new CustomerAccount
                        {
                            Id = evt.AccountId,
                            AccountId = evt.AccountId,
                            Email = normalizedEmail,
                            FullName = evt.FullName.Trim(),
                            PhoneNumber = Normalize(evt.PhoneNumber),
                            AvatarUrl = evt.HasAvatarSnapshot ? normalizedAvatarUrl : null,
                            Status = status,
                            IsDeleted = false,
                            DeletedAt = null,
                            LastSyncedAt = now,
                            LastSourceEventAtUtc = evt.SnapshotAtUtc,
                        };
                        await _uow.CustomerAccounts.AddAsync(customer);
                    }
                    else
                    {
                        customer.Email = normalizedEmail;
                        customer.FullName = evt.FullName.Trim();
                        customer.PhoneNumber = Normalize(evt.PhoneNumber);
                        if (evt.HasAvatarSnapshot)
                            customer.AvatarUrl = normalizedAvatarUrl;
                        customer.Status = status;
                        customer.IsDeleted = false;
                        customer.DeletedAt = null;
                        customer.LastSyncedAt = now;
                        customer.LastSourceEventAtUtc = evt.SnapshotAtUtc;
                        _uow.CustomerAccounts.UpdateAsync(customer);
                    }

                    changed = true;

                    if (staff is not null)
                    {
                        staff.Status = AccountStatusEnum.Inactive;
                        staff.LastSyncedAt = now;
                        staff.LastSourceEventAtUtc = evt.SnapshotAtUtc;
                        _uow.StaffAccounts.UpdateAsync(staff);
                    }
                }
                else
                {
                    // Role rỗng/không biết: fail closed, không tự đoán thành Customer.
                    if (staff is not null)
                    {
                        staff.Status = AccountStatusEnum.Inactive;
                        staff.LastSyncedAt = now;
                        staff.LastSourceEventAtUtc = evt.SnapshotAtUtc;
                        _uow.StaffAccounts.UpdateAsync(staff);
                        changed = true;
                    }
                    if (customer is not null)
                    {
                        customer.Status = AccountStatusEnum.Inactive;
                        customer.LastSyncedAt = now;
                        customer.LastSourceEventAtUtc = evt.SnapshotAtUtc;
                        _uow.CustomerAccounts.UpdateAsync(customer);
                        changed = true;
                    }
                }
            }

            if (applyStaffProfileSnapshot && staff is not null)
            {
                staff.EmployeeCode = Normalize(evt.EmployeeCode);
                staff.MaxConcurrentTickets = evt.MaxConcurrentTickets;
                staff.IsAvailable = evt.IsAvailable;
                staff.SkillTier = Enum.IsDefined(typeof(StaffSkillTierEnum), evt.SkillTier)
                    ? (StaffSkillTierEnum)evt.SkillTier
                    : StaffSkillTierEnum.Generalist;
                staff.SkillCodes = evt.SkillCodes?
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Select(code => code.Trim())
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(code => code, StringComparer.Ordinal)
                    .ToList() ?? new List<string>();
                staff.LastStaffProfileSourceEventAtUtc = evt.SnapshotAtUtc;
                staff.LastSyncedAt = now;
                // Entity vừa AddAsync phải giữ state Added cho tới SaveChanges. Gọi UpdateAsync
                // ở đây sẽ đổi nó thành Modified, EF phát UPDATE một row chưa tồn tại và snapshot
                // đầu tiên của Staff bị fault vĩnh viễn sau retry.
                if (!staffWasAdded)
                    _uow.StaffAccounts.UpdateAsync(staff);
                changed = true;
            }

            if (changed)
                await _uow.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Đối soát bản sao account {AccountId}: role={Role} active={IsActive} deleted={IsDeleted} reason={Reason}.",
                evt.AccountId, evt.Role, evt.IsActive, evt.IsDeleted, evt.Reason);
        });
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
