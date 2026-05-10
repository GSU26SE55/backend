using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using FileStorageService.Application.Interfaces;
using FileStorageService.Infrastructure.Options;
using FileStorageService.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FileStorageService.Infrastructure.DependencyInjection;

public static class ManageDependencyInjection
{
    public static IServiceCollection AddFileStorageInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ObjectStorageOptions>(configuration.GetSection(ObjectStorageOptions.SectionName));

        var options = configuration
            .GetSection(ObjectStorageOptions.SectionName)
            .Get<ObjectStorageOptions>() ?? new ObjectStorageOptions();

        services.AddSingleton<IAmazonS3>(_ => CreateS3Client(options, useInternal: true));

        // Client riêng cho presigned URL: dùng PublicServiceUrl (browser-accessible) để Host trong
        // signature khớp với URL mà browser sẽ truy cập.
        services.AddKeyedSingleton<IAmazonS3>("public", (_, _) => CreateS3Client(options, useInternal: false));

        services.AddScoped<IObjectStorageService, S3CompatibleFileStorageService>();
        return services;
    }

    private static IAmazonS3 CreateS3Client(ObjectStorageOptions options, bool useInternal)
    {
        var credentials = new BasicAWSCredentials(options.AccessKey, options.SecretKey);
        var serviceUrl = useInternal || string.IsNullOrWhiteSpace(options.PublicServiceUrl)
            ? options.ServiceUrl
            : options.PublicServiceUrl;

        var s3Config = new AmazonS3Config
        {
            ServiceURL = serviceUrl,
            ForcePathStyle = options.ForcePathStyle,
            AuthenticationRegion = options.Region,
        };

        return new AmazonS3Client(credentials, s3Config);
    }
}
