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

    [Fact]
    public async Task Case1_V1Anomaly_ShouldStartSaga_AndTransitionToTicketRequested()
    {
        var (harness, saga) = await SetupHarnessAsync();
        var alertId = Guid.NewGuid();

        await harness.Bus.Publish(MakeV1(alertId));

        var found = await saga.Exists(alertId, x => x.TicketRequested);
        found.Should().NotBeNull();
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

        await harness.Bus.Publish(MakeV1(alertId));
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

        await harness.Bus.Publish(MakeV1(alertId));
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

        await harness.Bus.Publish(MakeV1(alertId));
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

        await harness.Bus.Publish(MakeV1(alertId));
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

        await harness.Bus.Publish(MakeV1(alertId));
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

        await harness.Bus.Publish(MakeV1(alertId));
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

        await harness.Bus.Publish(MakeV1(alertId));
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

    [Fact]
    public async Task Case11_DuplicateV1Anomaly_ShouldNotCreateSecondSaga()
    {
        var (harness, saga) = await SetupHarnessAsync();
        var alertId = Guid.NewGuid();

        await harness.Bus.Publish(MakeV1(alertId));
        await saga.Exists(alertId, x => x.TicketRequested);

        await harness.Bus.Publish(MakeV1(alertId));

        var instances = saga.Created.Select(s => s.AlertId == alertId).Count();
        instances.Should().Be(1);
        await harness.Stop();
    }

    [Fact]
    public async Task Case12_RedeliveryInCompletedState_ShouldRemainCompleted()
    {
        var (harness, saga) = await SetupHarnessAsync();
        var alertId = Guid.NewGuid();

        await harness.Bus.Publish(MakeV1(alertId));
        await saga.Exists(alertId, x => x.TicketRequested);

        var ticketCreated = MakeTicketCreated(alertId, alertId);
        await harness.Bus.Publish(ticketCreated);
        await saga.Exists(alertId, x => x.AlertLinkRequested);
        await harness.Bus.Publish(MakeAlertLinked(alertId, alertId, ticketCreated.TicketId));
        await saga.Exists(alertId, x => x.Completed);

        await harness.Bus.Publish(MakeV1(alertId));

        var state = saga.Created.Contains(alertId);
        state!.CurrentState.Should().Be(nameof(AlertTicketSagaStateMachine.Completed));
        await harness.Stop();
    }

    [Fact]
    public async Task Case13_RedeliveryInFailedState_ShouldRemainFailed()
    {
        var (harness, saga) = await SetupHarnessAsync();
        var alertId = Guid.NewGuid();

        await harness.Bus.Publish(MakeV1(alertId));
        await saga.Exists(alertId, x => x.TicketRequested);
        await harness.Bus.Publish(MakeTicketRejected(alertId, alertId));
        await saga.Exists(alertId, x => x.Failed);

        await harness.Bus.Publish(MakeV2(alertId));

        var state = saga.Created.Contains(alertId);
        state!.CurrentState.Should().Be(nameof(AlertTicketSagaStateMachine.Failed));
        await harness.Stop();
    }

    // ===== 14-16: State persistence =====

    [Fact]
    public async Task Case14_PayloadSnapshot_ShouldBePersistedFromV1()
    {
        var (harness, saga) = await SetupHarnessAsync();
        var alertId = Guid.NewGuid();
        var evt = MakeV1(alertId);

        await harness.Bus.Publish(evt);
        await saga.Exists(alertId, x => x.TicketRequested);

        var state = saga.Created.Contains(alertId);
        state!.AnomalyType.Should().Be(evt.AnomalyType);
        state.Severity.Should().Be(evt.Severity);
        state.ThresholdValue.Should().Be(evt.ThresholdValue);
        state.ActualValue.Should().Be(evt.ActualValue);
        state.CustomerId.Should().Be(evt.CustomerId);
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

        await harness.Bus.Publish(MakeV1(alertId));
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

        await harness.Bus.Publish(MakeV1(alert1));
        await harness.Bus.Publish(MakeV1(alert2));

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

        await harness.Bus.Publish(MakeV1(alertId));
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

        await harness.Bus.Publish(MakeV1(alertId));
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

        await harness.Bus.Publish(MakeV1(alertId));
        await saga.Exists(alertId, x => x.TicketRequested);
        await harness.Bus.Publish(MakeTicketRejected(alertId, alertId));
        await saga.Exists(alertId, x => x.Failed);

        var state = saga.Created.Contains(alertId);
        state!.FailedAt.Should().NotBeNull();
        state.FailedAt.Should().BeOnOrAfter(before);
        await harness.Stop();
    }
}
