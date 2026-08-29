using MassTransit;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Events;
using SharedInfrastructure.Idempotency;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Infrastructure.Consumers;

public class TicketAccountActivatedConsumer : IConsumer<AccountActivatedEvent>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IInboxStore _inbox;

    public TicketAccountActivatedConsumer(ITicketUnitOfWork uow, IInboxStore inbox)
    {
        _uow = uow;
        _inbox = inbox;
    }

    public async Task Consume(ConsumeContext<AccountActivatedEvent> context)
    {
        var @event = context.Message;
        // GH-764 — qua ProcessOnceAsync: giữ chỗ có hạn → chạy → chốt khi xong / nhả khi lỗi.
        // Bản cũ đánh dấu đã-xử-lý TRƯỚC side effect và không bao giờ gỡ, nên một lỗi tạm thời
        // biến thành mất message vĩnh viễn (gửi lại thấy dấu → bỏ qua → ACK).
        await context.ProcessOnceAsync(_inbox, nameof(TicketAccountActivatedConsumer), async () =>
        {
            var isStaff = @event.Role.Equals("Staff", StringComparison.OrdinalIgnoreCase) ||
                          @event.Role.Equals("Manager", StringComparison.OrdinalIgnoreCase) ||
                          @event.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase);
            var isCustomer = @event.Role.Equals("Customer", StringComparison.OrdinalIgnoreCase);

            if (!isStaff && !isCustomer)
                return;

            var staff = await _uow.StaffAccounts.GetAllAsync()
                .FirstOrDefaultAsync(s => s.AccountId == @event.AccountId, context.CancellationToken);
            var customer = await _uow.CustomerAccounts.GetAllAsync()
                .FirstOrDefaultAsync(c => c.AccountId == @event.AccountId, context.CancellationToken);

            var latest = new[] { staff?.LastSourceEventAtUtc, customer?.LastSourceEventAtUtc }
                .Where(value => value.HasValue)
                .Max();
            if (latest is { } applied && applied >= @event.OccurredAt)
                return;

            var now = DateTime.UtcNow;

            if (isStaff)
            {
                if (staff == null)
                {
                    staff = new StaffAccount
                    {
                        Id = @event.AccountId,
                        AccountId = @event.AccountId,
                        Email = @event.Email.Trim().ToLowerInvariant(),
                        FullName = @event.FullName.Trim(),
                        // Giữ lại vai trò: bảng chứa cả Staff/Manager/Admin nên không có nó thì
                        // không tách được "danh sách kỹ thuật viên" khỏi Manager/Admin.
                        Role = @event.Role,
                        Status = AccountStatusEnum.Active,
                        IsDeleted = false,
                        DeletedAt = null,
                        LastSyncedAt = now,
                        LastSourceEventAtUtc = @event.OccurredAt
                    };
                    await _uow.StaffAccounts.AddAsync(staff);
                }
                else
                {
                    staff.Email = @event.Email.Trim().ToLowerInvariant();
                    staff.FullName = @event.FullName.Trim();
                    staff.Role = @event.Role;
                    staff.Status = AccountStatusEnum.Active;
                    staff.IsDeleted = false;
                    staff.DeletedAt = null;
                    staff.LastSyncedAt = now;
                    staff.LastSourceEventAtUtc = @event.OccurredAt;
                    _uow.StaffAccounts.UpdateAsync(staff);
                }

                if (customer is not null)
                {
                    customer.Status = AccountStatusEnum.Inactive;
                    customer.LastSyncedAt = now;
                    customer.LastSourceEventAtUtc = @event.OccurredAt;
                    _uow.CustomerAccounts.UpdateAsync(customer);
                }
            }
            else
            {
                if (customer == null)
                {
                    customer = new CustomerAccount
                    {
                        Id = @event.AccountId,
                        AccountId = @event.AccountId,
                        Email = @event.Email.Trim().ToLowerInvariant(),
                        FullName = @event.FullName.Trim(),
                        PhoneNumber = Normalize(@event.PhoneNumber),
                        Status = AccountStatusEnum.Active,
                        IsDeleted = false,
                        DeletedAt = null,
                        LastSyncedAt = now,
                        LastSourceEventAtUtc = @event.OccurredAt
                    };
                    await _uow.CustomerAccounts.AddAsync(customer);
                }
                else
                {
                    customer.Email = @event.Email.Trim().ToLowerInvariant();
                    customer.FullName = @event.FullName.Trim();
                    customer.PhoneNumber = Normalize(@event.PhoneNumber);
                    customer.Status = AccountStatusEnum.Active;
                    customer.IsDeleted = false;
                    customer.DeletedAt = null;
                    customer.LastSyncedAt = now;
                    customer.LastSourceEventAtUtc = @event.OccurredAt;
                    _uow.CustomerAccounts.UpdateAsync(customer);
                }

                if (staff is not null)
                {
                    staff.Status = AccountStatusEnum.Inactive;
                    staff.LastSyncedAt = now;
                    staff.LastSourceEventAtUtc = @event.OccurredAt;
                    _uow.StaffAccounts.UpdateAsync(staff);
                }
            }

            await _uow.SaveChangesAsync(context.CancellationToken);
        });
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public class TicketAccountStatusChangedConsumer : IConsumer<AccountStatusChangedEvent>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IInboxStore _inbox;

    public TicketAccountStatusChangedConsumer(ITicketUnitOfWork uow, IInboxStore inbox)
    {
        _uow = uow;
        _inbox = inbox;
    }

    public async Task Consume(ConsumeContext<AccountStatusChangedEvent> context)
    {
        var @event = context.Message;
        // GH-764 — qua ProcessOnceAsync: giữ chỗ có hạn → chạy → chốt khi xong / nhả khi lỗi.
        // Bản cũ đánh dấu đã-xử-lý TRƯỚC side effect và không bao giờ gỡ, nên một lỗi tạm thời
        // biến thành mất message vĩnh viễn (gửi lại thấy dấu → bỏ qua → ACK).
        await context.ProcessOnceAsync(_inbox, nameof(TicketAccountStatusChangedConsumer), async () =>
        {
            // Event mang số của enum bên AuthService, KHÔNG phải của enum bên này — hai enum lệch
            // nhau một bậc. Ép kiểu thẳng thì Locked(2) của Auth rơi trúng Active(2) của Ticket,
            // tức là khoá tài khoản lại làm nó hợp lệ để giao ticket. Xem AuthAccountStatusMapper.
            var status = AuthAccountStatusMapper.FromAuthStatus(@event.NewStatus);
            var isStaffRole = @event.Role.Equals("Staff", StringComparison.OrdinalIgnoreCase)
                              || @event.Role.Equals("Manager", StringComparison.OrdinalIgnoreCase)
                              || @event.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase);
            var isCustomerRole = @event.Role.Equals("Customer", StringComparison.OrdinalIgnoreCase);
            var hasCanonicalRole = isStaffRole || isCustomerRole;
            var now = DateTime.UtcNow;

            var staff = await _uow.StaffAccounts.GetAllAsync()
                .FirstOrDefaultAsync(s => s.AccountId == @event.AccountId, context.CancellationToken);
            if (staff != null && (staff.LastSourceEventAtUtc is null || staff.LastSourceEventAtUtc < @event.OccurredAt))
            {
                staff.Email = @event.Email.Trim().ToLowerInvariant();
                if (!string.IsNullOrWhiteSpace(@event.FullName))
                    staff.FullName = @event.FullName.Trim();
                if (!string.IsNullOrWhiteSpace(@event.Role))
                    staff.Role = @event.Role.Trim();
                // Compatibility with events produced before Role was added to the contract: update
                // every existing projection. New events carry the canonical role, allowing us to
                // keep only the current-role projection active.
                staff.Status = !hasCanonicalRole || isStaffRole
                    ? status
                    : AccountStatusEnum.Inactive;
                staff.LastSyncedAt = now;
                staff.LastSourceEventAtUtc = @event.OccurredAt;
                _uow.StaffAccounts.UpdateAsync(staff);
            }

            var customer = await _uow.CustomerAccounts.GetAllAsync()
                .FirstOrDefaultAsync(c => c.AccountId == @event.AccountId, context.CancellationToken);
            if (customer != null && (customer.LastSourceEventAtUtc is null || customer.LastSourceEventAtUtc < @event.OccurredAt))
            {
                customer.Email = @event.Email.Trim().ToLowerInvariant();
                if (!string.IsNullOrWhiteSpace(@event.FullName))
                    customer.FullName = @event.FullName.Trim();
                customer.PhoneNumber = Normalize(@event.PhoneNumber);
                customer.AvatarUrl = Normalize(@event.AvatarUrl);
                customer.Status = !hasCanonicalRole || isCustomerRole
                    ? status
                    : AccountStatusEnum.Inactive;
                customer.LastSyncedAt = now;
                customer.LastSourceEventAtUtc = @event.OccurredAt;
                _uow.CustomerAccounts.UpdateAsync(customer);
            }

            await _uow.SaveChangesAsync(context.CancellationToken);
        });
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public class TicketAccountProfileUpdatedConsumer : IConsumer<AccountProfileUpdatedEvent>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IInboxStore _inbox;

    public TicketAccountProfileUpdatedConsumer(ITicketUnitOfWork uow, IInboxStore inbox)
    {
        _uow = uow;
        _inbox = inbox;
    }

    public async Task Consume(ConsumeContext<AccountProfileUpdatedEvent> context)
    {
        var @event = context.Message;
        // GH-764 — qua ProcessOnceAsync: giữ chỗ có hạn → chạy → chốt khi xong / nhả khi lỗi.
        // Bản cũ đánh dấu đã-xử-lý TRƯỚC side effect và không bao giờ gỡ, nên một lỗi tạm thời
        // biến thành mất message vĩnh viễn (gửi lại thấy dấu → bỏ qua → ACK).
        await context.ProcessOnceAsync(_inbox, nameof(TicketAccountProfileUpdatedConsumer), async () =>
        {
            var staff = await _uow.StaffAccounts.GetAllAsync()
                .FirstOrDefaultAsync(s => s.AccountId == @event.AccountId, context.CancellationToken);
            if (staff != null && (staff.LastSourceEventAtUtc is null || staff.LastSourceEventAtUtc < @event.OccurredAt))
            {
                staff.Email = @event.Email.Trim().ToLowerInvariant();
                staff.FullName = @event.FullName.Trim();
                staff.AvatarUrl = Normalize(@event.AvatarUrl);
                if (!string.IsNullOrWhiteSpace(@event.Role))
                {
                    staff.Role = @event.Role.Trim();
                    staff.Status = IsStaffRole(@event.Role)
                        ? AuthAccountStatusMapper.FromAuthStatus(@event.AccountStatus)
                        : AccountStatusEnum.Inactive;
                }
                staff.LastSyncedAt = DateTime.UtcNow;
                staff.LastSourceEventAtUtc = @event.OccurredAt;
                _uow.StaffAccounts.UpdateAsync(staff);
            }

            var customer = await _uow.CustomerAccounts.GetAllAsync()
                .FirstOrDefaultAsync(c => c.AccountId == @event.AccountId, context.CancellationToken);
            if (customer != null && (customer.LastSourceEventAtUtc is null || customer.LastSourceEventAtUtc < @event.OccurredAt))
            {
                customer.Email = @event.Email.Trim().ToLowerInvariant();
                customer.FullName = @event.FullName.Trim();
                customer.PhoneNumber = Normalize(@event.PhoneNumber);
                customer.AvatarUrl = Normalize(@event.AvatarUrl);
                if (!string.IsNullOrWhiteSpace(@event.Role))
                    customer.Status = @event.Role.Equals("Customer", StringComparison.OrdinalIgnoreCase)
                        ? AuthAccountStatusMapper.FromAuthStatus(@event.AccountStatus)
                        : AccountStatusEnum.Inactive;
                customer.LastSyncedAt = DateTime.UtcNow;
                customer.LastSourceEventAtUtc = @event.OccurredAt;
                _uow.CustomerAccounts.UpdateAsync(customer);
            }

            await _uow.SaveChangesAsync(context.CancellationToken);
        });
    }

    private static bool IsStaffRole(string role)
        => role.Equals("Staff", StringComparison.OrdinalIgnoreCase)
           || role.Equals("Manager", StringComparison.OrdinalIgnoreCase)
           || role.Equals("Admin", StringComparison.OrdinalIgnoreCase);

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public class TicketStaffProfileUpdatedConsumer : IConsumer<StaffProfileUpdatedEvent>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IInboxStore _inbox;

    public TicketStaffProfileUpdatedConsumer(ITicketUnitOfWork uow, IInboxStore inbox)
    {
        _uow = uow;
        _inbox = inbox;
    }

    public async Task Consume(ConsumeContext<StaffProfileUpdatedEvent> context)
    {
        var @event = context.Message;
        // GH-764 — qua ProcessOnceAsync: giữ chỗ có hạn → chạy → chốt khi xong / nhả khi lỗi.
        // Bản cũ đánh dấu đã-xử-lý TRƯỚC side effect và không bao giờ gỡ, nên một lỗi tạm thời
        // biến thành mất message vĩnh viễn (gửi lại thấy dấu → bỏ qua → ACK).
        await context.ProcessOnceAsync(_inbox, nameof(TicketStaffProfileUpdatedConsumer), async () =>
        {
            var staff = await _uow.StaffAccounts.GetAllAsync()
                .FirstOrDefaultAsync(s => s.AccountId == @event.AccountId, context.CancellationToken);
            if (staff != null)
            {
                if (staff.LastStaffProfileSourceEventAtUtc is { } applied && applied >= @event.OccurredAt)
                    return;

                staff.EmployeeCode = @event.EmployeeCode;
                staff.MaxConcurrentTickets = @event.MaxConcurrentTickets;
                staff.IsAvailable = @event.IsAvailable;
                staff.SkillTier = (StaffSkillTierEnum)@event.SkillTier;
                staff.LastSyncedAt = DateTime.UtcNow;
                staff.LastStaffProfileSourceEventAtUtc = @event.OccurredAt;
                _uow.StaffAccounts.UpdateAsync(staff);
                await _uow.SaveChangesAsync(context.CancellationToken);
            }
        });
    }
}

public class TicketStaffSkillsUpdatedConsumer : IConsumer<StaffSkillsUpdatedEvent>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IInboxStore _inbox;

    public TicketStaffSkillsUpdatedConsumer(ITicketUnitOfWork uow, IInboxStore inbox)
    {
        _uow = uow;
        _inbox = inbox;
    }

    public async Task Consume(ConsumeContext<StaffSkillsUpdatedEvent> context)
    {
        var @event = context.Message;
        // GH-764 — qua ProcessOnceAsync: giữ chỗ có hạn → chạy → chốt khi xong / nhả khi lỗi.
        // Bản cũ đánh dấu đã-xử-lý TRƯỚC side effect và không bao giờ gỡ, nên một lỗi tạm thời
        // biến thành mất message vĩnh viễn (gửi lại thấy dấu → bỏ qua → ACK).
        await context.ProcessOnceAsync(_inbox, nameof(TicketStaffSkillsUpdatedConsumer), async () =>
        {
            var staff = await _uow.StaffAccounts.GetAllAsync()
                .FirstOrDefaultAsync(s => s.AccountId == @event.AccountId, context.CancellationToken);
            if (staff != null)
            {
                if (staff.LastStaffProfileSourceEventAtUtc is { } applied && applied >= @event.OccurredAt)
                    return;

                staff.SkillCodes = @event.SkillCodes
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Select(code => code.Trim())
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(code => code, StringComparer.Ordinal)
                    .ToList();
                staff.LastSyncedAt = DateTime.UtcNow;
                staff.LastStaffProfileSourceEventAtUtc = @event.OccurredAt;
                _uow.StaffAccounts.UpdateAsync(staff);
                await _uow.SaveChangesAsync(context.CancellationToken);
            }
        });
    }
}
