using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using TicketService.Infrastructure.Persistence;

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
        x.AddPublishMessageScheduler();
    }
}
