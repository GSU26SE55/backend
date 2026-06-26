using AuditAggregatorService.Application.CQRS.Command.Audit;
using Microsoft.Extensions.DependencyInjection;

namespace AuditAggregatorService.Application.DependencyInjection;

/// <summary>
/// DI cho Application layer của AuditAggregatorService — đăng ký MediatR (CQRS handlers) (Sprint audit <c>#AUDIT-17</c>).
/// Theo cùng pattern các service khác (Battery/Ticket/...).
/// </summary>
public static class ManageDependencyInjection
{
    public static IServiceCollection AddAuditAggregatorApplication(this IServiceCollection services)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(AuditRedactCommand).Assembly));
        return services;
    }
}
