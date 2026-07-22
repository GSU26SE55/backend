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

        // Quartz scheduler endpoint cho persistent timeout (xem #237).
        //
        // FIX saga-scheduler: cần ĐỦ 3 vế, trước đây chỉ có vế 1 nên saga throw
        // PayloadNotFoundException(MessageSchedulerContext) ngay ở Initially → không saga nào chạy:
        //   1. AddPublishMessageScheduler()  — đăng ký DI (đã có)
        //   2. AddQuartzConsumers()          — tạo receive endpoint "quartz" xử lý scheduled message
        //   3. cfg.UseMessageScheduler(...)  — đưa MessageSchedulerContext vào consume pipeline
        //      (gọi ở ConfigureAlertTicketSagaBus bên dưới)
        x.AddPublishMessageScheduler();
        x.AddQuartzConsumers();
    }

    /// <summary>
    /// Địa chỉ endpoint Quartz scheduler — phải khớp giữa <c>AddQuartzConsumers</c>
    /// và <c>UseMessageScheduler</c>, nếu lệch thì scheduled message rơi vào hư không.
    /// </summary>
    public static readonly Uri QuartzSchedulerAddress = new("queue:quartz");

    /// <summary>
    /// Configurator cho <c>AddMessageBus(configureBus: ...)</c> — chạy trong <c>UsingRabbitMq</c>.
    /// Bật message scheduler để saga dùng được <c>.Schedule(...)</c> / <c>.Unschedule(...)</c>.
    /// </summary>
    public static void ConfigureAlertTicketSagaBus(
        IBusRegistrationContext context,
        IRabbitMqBusFactoryConfigurator cfg)
    {
        cfg.UseMessageScheduler(QuartzSchedulerAddress);

        // FIX saga-race: trước đây KHÔNG có retry policy nào → mọi va chạm timing tạm thời
        // biến thành fault vĩnh viễn trong _error queue. 2 dạng gặp thực tế:
        //   - UnhandledEventException: AlertLinked/TicketCreated về TRƯỚC khi saga kịp
        //     TransitionTo state tương ứng (response nhanh hơn commit state).
        //   - DbUpdateConcurrencyException: 2 message cùng update 1 saga instance
        //     (EF repository dùng ConcurrencyMode.Optimistic).
        // Cả hai đều tự khỏi khi thử lại sau vài chục ms.
        cfg.UseMessageRetry(r =>
        {
            r.Interval(5, TimeSpan.FromMilliseconds(200));
            r.Handle<DbUpdateConcurrencyException>();
            r.Handle<UnhandledEventException>();
        });
    }

    /// <summary>
    /// Đăng ký ChatEscalationReview saga (#566). Reuse Quartz scheduler đã có.
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
