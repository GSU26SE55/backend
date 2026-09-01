using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using SharedContracts.Events;
using SharedContracts.Saga.AlertTicket;
using TicketService.Infrastructure.Sagas;

namespace TicketService.UnitTests.Sagas;

/// <summary>
/// Sprint 5B #239 — MassTransit TestHarness unit tests cho AlertTicketSagaStateMachine.
/// Test matrix §53.10: ≥ 21 case bao gồm initial transitions, retry, idempotency,
/// rejection paths, reconciliation, terminal tombstone.
///
/// Mỗi test setup fresh harness + in-memory saga repository.
/// </summary>
public class AlertTicketSagaStateMachineTests
{
    private static async Task<(ITestHarness Harness, ISagaStateMachineTestHarness<AlertTicketSagaStateMachine, AlertTicketSagaState> Saga)>
        SetupHarnessAsync()
    {
        var provider = new ServiceCollection()
            .AddMassTransitTestHarness(x =>
            {
                // Flaky guard 2026-07-31: inactivity mặc định của MassTransit v8 = 1s ⇒ Consumed.Any<T>()
                // trả false khi cả solution chạy song song. Khuôn: NotificationService/Helpers/ConsumerTestHarness.cs
                x.SetTestTimeouts(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(15));
                x.AddSagaStateMachine<AlertTicketSagaStateMachine, AlertTicketSagaState>()
                    .InMemoryRepository();
            })
            .AddLogging()
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        var saga = harness.GetSagaStateMachineHarness<AlertTicketSagaStateMachine, AlertTicketSagaState>();
        return (harness, saga);
    }

    private static BatteryAnomalyDetectedEvent MakeV1(Guid alertId) => new(
        AlertId: alertId,
        BatteryAssetId: Guid.NewGuid(),
        CustomerId: Guid.NewGuid(),
        AssetSerialNumber: "BMS-001",
        AnomalyType: 1,
        Severity: 3,
        ThresholdValue: 60m,
        ActualValue: 75m,
        Unit: "C",
        DetectedAt: DateTime.UtcNow);

    private static BatteryAnomalyDetectedV2Event MakeV2(Guid alertId) => new(
        AlertId: alertId,
        BatteryAssetId: Guid.NewGuid(),
        CustomerId: Guid.NewGuid(),
        SiteId: Guid.NewGuid(),
        AssetSerialNumber: "BMS-002",
        AnomalyType: 12,
        Severity: 3,
        ThresholdValue: 40m,
        ActualValue: 55m,
        Unit: "mΩ",
        DetectedAt: DateTime.UtcNow,
        InternalResistanceMilliohm: 55m,
        CellVoltageDeltaMv: null,
        EnvironmentalIncidentId: null);

    private static TicketCreatedFromAlertResponse MakeTicketCreated(Guid correlationId, Guid alertId, bool isReused = false) => new(
        CorrelationId: correlationId, AlertId: alertId, TicketId: Guid.NewGuid(),
        TicketCode: "TCK-001", IsReused: isReused);

    private static TicketCreationFromAlertRejected MakeTicketRejected(Guid correlationId, Guid alertId, string reason = "ASSET_NOT_FOUND") => new(
        CorrelationId: correlationId, AlertId: alertId, Reason: reason, ErrorCode: reason);

    private static AlertLinkedToTicketResponse MakeAlertLinked(Guid correlationId, Guid alertId, Guid ticketId) => new(
        CorrelationId: correlationId, AlertId: alertId, TicketId: ticketId, LinkedAt: DateTime.UtcNow);

    private static AlertLinkToTicketRejected MakeAlertLinkRejected(Guid correlationId, Guid alertId, Guid ticketId) => new(
        CorrelationId: correlationId, AlertId: alertId, TicketId: ticketId,
        Reason: "ALERT_ALREADY_LINKED", ErrorCode: "ALREADY_LINKED");

    // ===== 1-3: Initial transitions =====

    /// <summary>
    /// ĐẢO NGƯỢC 2026-07-31 — trước đây case này khẳng định "V1 khởi tạo saga", nay hợp đồng ngược lại:
    /// <b>V1 một mình KHÔNG được khởi tạo saga</b>.
    ///
    /// Lý do (xem comment <c>Initially</c> trong <see cref="AlertTicketSagaStateMachine"/>):
    /// BatteryService publish CẢ V1 LẪN V2 cho cùng một alert, và cả hai đều <c>SelectId(AlertId)</c>
    /// ⇒ cùng correlation id. Nếu cả hai cùng nằm ở <c>Initially</c> thì 2 message tranh nhau tạo
    /// instance ⇒ <c>23505 PK_alert_ticket_saga_states</c>, nhánh thua nhận AlertLinked sai state
    /// ⇒ <c>UnhandledEventException</c>. Nay chỉ V2 (nhiều field hơn) được khởi tạo; V1 chỉ còn ở
    /// <c>DuringAny</c> để đếm dedup.
    /// </summary>
    [Fact]
    public async Task Case1_V1AnomalyAlone_ShouldNotStartSaga()
    {
        var (harness, saga) = await SetupHarnessAsync();
        var alertId = Guid.NewGuid();

        await harness.Bus.Publish(MakeV1(alertId));
        await Task.Delay(300);

        saga.Created.Select(x => true).Any().Should()
            .BeFalse("Initially chỉ nhận V2 — V1 chỉ được DuringAny bắt để dedup");
        await harness.Stop();
    }

    [Fact]
    public async Task Case2_V2Anomaly_ShouldStartSaga_AndHydrateTier2Fields()
    {
        var (harness, saga) = await SetupHarnessAsync();
        var alertId = Guid.NewGuid();

        await harness.Bus.Publish(MakeV2(alertId));

        var found = await saga.Exists(alertId, x => x.TicketRequested);
        found.Should().NotBeNull();

        var state = saga.Created.Contains(alertId);
        state!.InternalResistanceMilliohm.Should().Be(55m);
        await harness.Stop();
    }

    /// <summary>
    /// LowSoc notification-only — pin dùng hết KHÔNG được sinh ticket.
    ///
    /// Khẳng định thật nằm ở <c>CreateTicketFromAlertCommand</c>: đó là message duy nhất dẫn tới
    /// ticket. Saga có được tạo tạm rồi <c>Finalize()</c> hay không là chi tiết cài đặt —
    /// MassTransit tạo instance trước khi xét mọi điều kiện của event initiating.
    /// </summary>
    [Fact]
    public async Task Case2b_V2Anomaly_LowSoc_ShouldNotRequestTicket()
    {
        var (harness, saga) = await SetupHarnessAsync();
        var alertId = Guid.NewGuid();

        await harness.Bus.Publish(MakeV2(alertId) with { AnomalyType = 4 }); // LowSoc
        await Task.Delay(500);

        (await harness.Published.Any<CreateTicketFromAlertCommand>()).Should()
            .BeFalse("LowSoc là notification-only — không được xin tạo ticket");
        saga.Created.Contains(alertId)?.CurrentState.Should()
            .NotBe(nameof(AlertTicketSagaStateMachine.TicketRequested),
                "saga phải kết thúc ngay, không được nằm chờ ticket");
        await harness.Stop();
    }

    /// <summary>Đường hiện có không hỏng: anomaly thường vẫn xin tạo ticket.</summary>
    [Fact]
    public async Task Case2c_V2Anomaly_NonNotificationOnly_ShouldRequestTicket()
    {
        var (harness, _) = await SetupHarnessAsync();

        await harness.Bus.Publish(MakeV2(Guid.NewGuid())); // AnomalyType 12 — HighInternalResistance

        (await harness.Published.Any<CreateTicketFromAlertCommand>()).Should().BeTrue();
        await harness.Stop();
    }

    [Fact]
    public async Task Case3_Reconciliation_ShouldStartInAlertLinkRequested()
    {
        var (harness, saga) = await SetupHarnessAsync();
        var alertId = Guid.NewGuid();

        await harness.Bus.Publish(new AlertTicketReconciliationCommand(
            CorrelationId: alertId, AlertId: alertId, TicketId: Guid.NewGuid(),
            BatteryAssetId: Guid.NewGuid(), CustomerId: Guid.NewGuid(),
            AssetSerialNumber: "S-X", TicketCode: "TCK-X",
            AnomalyCategory: "Overheat", DetectedAt: DateTime.UtcNow));

        var found = await saga.Exists(alertId, x => x.AlertLinkRequested);
        found.Should().NotBeNull();
        await harness.Stop();
    }

    // ===== 4-7: Happy path =====

    [Fact]
    public async Task Case4_TicketCreated_ShouldTransitionToAlertLinkRequested()
    {
        var (harness, saga) = await SetupHarnessAsync();
        var alertId = Guid.NewGuid();

        await harness.Bus.Publish(MakeV2(alertId));
        await saga.Exists(alertId, x => x.TicketRequested);

        await harness.Bus.Publish(MakeTicketCreated(alertId, alertId));

        var found = await saga.Exists(alertId, x => x.AlertLinkRequested);
        found.Should().NotBeNull();
        await harness.Stop();
    }

    [Fact]
    public async Task Case5_AlertLinked_ShouldTransitionToCompleted()
    {
        var (harness, saga) = await SetupHarnessAsync();
        var alertId = Guid.NewGuid();

        await harness.Bus.Publish(MakeV2(alertId));
        await saga.Exists(alertId, x => x.TicketRequested);

        var ticketCreated = MakeTicketCreated(alertId, alertId);
        await harness.Bus.Publish(ticketCreated);
        await saga.Exists(alertId, x => x.AlertLinkRequested);

        await harness.Bus.Publish(MakeAlertLinked(alertId, alertId, ticketCreated.TicketId));

        var found = await saga.Exists(alertId, x => x.Completed);
        found.Should().NotBeNull();
        await harness.Stop();
    }

    [Fact]
    public async Task Case6_TicketReused_ShouldSetTicketIsReused()
    {
        var (harness, saga) = await SetupHarnessAsync();
        var alertId = Guid.NewGuid();

        await harness.Bus.Publish(MakeV2(alertId));
        await saga.Exists(alertId, x => x.TicketRequested);

        await harness.Bus.Publish(MakeTicketCreated(alertId, alertId, isReused: true));
        await saga.Exists(alertId, x => x.AlertLinkRequested);

        var state = saga.Created.Contains(alertId);
        state!.TicketIsReused.Should().BeTrue();
        await harness.Stop();
    }

    [Fact]
    public async Task Case7_Completed_ShouldRecordCompletedAt()
    {
        var (harness, saga) = await SetupHarnessAsync();
        var alertId = Guid.NewGuid();

        await harness.Bus.Publish(MakeV2(alertId));
        await saga.Exists(alertId, x => x.TicketRequested);

        var ticketCreated = MakeTicketCreated(alertId, alertId);
        await harness.Bus.Publish(ticketCreated);
        await saga.Exists(alertId, x => x.AlertLinkRequested);

        await harness.Bus.Publish(MakeAlertLinked(alertId, alertId, ticketCreated.TicketId));
        await saga.Exists(alertId, x => x.Completed);

        var state = saga.Created.Contains(alertId);
        state!.CompletedAt.Should().NotBeNull();
        await harness.Stop();
    }

    // ===== 8-10: Rejection paths =====

    [Fact]
    public async Task Case8_TicketRejected_ShouldTransitionToFailed()
    {
        var (harness, saga) = await SetupHarnessAsync();
        var alertId = Guid.NewGuid();

        await harness.Bus.Publish(MakeV2(alertId));
        await saga.Exists(alertId, x => x.TicketRequested);

        await harness.Bus.Publish(MakeTicketRejected(alertId, alertId));

        var found = await saga.Exists(alertId, x => x.Failed);
        found.Should().NotBeNull();
        await harness.Stop();
    }

    [Fact]
    public async Task Case9_TicketRejected_ShouldRecordFailureReason()
    {
        var (harness, saga) = await SetupHarnessAsync();
        var alertId = Guid.NewGuid();

        await harness.Bus.Publish(MakeV2(alertId));
        await saga.Exists(alertId, x => x.TicketRequested);

        await harness.Bus.Publish(MakeTicketRejected(alertId, alertId, reason: "CUSTOMER_INACTIVE"));
        await saga.Exists(alertId, x => x.Failed);

        var state = saga.Created.Contains(alertId);
        state!.FailureReason.Should().Be("CUSTOMER_INACTIVE");
        state.FailedAtStage.Should().Be("TicketRequested");
        await harness.Stop();
    }

    [Fact]
    public async Task Case10_AlertLinkRejected_ShouldTransitionToFailed()
    {
        var (harness, saga) = await SetupHarnessAsync();
        var alertId = Guid.NewGuid();

        await harness.Bus.Publish(MakeV2(alertId));
        await saga.Exists(alertId, x => x.TicketRequested);

        var ticketCreated = MakeTicketCreated(alertId, alertId);
        await harness.Bus.Publish(ticketCreated);
        await saga.Exists(alertId, x => x.AlertLinkRequested);

        await harness.Bus.Publish(MakeAlertLinkRejected(alertId, alertId, ticketCreated.TicketId));

        var found = await saga.Exists(alertId, x => x.Failed);
        found.Should().NotBeNull();

        var state = saga.Created.Contains(alertId);
        state!.FailedAtStage.Should().Be("AlertLinkRequested");
        await harness.Stop();
    }

    // ===== 11-13: Idempotency / Redelivery =====

    /// <summary>
    /// SỬA 2026-07-31 — mô phỏng đúng hành vi production: BatteryService bắn **cả V2 lẫn V1** cho
    /// cùng một alert. V2 khởi tạo saga; V1 tới sau chỉ được dedup, KHÔNG được đẻ instance thứ hai.
    /// </summary>
    [Fact]
    public async Task Case11_V1ArrivingAfterV2_ShouldNotCreateSecondSaga()
    {
        var (harness, saga) = await SetupHarnessAsync();
        var alertId = Guid.NewGuid();

        await harness.Bus.Publish(MakeV2(alertId));
        await saga.Exists(alertId, x => x.TicketRequested);

        await harness.Bus.Publish(MakeV1(alertId));
        await harness.Bus.Publish(MakeV2(alertId));
        await Task.Delay(200);

        saga.Created.Select(x => true).Count().Should().Be(1);
        await harness.Stop();
    }

    [Fact]
    public async Task Case12_RedeliveryInCompletedState_ShouldRemainCompleted()
    {
        var (harness, saga) = await SetupHarnessAsync();
        var alertId = Guid.NewGuid();

        await harness.Bus.Publish(MakeV2(alertId));
        await saga.Exists(alertId, x => x.TicketRequested);

        var ticketCreated = MakeTicketCreated(alertId, alertId);
        await harness.Bus.Publish(ticketCreated);
        await saga.Exists(alertId, x => x.AlertLinkRequested);
        await harness.Bus.Publish(MakeAlertLinked(alertId, alertId, ticketCreated.TicketId));
        await saga.Exists(alertId, x => x.Completed);

        // Redelivery V1 ở state tombstone — DuringAny chỉ đếm dedup, không đổi state.
        await harness.Bus.Publish(MakeV1(alertId));
        await Task.Delay(200);

        var state = saga.Created.Contains(alertId);
        state!.CurrentState.Should().Be(nameof(AlertTicketSagaStateMachine.Completed));
        await harness.Stop();
    }

    [Fact]
    public async Task Case13_RedeliveryInFailedState_ShouldRemainFailed()
    {
        var (harness, saga) = await SetupHarnessAsync();
        var alertId = Guid.NewGuid();

        await harness.Bus.Publish(MakeV2(alertId));
        await saga.Exists(alertId, x => x.TicketRequested);
        await harness.Bus.Publish(MakeTicketRejected(alertId, alertId));
        await saga.Exists(alertId, x => x.Failed);

        // Redelivery V1 ở state Failed — phải giữ nguyên Failed.
        await harness.Bus.Publish(MakeV1(alertId));
        await Task.Delay(200);

        var state = saga.Created.Contains(alertId);
        state!.CurrentState.Should().Be(nameof(AlertTicketSagaStateMachine.Failed));
        await harness.Stop();
    }

    // ===== 14-16: State persistence =====

    /// <summary>
    /// ĐỔI Ý ĐỒ 2026-07-31 — trước đây case này kiểm "snapshot persist từ V1", nhưng
    /// <c>HydrateFromV1</c> nay là code chết (saga chỉ khởi tạo từ V2). Thứ đáng bảo vệ là:
    /// V1 tới sau KHÔNG được ghi đè snapshot mà V2 đã ghi — V1 ít field hơn (không có SiteId /
    /// Tier-2), ghi đè sẽ làm mất dữ liệu. Snapshot từ V2 đã được Case15 phủ.
    /// </summary>
    [Fact]
    public async Task Case14_V1ArrivingAfterV2_ShouldNotOverwriteSnapshot()
    {
        var (harness, saga) = await SetupHarnessAsync();
        var alertId = Guid.NewGuid();
        var v2 = MakeV2(alertId);

        await harness.Bus.Publish(v2);
        await saga.Exists(alertId, x => x.TicketRequested);

        // V1 cùng AlertId nhưng số liệu khác hẳn — nếu bị hydrate lại thì assert dưới sẽ gãy.
        await harness.Bus.Publish(new BatteryAnomalyDetectedEvent(
            AlertId: alertId,
            BatteryAssetId: Guid.NewGuid(),
            CustomerId: Guid.NewGuid(),
            AssetSerialNumber: "BMS-OVERWRITE",
            AnomalyType: 99,
            Severity: 1,
            ThresholdValue: 1m,
            ActualValue: 2m,
            Unit: "X",
            DetectedAt: DateTime.UtcNow));
        await Task.Delay(200);

        var state = saga.Created.Contains(alertId);
        state!.AnomalyType.Should().Be(v2.AnomalyType);
        state.Severity.Should().Be(v2.Severity);
        state.ThresholdValue.Should().Be(v2.ThresholdValue);
        state.ActualValue.Should().Be(v2.ActualValue);
        state.CustomerId.Should().Be(v2.CustomerId);
        state.SiteId.Should().Be(v2.SiteId);
        await harness.Stop();
    }

    [Fact]
    public async Task Case15_PayloadSnapshot_ShouldBePersistedFromV2()
    {
        var (harness, saga) = await SetupHarnessAsync();
        var alertId = Guid.NewGuid();
        var evt = MakeV2(alertId);

        await harness.Bus.Publish(evt);
        await saga.Exists(alertId, x => x.TicketRequested);

        var state = saga.Created.Contains(alertId);
        state!.SiteId.Should().Be(evt.SiteId);
        state.InternalResistanceMilliohm.Should().Be(evt.InternalResistanceMilliohm);
        await harness.Stop();
    }

    [Fact]
    public async Task Case16_StartedAt_ShouldBeRecorded()
    {
        var (harness, saga) = await SetupHarnessAsync();
        var alertId = Guid.NewGuid();
        var before = DateTime.UtcNow.AddSeconds(-1);

        await harness.Bus.Publish(MakeV2(alertId));
        await saga.Exists(alertId, x => x.TicketRequested);

        var state = saga.Created.Contains(alertId);
        state!.StartedAt.Should().BeOnOrAfter(before);
        await harness.Stop();
    }

    // ===== 17-18: Correlation =====

    [Fact]
    public async Task Case17_TicketCreated_OnUnknownAlertId_ShouldNotCreateSaga()
    {
        var (harness, saga) = await SetupHarnessAsync();
        var randomCorrelation = Guid.NewGuid();
        var randomAlert = Guid.NewGuid();

        await harness.Bus.Publish(MakeTicketCreated(randomCorrelation, randomAlert));

        await Task.Delay(100);

        var any = saga.Created.Select(x => true).Any();
        any.Should().BeFalse();
        await harness.Stop();
    }

    [Fact]
    public async Task Case18_TwoIndependentAlerts_ShouldHaveSeparateSagas()
    {
        var (harness, saga) = await SetupHarnessAsync();
        var alert1 = Guid.NewGuid();
        var alert2 = Guid.NewGuid();

        await harness.Bus.Publish(MakeV2(alert1));
        await harness.Bus.Publish(MakeV2(alert2));

        await saga.Exists(alert1, x => x.TicketRequested);
        await saga.Exists(alert2, x => x.TicketRequested);

        var count = saga.Created.Select(x => true).Count();
        count.Should().Be(2);
        await harness.Stop();
    }

    // ===== 19-21: Edge cases =====

    [Fact]
    public async Task Case19_AlertLinkedAfterTicketRejected_ShouldNotChangeState()
    {
        var (harness, saga) = await SetupHarnessAsync();
        var alertId = Guid.NewGuid();

        await harness.Bus.Publish(MakeV2(alertId));
        await saga.Exists(alertId, x => x.TicketRequested);
        await harness.Bus.Publish(MakeTicketRejected(alertId, alertId));
        await saga.Exists(alertId, x => x.Failed);

        await harness.Bus.Publish(MakeAlertLinked(alertId, alertId, Guid.NewGuid()));

        var state = saga.Created.Contains(alertId);
        state!.CurrentState.Should().Be(nameof(AlertTicketSagaStateMachine.Failed));
        await harness.Stop();
    }

    [Fact]
    public async Task Case20_TicketCreatedTwice_ShouldNotDoubleTransition()
    {
        var (harness, saga) = await SetupHarnessAsync();
        var alertId = Guid.NewGuid();

        await harness.Bus.Publish(MakeV2(alertId));
        await saga.Exists(alertId, x => x.TicketRequested);

        var ticketCreated = MakeTicketCreated(alertId, alertId);
        await harness.Bus.Publish(ticketCreated);
        await saga.Exists(alertId, x => x.AlertLinkRequested);

        // Redelivery — saga đã rời TicketRequested, second event không có handler ở AlertLinkRequested.
        await harness.Bus.Publish(ticketCreated);
        await Task.Delay(100);

        var state = saga.Created.Contains(alertId);
        state!.CurrentState.Should().Be(nameof(AlertTicketSagaStateMachine.AlertLinkRequested));
        await harness.Stop();
    }

    [Fact]
    public async Task Case21_FailedSaga_ShouldRecordFailedAt()
    {
        var (harness, saga) = await SetupHarnessAsync();
        var alertId = Guid.NewGuid();
        var before = DateTime.UtcNow.AddSeconds(-1);

        await harness.Bus.Publish(MakeV2(alertId));
        await saga.Exists(alertId, x => x.TicketRequested);
        await harness.Bus.Publish(MakeTicketRejected(alertId, alertId));
        await saga.Exists(alertId, x => x.Failed);

        var state = saga.Created.Contains(alertId);
        state!.FailedAt.Should().NotBeNull();
        state.FailedAt.Should().BeOnOrAfter(before);
        await harness.Stop();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Sprint IoT-2 — DoD: "Saga path verify: trigger 1 anomaly Critical → Saga
    // `TicketProvisioned → Completed`; bơm cùng anomaly 2 lần (idempotent) → 1 Ticket duy nhất."
    //
    // Các Case 1–21 ở trên kiểm TỪNG chuyển trạng thái riêng lẻ. Hai test dưới đi HẾT vòng trong
    // một bài, đúng như câu chữ DoD — để khi đọc báo cáo không phải ghép 5 test lại mới thấy luồng.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DoD_IoT2_CriticalAnomaly_WalksFullPath_ToCompleted()
    {
        var (harness, saga) = await SetupHarnessAsync();
        var alertId = Guid.NewGuid();

        // Severity 3 = Critical.
        await harness.Bus.Publish(MakeV2(alertId));
        (await saga.Exists(alertId, x => x.TicketRequested)).Should().NotBeNull(
            "anomaly Critical phải khởi tạo saga và yêu cầu tạo ticket");

        var created = MakeTicketCreated(alertId, alertId);
        await harness.Bus.Publish(created);
        (await saga.Exists(alertId, x => x.AlertLinkRequested)).Should().NotBeNull(
            "ticket tạo xong -> đi qua TicketProvisioned rồi sang AlertLinkRequested");

        await harness.Bus.Publish(MakeAlertLinked(alertId, alertId, created.TicketId));
        (await saga.Exists(alertId, x => x.Completed)).Should().NotBeNull(
            "alert đã gắn vào ticket -> saga Completed");

        var state = saga.Created.Contains(alertId);
        state!.CurrentState.Should().Be(nameof(AlertTicketSagaStateMachine.Completed));
        state.TicketId.Should().Be(created.TicketId);
        state.CompletedAt.Should().NotBeNull();

        await harness.Stop();
    }

    [Fact]
    public async Task DoD_IoT2_SameAnomalyTwice_IsIdempotent_OneTicketOnly()
    {
        var (harness, saga) = await SetupHarnessAsync();
        var alertId = Guid.NewGuid();

        // Lần 1 — tạo saga + yêu cầu tạo ticket.
        await harness.Bus.Publish(MakeV2(alertId));
        (await saga.Exists(alertId, x => x.TicketRequested)).Should().NotBeNull();

        // Lần 2 — CÙNG alert (redelivery của broker, hoặc BatteryService bắn lại).
        await harness.Bus.Publish(MakeV2(alertId));
        await Task.Delay(300);

        saga.Created.Select(x => true).Count().Should().Be(1,
            "cùng AlertId phải gom về 1 saga — nếu đẻ 2 saga thì Customer nhận 2 ticket cho cùng 1 sự cố");

        var created = MakeTicketCreated(alertId, alertId);
        await harness.Bus.Publish(created);
        (await saga.Exists(alertId, x => x.AlertLinkRequested)).Should().NotBeNull();

        // Redelivery của chính response tạo ticket cũng không được đẩy state đi tiếp.
        await harness.Bus.Publish(created);
        await Task.Delay(300);

        var state = saga.Created.Contains(alertId);
        state!.CurrentState.Should().Be(nameof(AlertTicketSagaStateMachine.AlertLinkRequested));
        state.TicketId.Should().Be(created.TicketId, "vẫn đúng 1 TicketId duy nhất");

        await harness.Stop();
    }
}
