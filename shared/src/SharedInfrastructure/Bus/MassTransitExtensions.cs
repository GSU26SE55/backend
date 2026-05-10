using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedContracts.Interfaces;

namespace SharedInfrastructure.Bus;

public static class MassTransitExtensions
{
    public static IServiceCollection AddMessageBus(this IServiceCollection services, IConfiguration configuration, params System.Reflection.Assembly[] consumerAssemblies)
    {
        services.AddHttpContextAccessor();

        services.AddMassTransit(x =>
        {
            if (consumerAssemblies != null && consumerAssemblies.Length > 0)
            {
                x.AddConsumers(consumerAssemblies);
            }

            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(configuration["RabbitMQ:Host"], "/", h =>
                {
                    h.Username(configuration["RabbitMQ:Username"]!);
                    h.Password(configuration["RabbitMQ:Password"]!);
                });

                // Forward Correlation ID qua message header khi publish, đọc lại khi consume.
                cfg.UsePublishFilter(typeof(CorrelationIdPublishFilter<>), context);
                cfg.UseConsumeFilter(typeof(CorrelationIdConsumeFilter<>), context);

                cfg.ConfigureEndpoints(context);
            });
        });

        services.AddScoped<IMessageProducerService, MassTransitProducer>();
        return services;
    }
}
