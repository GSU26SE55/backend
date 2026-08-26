using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedContracts.Events;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.Persistence;
using TicketService.IntegrationTests.Fixtures;

namespace TicketService.IntegrationTests.Scenarios;

/// <summary>
/// Khách tự chọn giờ cho chuyến bảo trì định kỳ.
/// </summary>
/// <remarks>
/// <para>
/// Đây là nửa còn lại của luồng nhắc: worker nhắc khách ba lần, nhưng nếu khách không có chỗ
/// trả lời thì mốc thứ ba luôn rơi vào tay Manager và mục đích "để khách nói giờ nào tiện
/// thay vì bị áp đặt" không bao giờ đạt được.
/// </para>
/// <para>
/// Bộ test chạy trên DbContext thật để thấy được thứ mock không nói: giờ khách chọn có thật
/// sự nằm trên ticket sau khi lưu, <c>ScheduleVersion</c> có tăng, và mốc
/// <c>PeriodicMaintenanceCustomerScheduledAtUtc</c> — thứ khoá khối bảo vệ lịch bên
/// <c>TicketAssignCommandHandler</c> — có được đặt hay không.
/// </para>
/// </remarks>
public class CustomerSchedulePeriodicMaintenanceTests : IClassFixture<TicketApiFactory>
{
    private static readonly Guid OwnerId = Guid.Parse("00000000-0000-0000-0000-0000000000b1");
    private static readonly Guid StrangerId = Guid.Parse("00000000-0000-0000-0000-0000000000b2");

    private readonly TicketApiFactory _factory;

    public CustomerSchedulePeriodicMaintenanceTests(TicketApiFactory factory)
    {
        _factory = factory;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketDbContext>();
        db.Database.EnsureDeleted();
        db.Database.EnsureCreated();
    }

    [Fact]
    public async Task Owner_PicksATimeInsideTheWindow_ScheduleIsSaved()
    {
        var ticketId = await SeedAsync();
        var chosen = DateTimeOffset.UtcNow.AddDays(3);

        var response = await ScheduleAsync(ticketId, OwnerId, chosen);

        response.IsSuccess.Should().BeTrue(response.Message);
        response.StatusCode.Should().Be(200);

        await using var db = NewDbContext();
        var ticket = await db.Tickets.AsNoTracking().SingleAsync(t => t.Id == ticketId);
        ticket.ScheduledStartAtUtc.Should().BeCloseTo(chosen.UtcDateTime, TimeSpan.FromSeconds(5));
        ticket.ScheduleVersion.Should().Be(1);

        // Mốc này là thứ khoá khối bảo vệ lịch bên TicketAssignCommandHandler: thiếu nó,
        // Manager dời được lịch khách vừa chọn mà không cần lý do.
        ticket.PeriodicMaintenanceCustomerScheduledAtUtc.Should().NotBeNull();
    }

    /// <summary>Chọn xong thì worker phải im — mốc nhắc kế tiếp không còn nợ.</summary>
    [Fact]
    public async Task AfterScheduling_TheTicketLeavesTheReminderQueue()
    {
        var ticketId = await SeedAsync();
        await ScheduleAsync(ticketId, OwnerId, DateTimeOffset.UtcNow.AddDays(2));

        await using var db = NewDbContext();
        var ticket = await db.Tickets.AsNoTracking().SingleAsync(t => t.Id == ticketId);

        // Worker chỉ lấy ticket có ScheduledStartAtUtc == null.
        ticket.ScheduledStartAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task ScheduleChange_IsRecordedInTheActivityLog()
    {
        var ticketId = await SeedAsync();

        await ScheduleAsync(ticketId, OwnerId, DateTimeOffset.UtcNow.AddDays(2));

        await using var db = NewDbContext();
        var activity = await db.TicketActivities.AsNoTracking()
            .Where(a => a.TicketId == ticketId
                     && a.Action == ActivityActionEnum.PeriodicMaintenanceScheduleChanged)
            .ToListAsync();

        activity.Should().ContainSingle();
        activity[0].ActorRole.Should().Be(ActorRoleEnum.Customer);
    }

    [Fact]
    public async Task ScheduleChange_EmitsTheEventEveryoneFollowingTheTicketNeeds()
    {
        var ticketId = await SeedAsync();

        await ScheduleAsync(ticketId, OwnerId, DateTimeOffset.UtcNow.AddDays(2));

        await using var db = NewDbContext();
        (await db.OutboxMessages.AsNoTracking()
            .AnyAsync(m => m.Type == nameof(PeriodicMaintenanceScheduleChangedEvent)))
            .Should().BeTrue();
    }

    // ---------- các nhánh từ chối ----------

    /// <summary>
    /// Người không sở hữu ticket bị chặn bằng 403 chứ không phải 404: thứ tự kiểm tra đặt
    /// quyền sở hữu sau khi tra ticket, nên phản hồi không dùng để dò xem ticket có tồn tại.
    /// </summary>
    [Fact]
    public async Task ANonOwner_IsRefused()
    {
        var ticketId = await SeedAsync();

        var response = await ScheduleAsync(ticketId, StrangerId, DateTimeOffset.UtcNow.AddDays(2));

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(403);
        await AssertUntouchedAsync(ticketId);
    }

    [Fact]
    public async Task ATimeInThePast_IsRefused()
    {
        var ticketId = await SeedAsync();

        var response = await ScheduleAsync(ticketId, OwnerId, DateTimeOffset.UtcNow.AddHours(-1));

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(400);
        await AssertUntouchedAsync(ticketId);
    }

    [Fact]
    public async Task ATimeBeyondTheDeadline_IsRefused()
    {
        var ticketId = await SeedAsync(deadlineInDays: 5);

        var response = await ScheduleAsync(ticketId, OwnerId, DateTimeOffset.UtcNow.AddDays(9));

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(400);
        await AssertUntouchedAsync(ticketId);
    }

    /// <summary>Cửa sổ đã đóng thì Manager sắp thay, khách không chọn được nữa.</summary>
    [Fact]
    public async Task AnExpiredWindow_IsRefused()
    {
        var ticketId = await SeedAsync(deadlineInDays: -1);

        var response = await ScheduleAsync(ticketId, OwnerId, DateTimeOffset.UtcNow.AddDays(1));

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(409);
        await AssertUntouchedAsync(ticketId);
    }

    /// <summary>Ticket đã được giao thì lịch thuộc về Manager, không phải khách.</summary>
    [Fact]
    public async Task AnAlreadyAssignedTicket_IsRefused()
    {
        var ticketId = await SeedAsync(status: TicketStatusEnum.InProgress);

        var response = await ScheduleAsync(ticketId, OwnerId, DateTimeOffset.UtcNow.AddDays(2));

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(409);
    }

    /// <summary>Endpoint này chỉ dành cho ticket bảo trì định kỳ, không phải mọi ticket.</summary>
    [Fact]
    public async Task ANonPeriodicTicket_IsRefused()
    {
        var ticketId = await SeedAsync(periodic: false);

        var response = await ScheduleAsync(ticketId, OwnerId, DateTimeOffset.UtcNow.AddDays(2));

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task AnUnknownTicket_IsRefused()
    {
        var response = await ScheduleAsync(Guid.NewGuid(), OwnerId, DateTimeOffset.UtcNow.AddDays(2));

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(404);
    }

    // ---------- helpers ----------

    private TicketDbContext NewDbContext() =>
        _factory.Services.CreateScope().ServiceProvider.GetRequiredService<TicketDbContext>();

    private async Task<Guid> SeedAsync(
        int deadlineInDays = 7,
        TicketStatusEnum status = TicketStatusEnum.Open,
        bool periodic = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketDbContext>();

        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            Code = $"PM-{Guid.NewGuid():N}"[..12],
            BatteryAssetId = Guid.NewGuid(),
            CustomerId = OwnerId,
            Title = "Periodic battery maintenance",
            Description = "Scheduled maintenance cycle.",
            Category = TicketCategoryEnum.Repair,
            Status = status,
            Origin = TicketOriginEnum.System,
            PeriodicMaintenanceDueAtUtc = periodic ? DateTime.UtcNow.AddDays(deadlineInDays) : null,
            PeriodicMaintenanceScheduleDeadlineAtUtc =
                periodic ? DateTime.UtcNow.AddDays(deadlineInDays) : null,
            CreatedAt = DateTime.UtcNow,
        };

        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();
        return ticket.Id;
    }

    private async Task<TicketActionResponse> ScheduleAsync(
        Guid ticketId, Guid customerId, DateTimeOffset scheduledStartAt)
    {
        using var scope = _factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        return await mediator.Send(new CustomerSchedulePeriodicMaintenanceCommand
        {
            TicketId = ticketId,
            CustomerId = customerId,
            ScheduledStartAt = scheduledStartAt,
        });
    }

    private async Task AssertUntouchedAsync(Guid ticketId)
    {
        await using var db = NewDbContext();
        var ticket = await db.Tickets.AsNoTracking().SingleAsync(t => t.Id == ticketId);
        ticket.ScheduledStartAtUtc.Should().BeNull("yêu cầu bị từ chối thì không được đụng vào lịch");
        ticket.PeriodicMaintenanceCustomerScheduledAtUtc.Should().BeNull();
        ticket.ScheduleVersion.Should().Be(0);
    }
}
