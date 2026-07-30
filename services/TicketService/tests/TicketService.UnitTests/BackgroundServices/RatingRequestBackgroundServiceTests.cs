using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.BackgroundJobs;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.BackgroundServices;

/// <summary>
/// Sprint 6.2 NOTI-07 (#678) — nhắc Customer đánh giá ticket treo ở CLOSED_PENDING_RATE.
/// Mốc nhắc mặc định 3 ngày (cấu hình <c>Ticket:RatingRequest:AfterDays</c>) vì
/// <c>AutoCloseBackgroundService</c> tự đóng ticket đúng ngày thứ 7 — nhắc đúng hôm đó thì vô nghĩa.
/// </summary>
public class RatingRequestBackgroundServiceTests
{
    private static Ticket PendingRate(DateTime approvedAt, bool alreadyRequested = false)
    {
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            Code = "TKT-RATE",
            Title = "T",
            Description = "D",
            Status = TicketStatusEnum.ClosedPendingRate,
            CustomerId = Guid.NewGuid(),
            ApprovedAt = approvedAt,
        };

        if (alreadyRequested)
        {
            ticket.Activities.Add(new TicketActivity
            {
                Id = Guid.NewGuid(),
                TicketId = ticket.Id,
                Ticket = ticket,
                Action = ActivityActionEnum.RatingRequested,
                ActorRole = ActorRoleEnum.System,
                ActorDisplayName = "System",
            });
        }

        return ticket;
    }

    private static (RatingRequestBackgroundService sut, Mock<IMessageProducerService> producer) Build(
        IEnumerable<Ticket> tickets,
        Dictionary<string, string?>? config = null)
    {
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: tickets);
        var producer = new Mock<IMessageProducerService>();

        var services = new ServiceCollection();
        services.AddSingleton(uow.Object);
        services.AddSingleton(producer.Object);
        var provider = services.BuildServiceProvider();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(config ?? new Dictionary<string, string?>())
            .Build();

        var sut = new RatingRequestBackgroundService(
            NullLogger<RatingRequestBackgroundService>.Instance,
            provider.GetRequiredService<IServiceScopeFactory>(),
            configuration);

        return (sut, producer);
    }

    [Fact]
    public async Task RequestPendingRatings_TicketOverdue_PublishesRatingRequestedEvent()
    {
        var ticket = PendingRate(DateTime.UtcNow.AddDays(-4));
        var (sut, producer) = Build([ticket]);

        await sut.RequestPendingRatingsAsync(CancellationToken.None);

        producer.Verify(p => p.PublishAsync(
            It.Is<TicketRatingRequestedEvent>(e =>
                e.TicketId == ticket.Id &&
                e.CustomerId == ticket.CustomerId &&
                e.DaysPending >= 4 &&
                e.DaysUntilAutoClose == 3),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RequestPendingRatings_TicketNotYetDue_PublishesNothing()
    {
        var ticket = PendingRate(DateTime.UtcNow.AddDays(-1));
        var (sut, producer) = Build([ticket]);

        await sut.RequestPendingRatingsAsync(CancellationToken.None);

        producer.Verify(p => p.PublishAsync(
            It.IsAny<TicketRatingRequestedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>Idempotent: đã có activity RatingRequested thì không nhắc lại vòng sau.</summary>
    [Fact]
    public async Task RequestPendingRatings_AlreadyRequested_DoesNotRepeat()
    {
        var ticket = PendingRate(DateTime.UtcNow.AddDays(-5), alreadyRequested: true);
        var (sut, producer) = Build([ticket]);

        await sut.RequestPendingRatingsAsync(CancellationToken.None);

        producer.Verify(p => p.PublishAsync(
            It.IsAny<TicketRatingRequestedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RequestPendingRatings_TicketAlreadyClosed_IsSkipped()
    {
        var ticket = PendingRate(DateTime.UtcNow.AddDays(-5));
        ticket.Status = TicketStatusEnum.Closed;
        var (sut, producer) = Build([ticket]);

        await sut.RequestPendingRatingsAsync(CancellationToken.None);

        producer.Verify(p => p.PublishAsync(
            It.IsAny<TicketRatingRequestedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RequestPendingRatings_RespectsConfiguredAfterDays()
    {
        var ticket = PendingRate(DateTime.UtcNow.AddDays(-2));
        var (sut, producer) = Build([ticket], new Dictionary<string, string?>
        {
            ["Ticket:RatingRequest:AfterDays"] = "1"
        });

        await sut.RequestPendingRatingsAsync(CancellationToken.None);

        producer.Verify(p => p.PublishAsync(
            It.IsAny<TicketRatingRequestedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
