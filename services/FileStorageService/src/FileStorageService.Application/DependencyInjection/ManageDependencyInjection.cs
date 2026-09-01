using FileStorageService.Application.Authorization;
using FileStorageService.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace FileStorageService.Application.DependencyInjection;

public static class ManageDependencyInjection
{
    public static IServiceCollection AddFileStorageApplication(this IServiceCollection services)
    {
        // MediatR đã được đăng ký bởi AddSharedInfrastructure (Infrastructure DI) cho cùng assembly
        // "FileStorageService.Application" — gọi AddMediatR ở đây nữa làm mọi INotificationHandler
        // chạy 2 lần (audit log ghi đôi).
        services.AddScoped<IFileAuthorizationService, FileAuthorizationService>();
        return services;
    }
}
