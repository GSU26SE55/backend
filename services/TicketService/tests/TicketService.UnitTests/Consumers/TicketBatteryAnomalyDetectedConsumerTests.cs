using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using SharedContracts.Events;
using SharedKernels.Interfaces;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.Consumers;

namespace TicketService.UnitTests.Consumers;

public class BatteryAnomalyDetectedConsumerTests
{
    private readonly Mock<IMediator> _mediatorMock;
    private readonly Mock<ITicketUnitOfWork> _uowMock;
    private readonly Mock<IGenericRepository<Ticket>> _ticketRepoMock;

    public BatteryAnomalyDetectedConsumerTests()
    {
        _mediatorMock = new Mock<IMediator>();
        _uowMock = new Mock<ITicketUnitOfWork>();
        _ticketRepoMock = new Mock<IGenericRepository<Ticket>>();

        _uowMock.SetupGet(u => u.Tickets).Returns(_ticketRepoMock.Object);
    }

    [Fact]
    public async Task Consume_ActiveTicketExists_ShouldSkipAutoCreation()
    {
        // Arrange
        var batteryAssetId = Guid.NewGuid();
        var alertId = Guid.NewGuid();
        var existingTicket = new Ticket
        {
            Code = "TCK-123",
            Title = "Existing Active Ticket",
            Description = "Description",
            BatteryAssetId = batteryAssetId,
            OriginAlertId = alertId,
            Status = TicketStatusEnum.Open,
            IsDeleted = false
        };

        var ticketList = new List<Ticket> { existingTicket }.AsQueryable().BuildMock();
        _ticketRepoMock.Setup(r => r.GetAllAsync()).Returns(ticketList);

        var message = new BatteryAnomalyDetectedEvent(
            AlertId: alertId,
            BatteryAssetId: batteryAssetId,
            CustomerId: Guid.NewGuid(),
            AssetSerialNumber: "SN-123",
            AnomalyType: 1, // Overheat
            Severity: 3,
            ActualValue: 75m,
            ThresholdValue: 60m,
            Unit: "C",
            DetectedAt: DateTime.UtcNow
        );

        var contextMock = new Mock<ConsumeContext<BatteryAnomalyDetectedEvent>>();
        contextMock.SetupGet(c => c.Message).Returns(message);
        contextMock.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);

        var consumer = new TicketBatteryAnomalyDetectedConsumer(
            _mediatorMock.Object,
            _uowMock.Object,
            NullLogger<TicketBatteryAnomalyDetectedConsumer>.Instance
        );

        // Act
        await consumer.Consume(contextMock.Object);

        // Assert
        _mediatorMock.Verify(m => m.Send(It.IsAny<TicketAutoCreateFromAlertCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Consume_NoActiveTicketExists_ShouldSendAutoCreateCommand()
    {
        // Arrange
        var batteryAssetId = Guid.NewGuid();
        var alertId = Guid.NewGuid();

        var ticketList = new List<Ticket>().AsQueryable().BuildMock();
        _ticketRepoMock.Setup(r => r.GetAllAsync()).Returns(ticketList);

        var message = new BatteryAnomalyDetectedEvent(
            AlertId: alertId,
            BatteryAssetId: batteryAssetId,
            CustomerId: Guid.NewGuid(),
            AssetSerialNumber: "SN-123",
            AnomalyType: 1, // Overheat
            Severity: 3,
            ActualValue: 75m,
            ThresholdValue: 60m,
            Unit: "C",
            DetectedAt: DateTime.UtcNow
        );

        var contextMock = new Mock<ConsumeContext<BatteryAnomalyDetectedEvent>>();
        contextMock.SetupGet(c => c.Message).Returns(message);
        contextMock.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);

        var consumer = new TicketBatteryAnomalyDetectedConsumer(
            _mediatorMock.Object,
            _uowMock.Object,
            NullLogger<TicketBatteryAnomalyDetectedConsumer>.Instance
        );

        // Act
        await consumer.Consume(contextMock.Object);

        // Assert
        _mediatorMock.Verify(m => m.Send(It.Is<TicketAutoCreateFromAlertCommand>(cmd =>
            cmd.OriginAlertId == alertId &&
            cmd.BatteryAssetId == batteryAssetId &&
            cmd.AnomalyCategory == "Overheat" &&
            cmd.Title.Contains("Battery Overheating")
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// LowSoc notification-only — pin dùng hết là vận hành bình thường của hệ solar, không phải
    /// sự cố cần người xử lý. KHÔNG được tạo ticket dù chưa có ticket nào đang mở cho pin này.
    /// </summary>
    [Fact]
    public async Task Consume_LowSoc_ShouldSkipAutoCreation_EvenWhenNoActiveTicket()
    {
        var ticketList = new List<Ticket>().AsQueryable().BuildMock();
        _ticketRepoMock.Setup(r => r.GetAllAsync()).Returns(ticketList);

        var message = new BatteryAnomalyDetectedEvent(
            AlertId: Guid.NewGuid(),
            BatteryAssetId: Guid.NewGuid(),
            CustomerId: Guid.NewGuid(),
            AssetSerialNumber: "SN-123",
            AnomalyType: 4, // LowSoc
            Severity: 3,
            ActualValue: 5m,
            ThresholdValue: 10m,
            Unit: "%",
            DetectedAt: DateTime.UtcNow
        );

        var contextMock = new Mock<ConsumeContext<BatteryAnomalyDetectedEvent>>();
        contextMock.SetupGet(c => c.Message).Returns(message);
        contextMock.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);

        var consumer = new TicketBatteryAnomalyDetectedConsumer(
            _mediatorMock.Object,
            _uowMock.Object,
            NullLogger<TicketBatteryAnomalyDetectedConsumer>.Instance
        );

        await consumer.Consume(contextMock.Object);

        _mediatorMock.Verify(
            m => m.Send(It.IsAny<TicketAutoCreateFromAlertCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Đường hiện có không được hỏng: mọi anomaly type KHÁC vẫn tạo ticket như cũ.
    /// </summary>
    [Theory]
    [InlineData(1)]  // Overheat
    [InlineData(2)]  // Overvoltage
    [InlineData(5)]  // RapidDischarge
    [InlineData(8)]  // SohDegradation
    public async Task Consume_OtherAnomalyTypes_ShouldStillSendAutoCreateCommand(int anomalyType)
    {
        var ticketList = new List<Ticket>().AsQueryable().BuildMock();
        _ticketRepoMock.Setup(r => r.GetAllAsync()).Returns(ticketList);

        var message = new BatteryAnomalyDetectedEvent(
            AlertId: Guid.NewGuid(),
            BatteryAssetId: Guid.NewGuid(),
            CustomerId: Guid.NewGuid(),
            AssetSerialNumber: "SN-123",
            AnomalyType: anomalyType,
            Severity: 3,
            ActualValue: 75m,
            ThresholdValue: 60m,
            Unit: "C",
            DetectedAt: DateTime.UtcNow
        );

        var contextMock = new Mock<ConsumeContext<BatteryAnomalyDetectedEvent>>();
        contextMock.SetupGet(c => c.Message).Returns(message);
        contextMock.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);

        var consumer = new TicketBatteryAnomalyDetectedConsumer(
            _mediatorMock.Object,
            _uowMock.Object,
            NullLogger<TicketBatteryAnomalyDetectedConsumer>.Instance
        );

        await consumer.Consume(contextMock.Object);

        _mediatorMock.Verify(
            m => m.Send(It.IsAny<TicketAutoCreateFromAlertCommand>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
