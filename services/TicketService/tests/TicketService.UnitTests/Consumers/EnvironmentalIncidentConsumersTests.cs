using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using MockQueryable.Moq;
using Moq;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using SharedKernels.Interfaces;
using TicketService.Application.Consumers;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.UnitTests.Consumers;

/// <summary>
/// Sprint Bonus NS-22 (#662, E2) — TicketService consume EnvironmentalIncidentDetected/Resolved.
/// </summary>
public class EnvironmentalIncidentConsumersTests
{
    private readonly Mock<ITicketUnitOfWork> _uow = new();
    private readonly Mock<IGenericRepository<Ticket>> _ticketRepo = new();
    private readonly Mock<IGenericRepository<SlaTimer>> _slaTimerRepo = new();
    private readonly Mock<IActivityLogger> _activityLogger = new();
    private readonly Mock<ITicketCodeGenerator> _codeGenerator = new();
    private readonly Mock<IIntegrationEventOutboxWriter> _outboxWriter = new();
    private readonly ISlaCalculator _slaCalculator = new TicketService.Infrastructure.Implements.Utils.SlaCalculator();
    private readonly Mock<TimeProvider> _timeProvider = new();

    public EnvironmentalIncidentConsumersTests()
    {
        _uow.SetupGet(u => u.Tickets).Returns(_ticketRepo.Object);
        _uow.SetupGet(u => u.SlaTimers).Returns(_slaTimerRepo.Object);
        _ticketRepo.Setup(r => r.GetAllAsync()).Returns(new List<Ticket>().AsQueryable().BuildMock());
        _slaTimerRepo.Setup(r => r.GetAllAsync()).Returns(new List<SlaTimer>().AsQueryable().BuildMock());
        _codeGenerator.Setup(g => g.GenerateAsync()).ReturnsAsync("TKT-ENV-001");
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _timeProvider.Setup(t => t.GetUtcNow())
            .Returns(new DateTimeOffset(2026, 8, 17, 2, 0, 0, TimeSpan.Zero));
    }

    private static ConsumeContext<T> Ctx<T>(T msg) where T : class
    {
        var ctx = new Mock<ConsumeContext<T>>();
        ctx.SetupGet(c => c.Message).Returns(msg);
        ctx.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return ctx.Object;
    }

    private static EnvironmentalIncidentDetectedEvent Detected(Guid incidentId, int severity = 3) => new(
        IncidentId: incidentId, SiteId: Guid.NewGuid(), CustomerId: Guid.NewGuid(),
        SiteName: "Kho A", IncidentType: 1, Severity: severity,
        DetectedAt: DateTime.UtcNow, AlertId: Guid.NewGuid(), Description: "Khói");

    private TicketEnvironmentalIncidentDetectedConsumer BuildDetected() =>
        new(_uow.Object, _codeGenerator.Object, _slaCalculator, _activityLogger.Object, _outboxWriter.Object,
            _timeProvider.Object, NullLogger<TicketEnvironmentalIncidentDetectedConsumer>.Instance);

    private TicketEnvironmentalIncidentResolvedConsumer BuildResolved() =>
        new(_uow.Object, _activityLogger.Object, NullLogger<TicketEnvironmentalIncidentResolvedConsumer>.Instance);

    // ── Detected ──

    [Fact]
    public async Task Detected_Critical_CreatesP1Ticket_LinkedToIncident_WithTimer()
    {
        var incidentId = Guid.NewGuid();
        Ticket? created = null;
        _ticketRepo.Setup(r => r.AddAsync(It.IsAny<Ticket>())).Callback<Ticket>(t => created = t).Returns(Task.CompletedTask);
        SlaTimer? timer = null;
        _slaTimerRepo.Setup(r => r.AddAsync(It.IsAny<SlaTimer>())).Callback<SlaTimer>(t => timer = t).Returns(Task.CompletedTask);

        await BuildDetected().Consume(Ctx(Detected(incidentId, severity: 3)));

        created.Should().NotBeNull("khói/ngập Critical phải auto-tạo ticket (không chỉ notify)");
        created!.Priority.Should().Be(TicketPriorityEnum.P1Critical);
        created.EnvironmentalIncidentId.Should().Be(incidentId);
        created.IsIncident.Should().BeTrue();
        // Origin RIÊNG cho môi trường, không dùng ké `System` (cascade risk + bảo trì cũng dùng
        // giá trị đó) — để phân loại nguồn đọc thẳng một field thay vì suy từ field phụ.
        created.Origin.Should().Be(TicketOriginEnum.AutoFromEnvironment);
        // Và KHÔNG đeo mác "[Auto]": đó là khuôn tiêu đề của ticket bất thường một viên pin.
        created.Title.Should().NotContain("[Auto]");
        created.Title.Should().StartWith("Environmental incident at");
        timer.Should().NotBeNull();
        timer!.Status.Should().Be(SlaTimerStatusEnum.Running);
        timer.DueAt.Should().Be(timer.StartedAt.AddHours(4), "P1 Response SLA = 4 giờ liên tục");
        _outboxWriter.Verify(p => p.WriteAsync(It.IsAny<TicketCreatedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Detected_Warning_CreatesP2Ticket()
    {
        Ticket? created = null;
        _ticketRepo.Setup(r => r.AddAsync(It.IsAny<Ticket>())).Callback<Ticket>(t => created = t).Returns(Task.CompletedTask);

        await BuildDetected().Consume(Ctx(Detected(Guid.NewGuid(), severity: 2)));

        created!.Priority.Should().Be(TicketPriorityEnum.P2High);
    }

    [Fact]
    public async Task Detected_DuplicateIncident_Idempotent_NoNewTicket()
    {
        var incidentId = Guid.NewGuid();
        var existing = new Ticket
        {
            Id = Guid.NewGuid(),
            Code = "T-1",
            Title = "t",
            Description = "d",
            EnvironmentalIncidentId = incidentId,
            Status = TicketStatusEnum.Open,
            BatteryAssetId = Guid.Empty,
            CustomerId = Guid.NewGuid()
        };
        _ticketRepo.Setup(r => r.GetAllAsync()).Returns(new List<Ticket> { existing }.AsQueryable().BuildMock());

        await BuildDetected().Consume(Ctx(Detected(incidentId)));

        _ticketRepo.Verify(r => r.AddAsync(It.IsAny<Ticket>()), Times.Never);
    }

    // ── Resolved ──

    [Fact]
    public async Task Resolved_FalseAlarm_ClosesTicket_AndStopsTimer()
    {
        var incidentId = Guid.NewGuid();
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            Code = "T-1",
            Title = "t",
            Description = "d",
            EnvironmentalIncidentId = incidentId,
            Status = TicketStatusEnum.InProgress,
            BatteryAssetId = Guid.Empty,
            CustomerId = Guid.NewGuid()
        };
        var timer = new SlaTimer { Id = Guid.NewGuid(), TicketId = ticket.Id, Status = SlaTimerStatusEnum.Running, StartedAt = DateTime.UtcNow, DueAt = DateTime.UtcNow.AddHours(4) };
        _ticketRepo.Setup(r => r.GetAllAsync()).Returns(new List<Ticket> { ticket }.AsQueryable().BuildMock());
        _slaTimerRepo.Setup(r => r.GetAllAsync()).Returns(new List<SlaTimer> { timer }.AsQueryable().BuildMock());

        var evt = new EnvironmentalIncidentResolvedEvent(incidentId, ticket.CustomerId, DateTime.UtcNow, Guid.NewGuid(), WasFalseAlarm: true, ResolutionNote: "Nhầm");
        await BuildResolved().Consume(Ctx(evt));

        ticket.Status.Should().Be(TicketStatusEnum.ClosedRejected);
        ticket.ClosedAt.Should().NotBeNull();
        // Stopped chứ không phải Met: ticket đóng vì báo động giả nên SLA bị HUỶ, không phải
        // đã đạt. Đánh Met sẽ tính ticket này là "đúng hạn" trong SLA compliance dù chẳng ai
        // xử lý gì — thổi phồng chỉ số. Đây cũng là trạng thái StopSlaAsync dùng cho mọi luồng
        // kết thúc khác (reject/merge) và seeder map cho ClosedRejected.
        timer.Status.Should().Be(SlaTimerStatusEnum.Stopped, "dừng timer để không breach ticket đã đóng");
    }

    [Fact]
    public async Task Resolved_GenuineResolution_NoOp()
    {
        var incidentId = Guid.NewGuid();
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            Code = "T-1",
            Title = "t",
            Description = "d",
            EnvironmentalIncidentId = incidentId,
            Status = TicketStatusEnum.InProgress,
            BatteryAssetId = Guid.Empty,
            CustomerId = Guid.NewGuid()
        };
        _ticketRepo.Setup(r => r.GetAllAsync()).Returns(new List<Ticket> { ticket }.AsQueryable().BuildMock());

        var evt = new EnvironmentalIncidentResolvedEvent(incidentId, ticket.CustomerId, DateTime.UtcNow, null, WasFalseAlarm: false, ResolutionNote: "Đã xử lý");
        await BuildResolved().Consume(Ctx(evt));

        ticket.Status.Should().Be(TicketStatusEnum.InProgress, "resolve thật → Staff đóng theo quy trình");
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
