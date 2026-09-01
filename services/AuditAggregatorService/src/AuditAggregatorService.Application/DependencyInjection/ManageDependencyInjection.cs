using Microsoft.Extensions.DependencyInjection;

namespace AuditAggregatorService.Application.DependencyInjection;

/// <summary>
/// DI cho Application layer của AuditAggregatorService. MediatR đã được đăng ký bởi
/// AddSharedInfrastructure (Infrastructure DI) cho cùng assembly "AuditAggregatorService.Application" —
/// gọi AddMediatR ở đây nữa làm mọi INotificationHandler chạy 2 lần (audit log ghi đôi).
/// </summary>
public static class ManageDependencyInjection
{
    public static IServiceCollection AddAuditAggregatorApplication(this IServiceCollection services)
    {
        return services;
    }
}
