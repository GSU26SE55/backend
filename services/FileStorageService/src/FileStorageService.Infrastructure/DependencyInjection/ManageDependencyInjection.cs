using Amazon.S3;
using FileStorageService.Application.Interfaces;
using FileStorageService.Infrastructure.Implements.Repositories;
using FileStorageService.Infrastructure.Options;
using FileStorageService.Infrastructure.Persistence;
using FileStorageService.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FileStorageService.Infrastructure.DependencyInjection;

public static class ManageDependencyInjection
{
    /// <param name="isLocalEnvironment">
    /// GH-788 — truyền tường minh thay vì tự đoán từ biến môi trường: quy tắc "credential mặc định
    /// bị cấm" chỉ được nới ở môi trường cục bộ, nên nếu đọc nhầm môi trường thì production sẽ âm
    /// thầm chạy dưới luật của máy cá nhân — đúng loại hỏng mà không ai thấy.
    /// Dựng giá trị này bằng <see cref="ObjectStorageCredentialGuard.IsLocalEnvironment(string)"/>
    /// chứ đừng dùng <c>IsDevelopment()</c>: stack docker của repo chạy dưới tên môi trường
    /// <c>Docker</c>, và chính nhầm lẫn đó từng làm service crash-loop.
    /// </param>
    public static IServiceCollection AddFileStorageInfrastructure(
        this IServiceCollection services, IConfiguration configuration, bool isLocalEnvironment)
    {
        services.AddDatabase(configuration);
        services.Configure<ObjectStorageOptions>(configuration.GetSection(ObjectStorageOptions.SectionName));

        var options = configuration
            .GetSection(ObjectStorageOptions.SectionName)
            .Get<ObjectStorageOptions>() ?? new ObjectStorageOptions();

        ObjectStorageCredentialGuard.ThrowIfInvalid(options, isLocalEnvironment);

        services.AddSingleton<IAmazonS3>(_ => ObjectStorageClientFactory.Create(options, useInternal: true));

        // Client riêng cho presigned URL: dùng PublicServiceUrl (browser-accessible) để Host trong
        // signature khớp với URL mà browser sẽ truy cập.
        services.AddKeyedSingleton<IAmazonS3>("public",
            (_, _) => ObjectStorageClientFactory.Create(options, useInternal: false));

        services.AddScoped<IObjectStorageService, S3CompatibleFileStorageService>();
        services.AddScoped<IFileStorageUnitOfWork, UnitOfWork>();
        return services;
    }

    private static void AddDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("FileStorageDb")
                               ?? configuration["FileStorageDb"]
                               ?? configuration["FileStorage_Db"]
                               ?? configuration["FILE_STORAGE_DB"];

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "Missing FileStorage database connection string. Expected ConnectionStrings__FileStorageDb, FileStorageDb, FileStorage_Db, or FILE_STORAGE_DB.");

        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        services.AddScoped<DbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());
    }
}
