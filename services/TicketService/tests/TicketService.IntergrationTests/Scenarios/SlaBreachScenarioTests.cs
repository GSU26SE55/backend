using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.BackgroundJobs;
using TicketService.Infrastructure.Persistence;
using TicketService.IntergrationTests.Fixtures;

namespace TicketService.IntergrationTests.Scenarios;

public class SlaBreachScenarioTests : IClassFixture<TicketApiFactory>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly FakeTimeProvider _timeProvider;
    private readonly Mock<IIntegrationEventOutboxWriter> _outboxWriterMock;

    public SlaBreachScenarioTests(TicketApiFactory factory)
    {
        _timeProvider = new FakeTimeProvider();
        _timeProvider.SetUtcNow(new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero));
        _outboxWriterMock = new Mock<IIntegrationEventOutboxWriter>();

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.RemoveAll<IIntegrationEventOutboxWriter>();

                services.AddSingleton<TimeProvider>(_timeProvider);
                services.AddScoped<IIntegrationEventOutboxWriter>(_ => _outboxWriterMock.Object);
                services.AddScoped<SlaTimerBackgroundService>();
                services.AddScoped<EscalationBackgroundService>();
            });
        });
    }

    [Fact]
    public async Task P1Ticket_Should_Escalate_When_Sla_Breached()
    {
        // 1. Create a P1 ticket
        var client = _factory.CreateClient();

        // Manual setup in DB for testing because we might not have the full create workflow ready/easy to call
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketDbContext>();

            var ticket = new TicketService.Domain.Entities.Ticket
            {
                Id = Guid.NewGuid(),
                Code = "TKT-001",
                Title = "Critical System Down",
                Description = "Major outage",
                Status = TicketStatusEnum.Open,
                Priority = TicketPriorityEnum.P1Critical,
                CreatedAt = _timeProvider.GetUtcNow().UtcDateTime,
                CreatedBy = Guid.NewGuid()
            };

            var timer = new TicketService.Domain.Entities.SlaTimer
            {
                Id = Guid.NewGuid(),
                TicketId = ticket.Id,
                Status = SlaTimerStatusEnum.Running,
                StartedAt = ticket.CreatedAt,
                DueAt = ticket.CreatedAt.AddHours(4), // P1 SLA = 4h
                OriginalDueAt = ticket.CreatedAt.AddHours(4),
                Ticket = ticket
            };

            db.Tickets.Add(ticket);
            db.SlaTimers.Add(timer);
            await db.SaveChangesAsync();

            // 2. Advance time to 85% (3.4h later)
            _timeProvider.Advance(TimeSpan.FromHours(3.5));

            // 3. Run SLA Timer Service manually
            var slaService = scope.ServiceProvider.GetRequiredService<SlaTimerBackgroundService>();
            await slaService.CheckSlaViolations(CancellationToken.None);

            // Verify Warning sent
            await db.Entry(timer).ReloadAsync();
            var updatedTimer = await db.SlaTimers.FindAsync(timer.Id);
            updatedTimer!.WarningSentAt.Should().NotBeNull();
            _outboxWriterMock.Verify(x => x.WriteAsync(It.IsAny<SlaWarningEvent>(), It.IsAny<CancellationToken>()), Times.Once);

            // 4. Advance time past breach (another 1h)
            _timeProvider.Advance(TimeSpan.FromHours(1));
            await slaService.CheckSlaViolations(CancellationToken.None);

            // Verify status is Breached
            await db.Entry(timer).ReloadAsync();
            updatedTimer = await db.SlaTimers.FindAsync(timer.Id);
            updatedTimer!.Status.Should().Be(SlaTimerStatusEnum.Breached);

            // Verify Breached Event Published
            _outboxWriterMock.Verify(x => x.WriteAsync(It.IsAny<SlaBreachedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
