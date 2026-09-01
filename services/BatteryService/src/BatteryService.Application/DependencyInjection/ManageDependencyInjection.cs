using Microsoft.Extensions.DependencyInjection;

namespace BatteryService.Application.DependencyInjection;

public static class ManageDependencyInjection
{
    // MediatR đã được đăng ký bởi AddSharedInfrastructure (Infrastructure DI) cho cùng assembly
    // "BatteryService.Application" — gọi AddMediatR ở đây nữa làm mọi INotificationHandler
    // chạy 2 lần (audit log ghi đôi). Giữ method để Program.cs không phải đổi call site.
    public static IServiceCollection AddBatteryApplication(this IServiceCollection services)
    {
        return services;
    }
}
