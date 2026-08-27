using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedContracts.Events;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.Persistence;
using TicketService.IntegrationTests.Fixtures;

namespace TicketService.IntegrationTests.Scenarios;

/// <summary>
/// Chu kỳ bảo trì định kỳ: lịch do khách tự chọn, và việc Manager thay lịch sau khi lịch đó
/// quá hạn — chạy trên DbContext, UnitOfWork và outbox thật.
/// </summary>
/// <remarks>
/// <para>
/// Dấu hiệu "ticket bảo trì định kỳ" là <c>PeriodicMaintenanceDueAtUtc</c>. Cột
/// <c>PeriodicMaintenanceSourceTicketId</c> đã bị gỡ: từ khi lịch chuyển sang tầng tài sản,
/// ticket sinh ra từ một kỳ bảo trì của pin chứ không neo vào ticket đã đóng.
/// </para>
/// <para>
/// Điểm cần giữ: lịch khách đã chọn là <b>bất khả xâm phạm khi còn hiệu lực</b>. Manager chỉ được
/// thay khi lịch đã trôi qua, và phải ghi lại lý do đã liên hệ khách. Nếu ràng buộc này lỏng ra thì
/// Manager có thể dời lịch của khách bất cứ lúc nào mà không để lại dấu vết.
/// </para>
/// <para>
/// Kiểm chứng ở mức lệnh thay vì HTTP: đường HTTP đã có <c>TicketApiTests</c> phủ, còn cái cần
/// nhìn ở đây là hàng ghi vào <c>outbox_messages</c> và <c>ticket_activities</c> có nằm cùng một
/// giao dịch với thay đổi lịch hay không.
/// </para>
/// </remarks>
public class PeriodicMaintenanceCycleFlowTests : IClassFixture<TicketApiFactory>
{
    private static readonly Guid ManagerId = Guid.Parse("00000000-0000-0000-0000-000000000009");
    private static readonly Guid StaffId = Guid.Parse("00000000-0000-0000-0000-000000000010");
    private static readonly Guid CustomerId = Guid.Parse("00000000-0000-0000-0000-000000000011");

    private readonly TicketApiFactory _factory;

    public PeriodicMaintenanceCycleFlowTests(TicketApiFactory factory)
    {
        _factory = factory;

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketDbContext>();
        db.Database.EnsureDeleted();
        db.Database.EnsureCreated();

        db.CustomerAccounts.Add(new CustomerAccount
        {
            AccountId = CustomerId,
            Email = "customer@test.com",
            FullName = "Khach Bao Tri",
            Status = AccountStatusEnum.Active,
            LastSyncedAt = DateTime.UtcNow
        });
        db.StaffAccounts.Add(new StaffAccount
        {
            AccountId = StaffId,
            Email = "staff@test.com",
            FullName = "Ky Thuat Vien",
            Role = "Staff",
            Status = AccountStatusEnum.Active,
            IsAvailable = true,
            SkillTier = StaffSkillTierEnum.SeniorSpecialist,
            LastSyncedAt = DateTime.UtcNow
        });
        db.SaveChanges();
    }

    /// <summary>
    /// Lịch khách chọn đã quá hạn: Manager thay được, và hệ thống phải để lại đủ ba dấu vết —
    /// lịch mới trên ticket, event báo đổi lịch trong outbox, và một dòng nhật ký hoạt động.
    /// </summary>
    [Fact]
    public async Task Assign_AfterCustomerScheduleExpired_UpdatesSchedule_WritesEventAndActivity()
    {
        var ticketId = await SeedPeriodicMaintenanceTicketAsync(
            customerScheduledAtUtc: DateTime.UtcNow.AddDays(-3),
            scheduledStartAtUtc: DateTime.UtcNow.AddDays(-3));

        var newSchedule = DateTimeOffset.UtcNow.AddDays(2);
        var response = await AssignAsync(ticketId, newSchedule, notes: "Đã gọi khách, khách đồng ý dời sang thứ Năm.");

        response.IsSuccess.Should().BeTrue(response.Message);

        await using var db = NewDbContext();
        var ticket = await db.Tickets.AsNoTracking().SingleAsync(t => t.Id == ticketId);
        ticket.Status.Should().Be(TicketStatusEnum.Pending);
        ticket.ScheduledStartAtUtc.Should().BeCloseTo(newSchedule.UtcDateTime, TimeSpan.FromSeconds(5));
        ticket.ScheduleVersion.Should().Be(2, "mỗi lần đổi lịch phải tăng phiên bản để chống ghi đè chéo");
        ticket.PendingContext.Should().Be(PendingContextEnum.Scheduled);

        var outbox = await db.OutboxMessages.AsNoTracking()
            .Where(m => m.Type == nameof(PeriodicMaintenanceScheduleChangedEvent))
            .ToListAsync();
        outbox.Should().ContainSingle();

        var evt = JsonSerializer.Deserialize<PeriodicMaintenanceScheduleChangedEvent>(outbox[0].Payload);
        evt.Should().NotBeNull();
        evt!.TicketId.Should().Be(ticketId);
        evt.CustomerId.Should().Be(CustomerId);
        evt.ScheduleVersion.Should().Be(2);
        evt.IsOverdue.Should().BeTrue("hạn bảo trì đã trôi qua tại thời điểm đổi lịch");

        var activity = await db.TicketActivities.AsNoTracking()
            .Where(a => a.TicketId == ticketId && a.Action == ActivityActionEnum.PeriodicMaintenanceScheduleChanged)
            .ToListAsync();
        activity.Should().ContainSingle();
        activity[0].Reason.Should().NotBeNullOrWhiteSpace("lý do liên hệ khách là bắt buộc và phải lưu lại");
    }

    /// <summary>
    /// Lịch khách chọn còn hiệu lực thì không ai được đổi — kể cả Manager.
    /// </summary>
    [Fact]
    public async Task Assign_WhileCustomerScheduleStillValid_IsRejectedWith409()
    {
        var ticketId = await SeedPeriodicMaintenanceTicketAsync(
            customerScheduledAtUtc: DateTime.UtcNow.AddDays(5),
            scheduledStartAtUtc: DateTime.UtcNow.AddDays(5));

        var response = await AssignAsync(ticketId, DateTimeOffset.UtcNow.AddDays(1), notes: "Muốn dời sớm hơn.");

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(409);

        await using var db = NewDbContext();
        var ticket = await db.Tickets.AsNoTracking().SingleAsync(t => t.Id == ticketId);
        ticket.ScheduleVersion.Should().Be(1, "yêu cầu bị từ chối thì không được đụng vào lịch");
        (await db.OutboxMessages.AsNoTracking()
            .AnyAsync(m => m.Type == nameof(PeriodicMaintenanceScheduleChangedEvent)))
            .Should().BeFalse();
    }

    /// <summary>
    /// Thay lịch đã quá hạn mà không ghi lý do thì bị chặn: dòng nhật ký trống không chứng minh
    /// được là đã liên hệ khách.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Assign_ReplacingExpiredScheduleWithoutNotes_IsRejectedWith400(string? notes)
    {
        var ticketId = await SeedPeriodicMaintenanceTicketAsync(
            customerScheduledAtUtc: DateTime.UtcNow.AddDays(-1),
            scheduledStartAtUtc: DateTime.UtcNow.AddDays(-1));

        var response = await AssignAsync(ticketId, DateTimeOffset.UtcNow.AddDays(3), notes);

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(400);

        await using var db = NewDbContext();
        (await db.TicketActivities.AsNoTracking()
            .AnyAsync(a => a.TicketId == ticketId
                        && a.Action == ActivityActionEnum.PeriodicMaintenanceScheduleChanged))
            .Should().BeFalse();
    }

    /// <summary>
    /// Lịch thay thế không được nằm trong quá khứ — kể cả khi Manager chỉ định gõ lại đúng lịch
    /// cũ đã quá hạn. Cửa sổ "hiện tại" chỉ rộng năm phút.
    /// </summary>
    /// <remarks>
    /// Chốt chặn này nằm trước mọi kiểm tra khác của lệnh phân công, nên một lịch quá khứ bị chặn
    /// ngay cả khi mọi điều kiện còn lại đều hợp lệ. Không có nó, Manager có thể ghi một buổi bảo
    /// trì "đã diễn ra" mà thực tế chưa ai làm.
    /// </remarks>
    [Fact]
    public async Task Assign_WithScheduleInThePast_IsRejectedAndLeavesTicketUntouched()
    {
        var expired = DateTime.UtcNow.AddDays(-2);
        var ticketId = await SeedPeriodicMaintenanceTicketAsync(
            customerScheduledAtUtc: expired,
            scheduledStartAtUtc: expired);

        var response = await AssignAsync(
            ticketId, new DateTimeOffset(expired, TimeSpan.Zero), notes: "Giữ nguyên lịch cũ.");

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(400);

        await using var db = NewDbContext();
        var ticket = await db.Tickets.AsNoTracking().SingleAsync(t => t.Id == ticketId);
        ticket.Status.Should().Be(TicketStatusEnum.Open, "lệnh bị chặn thì ticket phải giữ nguyên trạng thái");
        ticket.ScheduleVersion.Should().Be(1);

        (await db.OutboxMessages.AsNoTracking()
            .AnyAsync(m => m.Type == nameof(PeriodicMaintenanceScheduleChangedEvent)))
            .Should().BeFalse();
    }

    /// <summary>
    /// Ticket thường (không thuộc chu kỳ bảo trì định kỳ) đi đường phân công bình thường và
    /// không được kéo theo event của luồng bảo trì.
    /// </summary>
    [Fact]
    public async Task Assign_NonPeriodicTicket_AssignsWithoutPeriodicEvent()
    {
        var ticketId = await SeedPeriodicMaintenanceTicketAsync(
            customerScheduledAtUtc: null,
            scheduledStartAtUtc: null,
            periodic: false);

        var response = await AssignAsync(ticketId, DateTimeOffset.UtcNow.AddDays(1), notes: null);

        response.IsSuccess.Should().BeTrue(response.Message);

        await using var db = NewDbContext();
        (await db.OutboxMessages.AsNoTracking()
            .AnyAsync(m => m.Type == nameof(PeriodicMaintenanceScheduleChangedEvent)))
            .Should().BeFalse();

        var ticket = await db.Tickets.AsNoTracking().SingleAsync(t => t.Id == ticketId);
        ticket.Status.Should().Be(TicketStatusEnum.Pending);
    }

    // ---------- helpers ----------

    private TicketDbContext NewDbContext()
    {
        var scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<TicketDbContext>();
    }

    private async Task<Guid> SeedPeriodicMaintenanceTicketAsync(
        DateTime? customerScheduledAtUtc,
        DateTime? scheduledStartAtUtc,
        bool periodic = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketDbContext>();

        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            Code = $"PM-{Guid.NewGuid():N}"[..12],
            BatteryAssetId = Guid.NewGuid(),
            CustomerId = CustomerId,
            Title = "Bảo trì định kỳ 6 tháng",
            Description = "Chu kỳ bảo trì định kỳ do hệ thống sinh.",
            Category = TicketCategoryEnum.Other,
            Status = TicketStatusEnum.Open,
            Origin = TicketOriginEnum.System,
            ScheduledStartAtUtc = scheduledStartAtUtc,
            ScheduleVersion = 1,
            PeriodicMaintenanceDueAtUtc = periodic ? DateTime.UtcNow.AddDays(-1) : null,
            PeriodicMaintenanceCustomerScheduledAtUtc = customerScheduledAtUtc,
            CreatedAt = DateTime.UtcNow
        };

        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();
        return ticket.Id;
    }

    private async Task<TicketService.Application.DTOs.Response.Tickets.TicketActionResponse> AssignAsync(
        Guid ticketId, DateTimeOffset scheduledStartAt, string? notes)
    {
        using var scope = _factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        return await mediator.Send(new TicketAssignCommand
        {
            TicketId = ticketId,
            PrimaryHandlerStaffId = StaffId,
            Priority = TicketPriorityEnum.P3Normal,
            ScheduledStartAt = scheduledStartAt,
            Notes = notes,
            ManagerId = ManagerId,
            ManagerName = "Quan Ly Test"
        });
    }
}
