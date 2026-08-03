using FluentAssertions;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using SharedContracts.Events;
using SharedContracts.Saga.AlertTicket;
using TicketService.Infrastructure.Persistence;
using TicketService.Infrastructure.Sagas;

namespace TicketService.UnitTests.Sagas;

/// <summary>
/// Sprint 5B #239 — Restart-recovery integration test.
///
/// Verify Saga state persistence + recovery:
/// 1. Saga vào TicketRequested, persist trong DB.
/// 2. Dispose harness (= "kill" TicketService).
/// 3. Re-create harness với cùng DB.
/// 4. Publish TicketCreated response → Saga tiếp tục từ TicketRequested → AlertLinkRequested → Completed.
/// </summary>
public class SagaRestartRecoveryTests
{
    private static (IServiceProvider Provider, string DbName) BuildProvider(string? sharedDbName = null)
    {
        var dbName = sharedDbName ?? $"saga-rr-{Guid.NewGuid()}";

        var provider = new ServiceCollection()
            .AddDbContext<TicketDbContext>(opt =>
                opt.UseInMemoryDatabase(dbName)
                   .ConfigureWarnings(w =>
                       w.Ignore(InMemoryEventId.TransactionIgnoredWarning)))
            .AddSingleton<SharedInfrastructure.Persistence.Interceptors.AuditableEntityInterceptor>(_ =>
                new SharedInfrastructure.Persistence.Interceptors.AuditableEntityInterceptor(
                    new SharedInfrastructure.Services.CurrentUserService(new Microsoft.AspNetCore.Http.HttpContextAccessor())))
            .AddMassTransitTestHarness(x =>
            {
                // Flaky guard 2026-07-31: inactivity mặc định của MassTransit v8 = 1s ⇒ Consumed.Any<T>()
                // trả false khi cả solution chạy song song. Khuôn: NotificationService/Helpers/ConsumerTestHarness.cs
                x.SetTestTimeouts(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(15));
                x.AddSagaStateMachine<AlertTicketSagaStateMachine, AlertTicketSagaState>()
                    .EntityFrameworkRepository(r =>
                    {
                        r.ConcurrencyMode = ConcurrencyMode.Optimistic;
                        r.ExistingDbContext<TicketDbContext>();
                    });
            })
            .AddLogging()
            .BuildServiceProvider(true);

        return (provider, dbName);
    }

    [Fact]
    public async Task Saga_PersistsState_AfterHarnessDispose_RecoversOnRestart()
    {
        var (firstProvider, dbName) = BuildProvider();
        var alertId = Guid.NewGuid();

        await using (firstProvider as IAsyncDisposable ?? (IAsyncDisposable)default!)
        {
            var harness1 = firstProvider.GetRequiredService<ITestHarness>();
            await harness1.Start();

            // Sửa 2026-07-31: dùng V2 — `Initially` của saga chỉ nhận `BatteryAnomalyDetectedV2Event`
            // (V1 bị bỏ khỏi Initially để tránh 2 message cùng AlertId tranh nhau tạo instance,
            // xem comment trong AlertTicketSagaStateMachine). V1 nay chỉ được DuringAny dedup.
            await harness1.Bus.Publish(new BatteryAnomalyDetectedV2Event(
                AlertId: alertId,
                BatteryAssetId: Guid.NewGuid(),
                CustomerId: Guid.NewGuid(),
                SiteId: Guid.NewGuid(),
                AssetSerialNumber: "BMS-RR-1",
                AnomalyType: 1, Severity: 3,
                ThresholdValue: 60m, ActualValue: 75m, Unit: "C",
                DetectedAt: DateTime.UtcNow,
                InternalResistanceMilliohm: null,
                CellVoltageDeltaMv: null,
                EnvironmentalIncidentId: null));

            var saga1 = harness1.GetSagaStateMachineHarness<AlertTicketSagaStateMachine, AlertTicketSagaState>();
            (await saga1.Exists(alertId, x => x.TicketRequested)).Should().NotBeNull();
            await harness1.Stop();
        }

        // Verify state persisted in DB.
        using (var verifyScope = BuildProvider(dbName).Provider.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<TicketDbContext>();
            var persisted = await db.AlertTicketSagaStates.FindAsync(alertId);
            persisted.Should().NotBeNull();
            persisted!.CurrentState.Should().Be(nameof(AlertTicketSagaStateMachine.TicketRequested));
            persisted.AlertId.Should().Be(alertId);
        }

        // Restart with same DB — saga should pick up where it left off.
        var (secondProvider, _) = BuildProvider(dbName);
        var harness2 = secondProvider.GetRequiredService<ITestHarness>();
        await harness2.Start();

        var ticketId = Guid.NewGuid();
        await harness2.Bus.Publish(new TicketCreatedFromAlertResponse(
            CorrelationId: alertId,
            AlertId: alertId,
            TicketId: ticketId,
            TicketCode: "TCK-RR-1",
            IsReused: false));

        var saga2 = harness2.GetSagaStateMachineHarness<AlertTicketSagaStateMachine, AlertTicketSagaState>();
        (await saga2.Exists(alertId, x => x.AlertLinkRequested)).Should().NotBeNull();

        await harness2.Bus.Publish(new AlertLinkedToTicketResponse(
            CorrelationId: alertId,
            AlertId: alertId,
            TicketId: ticketId,
            LinkedAt: DateTime.UtcNow));

        (await saga2.Exists(alertId, x => x.Completed)).Should().NotBeNull();

        // Final DB verify.
        using (var verifyScope = secondProvider.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<TicketDbContext>();
            var finalState = await db.AlertTicketSagaStates.FindAsync(alertId);
            finalState!.CurrentState.Should().Be(nameof(AlertTicketSagaStateMachine.Completed));
            finalState.TicketId.Should().Be(ticketId);
            finalState.CompletedAt.Should().NotBeNull();
        }

        await harness2.Stop();
    }

    [Fact]
    public async Task FailedSaga_PersistsState_AfterRestart_RemainsFailed()
    {
        var (provider1, dbName) = BuildProvider();
        var alertId = Guid.NewGuid();

        await using (provider1 as IAsyncDisposable ?? (IAsyncDisposable)default!)
        {
            var h1 = provider1.GetRequiredService<ITestHarness>();
            await h1.Start();

            // Sửa 2026-07-31: V2 mới khởi tạo được saga (xem chú thích ở test trên).
            await h1.Bus.Publish(new BatteryAnomalyDetectedV2Event(
                AlertId: alertId, BatteryAssetId: Guid.NewGuid(), CustomerId: Guid.NewGuid(),
                SiteId: Guid.NewGuid(),
                AssetSerialNumber: "BMS-F", AnomalyType: 1, Severity: 3,
                ThresholdValue: 60m, ActualValue: 75m, Unit: "C",
                DetectedAt: DateTime.UtcNow,
                InternalResistanceMilliohm: null,
                CellVoltageDeltaMv: null,
                EnvironmentalIncidentId: null));

            var saga1 = h1.GetSagaStateMachineHarness<AlertTicketSagaStateMachine, AlertTicketSagaState>();
            await saga1.Exists(alertId, x => x.TicketRequested);

            await h1.Bus.Publish(new TicketCreationFromAlertRejected(
                CorrelationId: alertId, AlertId: alertId,
                Reason: "ASSET_NOT_FOUND", ErrorCode: "ASSET_NOT_FOUND"));

            await saga1.Exists(alertId, x => x.Failed);
            await h1.Stop();
        }

        // Restart — Failed remains Failed.
        var (provider2, _) = BuildProvider(dbName);
        var h2 = provider2.GetRequiredService<ITestHarness>();
        await h2.Start();

        using (var verifyScope = provider2.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<TicketDbContext>();
            var state = await db.AlertTicketSagaStates.FindAsync(alertId);
            state!.CurrentState.Should().Be(nameof(AlertTicketSagaStateMachine.Failed));
            state.FailureReason.Should().Contain("ASSET_NOT_FOUND");
            state.FailedAt.Should().NotBeNull();
        }

        await h2.Stop();
    }
}
