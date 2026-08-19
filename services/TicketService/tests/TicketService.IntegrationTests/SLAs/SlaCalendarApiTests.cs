using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Request.SLAs;
using TicketService.Application.DTOs.Response.SLAs;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.Implements.Utils;
using TicketService.Infrastructure.Persistence;
using TicketService.IntegrationTests.Fixtures;

namespace TicketService.IntegrationTests.SLAs;

public class SlaCalendarApiTests : IClassFixture<TicketApiFactory>
{
    private readonly TicketApiFactory _factory;
    private readonly HttpClient _client;

    public SlaCalendarApiTests(TicketApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketDbContext>();
        db.Database.EnsureDeleted();
        db.Database.EnsureCreated();
    }

    [Fact]
    public async Task Manager_CanCreateListUpdateAndDeletePeriod()
    {
        var start = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10));
        var create = await _client.PostAsJsonAsync("/api/sla/non-working-periods", new SlaNonWorkingPeriodRequest
        {
            StartDate = start,
            EndDate = start.AddDays(2),
            Reason = "Company holiday"
        });

        create.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await create.Content.ReadFromJsonAsync<CommonResponse<SlaNonWorkingPeriodDto>>();
        created!.IsSuccess.Should().BeTrue();
        var id = created.Data!.Id;

        var list = await _client.GetFromJsonAsync<CommonResponse<PaginationResponse<SlaNonWorkingPeriodDto>>>(
            "/api/sla/non-working-periods?pageNumber=1&pageSize=10");
        list!.Data!.Items.Should().ContainSingle(x => x.Id == id);

        var update = await _client.PutAsJsonAsync($"/api/sla/non-working-periods/{id}", new SlaNonWorkingPeriodRequest
        {
            StartDate = start.AddDays(1),
            EndDate = start.AddDays(3),
            Reason = "Updated holiday"
        });
        update.StatusCode.Should().Be(HttpStatusCode.OK);

        using var deleteRequest = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/sla/non-working-periods/{id}");
        using var delete = await _client.SendAsync(deleteRequest);
        delete.StatusCode.Should().Be(HttpStatusCode.OK);
        var afterDelete = await _client.GetFromJsonAsync<CommonResponse<PaginationResponse<SlaNonWorkingPeriodDto>>>(
            "/api/sla/non-working-periods?pageNumber=1&pageSize=10");
        afterDelete!.Data!.Items.Should().NotContain(x => x.Id == id);
    }

    [Fact]
    public async Task Create_OverlappingRange_ReturnsConflict()
    {
        var start = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(20));
        var body = new SlaNonWorkingPeriodRequest { StartDate = start, EndDate = start.AddDays(2), Reason = "First" };
        (await _client.PostAsJsonAsync("/api/sla/non-working-periods", body)).EnsureSuccessStatusCode();

        var overlap = await _client.PostAsJsonAsync("/api/sla/non-working-periods", new SlaNonWorkingPeriodRequest
        {
            StartDate = start.AddDays(2),
            EndDate = start.AddDays(4),
            Reason = "Overlap"
        });

        overlap.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_ReconcilesActiveTimerAgainstDeclaredDate()
    {
        var holiday = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30));
        var startedAt = new DateTime(holiday.Year, holiday.Month, holiday.Day, 0, 0, 0, DateTimeKind.Utc).AddDays(-1);
        var originalDueAt = new SlaCalculator().CalculateDueDate(startedAt, TicketPriorityEnum.P2High);
        var ticketId = Guid.NewGuid();
        var timerId = Guid.NewGuid();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketDbContext>();
            db.Tickets.Add(new Ticket
            {
                Id = ticketId,
                Code = "TKT-CALENDAR",
                Title = "Calendar reconciliation",
                Description = "Test",
                Category = TicketCategoryEnum.Other,
                CustomerId = Guid.NewGuid(),
                BatteryAssetId = Guid.NewGuid(),
                Status = TicketStatusEnum.InProgress,
                Priority = TicketPriorityEnum.P2High,
                Origin = TicketOriginEnum.ManualByCustomer
            });
            db.SlaTimers.Add(new SlaTimer
            {
                Id = timerId,
                TicketId = ticketId,
                Priority = TicketPriorityEnum.P2High,
                Status = SlaTimerStatusEnum.Running,
                StartedAt = startedAt,
                OriginalDueAt = originalDueAt,
                DueAt = originalDueAt
            });
            await db.SaveChangesAsync();
        }

        var response = await _client.PostAsJsonAsync("/api/sla/non-working-periods", new SlaNonWorkingPeriodRequest
        {
            StartDate = holiday,
            EndDate = holiday,
            Reason = "Reconciliation test"
        });
        response.EnsureSuccessStatusCode();

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var updated = await verifyScope.ServiceProvider.GetRequiredService<TicketDbContext>().SlaTimers.FindAsync(timerId);
        updated!.DueAt.Should().BeAfter(originalDueAt);
        updated.OriginalDueAt.Should().Be(updated.DueAt);
    }

    [Fact]
    public async Task Staff_CannotAccessCalendarManagement()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/sla/non-working-periods");
        request.Headers.Add(TestAuthHandler.RolesHeader, "Staff");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Create_RejectsPastAndReversedRanges_ButAllowsCurrentLocalDate()
    {
        var localToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(
            DateTime.UtcNow, "Asia/Ho_Chi_Minh"));

        var past = await _client.PostAsJsonAsync("/api/sla/non-working-periods",
            new SlaNonWorkingPeriodRequest
            {
                StartDate = localToday.AddDays(-1),
                EndDate = localToday,
                Reason = "Past"
            });
        var reversed = await _client.PostAsJsonAsync("/api/sla/non-working-periods",
            new SlaNonWorkingPeriodRequest
            {
                StartDate = localToday.AddDays(2),
                EndDate = localToday.AddDays(1),
                Reason = "Reversed"
            });
        var current = await _client.PostAsJsonAsync("/api/sla/non-working-periods",
            new SlaNonWorkingPeriodRequest
            {
                StartDate = localToday,
                EndDate = localToday,
                Reason = "Current local date"
            });

        past.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        reversed.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        current.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ReconciledDueAt_PreventsWorkerFromPersistingStaleBreach()
    {
        var ticketId = Guid.NewGuid();
        var timerId = Guid.NewGuid();
        await using (var setupScope = _factory.Services.CreateAsyncScope())
        {
            var db = setupScope.ServiceProvider.GetRequiredService<TicketDbContext>();
            db.Tickets.Add(new Ticket
            {
                Id = ticketId,
                Code = "TKT-SLA-RACE",
                Title = "SLA race",
                Description = "Concurrency test",
                Category = TicketCategoryEnum.Other,
                CustomerId = Guid.NewGuid(),
                BatteryAssetId = Guid.NewGuid(),
                Status = TicketStatusEnum.InProgress,
                Priority = TicketPriorityEnum.P1Critical,
                Origin = TicketOriginEnum.ManualByCustomer
            });
            db.SlaTimers.Add(new SlaTimer
            {
                Id = timerId,
                TicketId = ticketId,
                Priority = TicketPriorityEnum.P1Critical,
                StartedAt = DateTime.UtcNow.AddHours(-4),
                OriginalDueAt = DateTime.UtcNow,
                DueAt = DateTime.UtcNow,
                Status = SlaTimerStatusEnum.Running
            });
            await db.SaveChangesAsync();
        }

        await using var calendarScope = _factory.Services.CreateAsyncScope();
        await using var workerScope = _factory.Services.CreateAsyncScope();
        var calendarDb = calendarScope.ServiceProvider.GetRequiredService<TicketDbContext>();
        var workerDb = workerScope.ServiceProvider.GetRequiredService<TicketDbContext>();
        var reconciledTimer = await calendarDb.SlaTimers.SingleAsync(x => x.Id == timerId);
        var staleWorkerTimer = await workerDb.SlaTimers.SingleAsync(x => x.Id == timerId);

        reconciledTimer.DueAt = reconciledTimer.DueAt.AddDays(1);
        await calendarDb.SaveChangesAsync();

        staleWorkerTimer.Status = SlaTimerStatusEnum.Breached;
        var saveStaleBreach = () => workerDb.SaveChangesAsync();

        await saveStaleBreach.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }
}
