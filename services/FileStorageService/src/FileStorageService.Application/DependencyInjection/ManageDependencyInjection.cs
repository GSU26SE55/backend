using FileStorageService.Application.Authorization;
using FileStorageService.Application.CQRS.Command;
using FileStorageService.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace FileStorageService.Application.DependencyInjection;

public static class ManageDependencyInjection
{
    public static IServiceCollection AddFileStorageApplication(this IServiceCollection services)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(UploadFileCommand).Assembly));
        services.AddScoped<IFileAuthorizationService, FileAuthorizationService>();
        return services;
    }
}
