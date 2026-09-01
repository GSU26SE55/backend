using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TicketService.Application.Common.Behaviors;
using TicketService.Application.Common.Models;
using TicketService.Application.StateMachine;
using TicketService.Application.StateMachine.Rules;

namespace TicketService.Application.DependencyInjection;

public static class ManageDependencyInjection
{
    public static IServiceCollection AddTicketServiceApplication(this IServiceCollection services, IConfiguration configuration)
    {
        // Options
        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.SectionName));
        services.Configure<ChatOptions>(configuration.GetSection(ChatOptions.SectionName));

        // MediatR đã được đăng ký bởi AddSharedInfrastructure (Infrastructure DI) cho cùng assembly
        // "TicketService.Application" — gọi AddMediatR ở đây nữa làm mọi INotificationHandler
        // chạy 2 lần (audit log ghi đôi).
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ClosedTicketMutationBehavior<,>));
        services.AddScoped<ITicketStateMachine, TicketStateMachine>();
        services.AddScoped<ITransitionRuleProvider, TransitionRuleProvider>();
        return services;
    }
}
