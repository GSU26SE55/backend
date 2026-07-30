using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using TicketService.Infrastructure.Persistence;
using TicketService.Infrastructure.Sagas.ChatEscalationReview;

namespace TicketService.Infrastructure.Sagas;

/// <summary>
/// Sprint 5B #237 — register Alert–Ticket Saga state machine với MassTransit:
/// EF repository (PostgreSQL, optimistic concurrency qua <c>xmin</c>) + Quartz persistent scheduler.
///
/// Wire vào lúc <see cref="SharedInfrastructure.Bus.MassTransitExtensions.AddMessageBus"/> được gọi
/// bằng cách truyền action <c>configure</c>.
/// </summary>
public static class SagaServiceCollectionExtensions
{
    /// <summary>
    /// Register Saga state machine + EF repository + Quartz scheduler.
    /// </summary>
    public static IServiceCollection AddAlertTicketSaga(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Quartz scheduler — cluster mode dùng instanceId=AUTO + checkin 10s
        // (xem overall.md §8.3.11bis).
        services.AddQuartz(q =>
        {
            q.SchedulerId = "AUTO";
            q.SchedulerName = "AlertTicketSagaScheduler";

            q.UsePersistentStore(s =>
            {
                s.UseProperties = true;
                s.UseClustering(c =>
                {
                    c.CheckinInterval = TimeSpan.FromSeconds(10);
                });
                s.UsePostgres(p =>
                {
                    p.ConnectionString = configuration.GetConnectionString("TicketDb")
                        ?? configuration["TicketDb"]
                        ?? throw new InvalidOperationException(
                            "TicketDb connection string required for Quartz cluster (Sprint 5B #235).");
                    p.TablePrefix = "qrtz_";
                });
                s.UseNewtonsoftJsonSerializer();
            });
        });

        services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);

        return services;
    }

    /// <summary>
    /// Configurator cho <c>AddMessageBus(configure: ...)</c>. Add Saga + EF repo + Quartz.
    /// </summary>
    public static void ConfigureAlertTicketSaga(IBusRegistrationConfigurator x)
    {
        x.AddSagaStateMachine<AlertTicketSagaStateMachine, AlertTicketSagaState>()
            .EntityFrameworkRepository(r =>
            {
                r.ConcurrencyMode = ConcurrencyMode.Optimistic;
                r.ExistingDbContext<TicketDbContext>();
                r.UsePostgres();
            });

        // ── Persistent timeout cho saga (xem #237) ──────────────────────────────────
        //
        // Cần ĐỦ BA mảnh, thiếu một là hỏng im lặng:
        //   1. AddQuartz + AddQuartzHostedService  → scheduler Quartz + bảng qrtz_ (ở AddAlertTicketSaga)
        //   2. AddPublishMessageScheduler          → phía DI: saga publish ScheduleMessage<T>
        //   3. AddQuartzConsumers                  → phía tiêu thụ: ai đó phải NHẬN ScheduleMessage<T>
        //      và nạp vào Quartz. Thiếu mảnh này thì lệnh hẹn giờ bay vào hư không.
        //
        // Kèm theo, bus factory phải gọi `cfg.UsePublishMessageScheduler()` để bơm
        // MessageSchedulerContext vào pipe (đã thêm ở SharedInfrastructure/Bus/MassTransitExtensions).
        //
        // **Sửa 30/07/2026:** trước đây thiếu mảnh (3) và lời gọi ở bus factory ⇒ mọi transition có
        // hẹn giờ ném `PayloadNotFoundException: MassTransit.MessageSchedulerContext`, dồn 1662
        // message vào `AlertTicketSagaState_error` và `qrtz_triggers` rỗng suốt.
        x.AddPublishMessageScheduler();
        x.AddQuartzConsumers();
    }

    /// <summary>
    /// Configurator cho <c>AddMessageBus(configureBus: ...)</c> — chạy trong <c>UsingRabbitMq</c>.
    /// Bật publish scheduler để saga dùng được <c>.Schedule(...)</c> / <c>.Unschedule(...)</c>.
    /// </summary>
    public static void ConfigureAlertTicketSagaBus(
        IBusRegistrationContext context,
        IRabbitMqBusFactoryConfigurator cfg)
    {
        // Phải ghép cặp với AddPublishMessageScheduler/AddQuartzConsumers ở trên.
        cfg.UsePublishMessageScheduler();

        // FIX saga-race: retry các va chạm timing tạm thời của saga.
        cfg.UseMessageRetry(r =>
        {
            r.Interval(5, TimeSpan.FromMilliseconds(200));
            r.Handle<DbUpdateConcurrencyException>();
            r.Handle<UnhandledEventException>();
        });
    }

    /// <summary>
    /// Đăng ký ChatEscalationReview saga (#566). Dùng chung Quartz scheduler đã khai ở
    /// <see cref="ConfigureAlertTicketSaga"/>.
    ///
    /// <para>
    /// Cố ý KHÔNG gọi lại <c>AddPublishMessageScheduler</c>/<c>AddQuartzConsumers</c> ở đây: cả hai
    /// saga đăng ký trên **cùng một bus** (xem <c>ManageDependencyInjection</c> — hai lời gọi
    /// Configure liên tiếp trong cùng một <c>AddMessageBus</c>), nên một lần khai là đủ cho cả hai.
    /// Gọi lặp sẽ đẻ thêm endpoint quartz thừa.
    /// </para>
    /// <para>
    /// Saga này cũng dùng <c>.Schedule(EscalationTimer, ...)</c> (chờ Manager ACK 30 phút) ⇒ nó
    /// chịu chung lỗi thiếu scheduler trước ngày 30/07/2026, và được bản sửa đó chữa luôn.
    /// </para>
    /// </summary>
    public static void ConfigureChatEscalationReviewSaga(IBusRegistrationConfigurator x)
    {
        x.AddSagaStateMachine<ChatEscalationReviewSagaStateMachine, ChatEscalationReviewSagaState>()
            .EntityFrameworkRepository(r =>
            {
                r.ConcurrencyMode = ConcurrencyMode.Optimistic;
                r.ExistingDbContext<TicketDbContext>();
                r.UsePostgres();
            });
    }
}
