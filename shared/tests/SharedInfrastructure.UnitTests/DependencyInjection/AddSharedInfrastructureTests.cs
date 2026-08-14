using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedContracts.Interfaces;
using SharedInfrastructure.Behaviors;
using SharedInfrastructure.Caching;
using SharedInfrastructure.DependencyInjection;
using SharedInfrastructure.Persistence.Interceptors;
using SharedInfrastructure.Services;
using SharedKernels.Interfaces;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SharedInfrastructure.UnitTests.DependencyInjection;

/// <summary>
/// Test AddSharedInfrastructure (composition root): tất cả services + middleware đăng ký đúng.
/// Dùng giả lập IConfiguration đầy đủ để pass through các sub-extension.
/// </summary>
public class AddSharedInfrastructureTests
{
    private static IConfiguration BuildConfig() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtSettings:SecretKey"] = "test-jwt-secret-key-must-be-at-least-32-chars-long-1234567",
            ["JwtSettings:Issuer"] = "test-issuer",
            ["JwtSettings:Audience"] = "test-audience",
            ["ConnectionStrings:Redis"] = "localhost:6379"
        }).Build();

    private static IServiceCollection BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouting();
        // Swagger nội bộ cần IWebHostEnvironment để build options.
        // #AUTH-76: GlobalExceptionMiddleware cũng inject IHostEnvironment để check IsProduction
        // → ẩn exceptionType/exceptionMessage trong prod. Register cả 2 interface từ cùng StubEnv.
        var env = new StubEnv();
        services.AddSingleton<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>(env);
        services.AddSingleton<Microsoft.Extensions.Hosting.IHostEnvironment>(env);
        // assemblyName bắt buộc — dùng tên SharedContracts vì có Application-style classes (IValidatable).
        // MediatR scan assembly cũng cần nó load được.
        services.AddSharedInfrastructure(BuildConfig(), assemblyName: "SharedContracts", apiTitle: "Test API");
        return services;
    }

    private class StubEnv : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Test";
        public string WebRootPath { get; set; } = "/tmp";
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string ContentRootPath { get; set; } = "/tmp";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    [Fact]
    public void AddSharedInfrastructure_RegistersPipelineBehaviors_AsTransient()
    {
        var services = BuildServices();

        var loggingBehavior = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IPipelineBehavior<,>) && d.ImplementationType == typeof(LoggingBehavior<,>));
        var validationBehavior = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IPipelineBehavior<,>) && d.ImplementationType == typeof(ValidationBehavior<,>));

        loggingBehavior.Should().NotBeNull();
        loggingBehavior!.Lifetime.Should().Be(ServiceLifetime.Transient);
        validationBehavior.Should().NotBeNull();
        validationBehavior!.Lifetime.Should().Be(ServiceLifetime.Transient);
    }

    [Fact]
    public void AddSharedInfrastructure_RegistersGenericRepository_AsScopedOpenGeneric()
    {
        var services = BuildServices();

        var d = services.FirstOrDefault(x => x.ServiceType == typeof(IGenericRepository<>));
        d.Should().NotBeNull();
        d!.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddSharedInfrastructure_RegistersHttpContextAccessor_AndCurrentUserService()
    {
        var services = BuildServices();

        services.Should().Contain(d => d.ServiceType == typeof(IHttpContextAccessor));

        var cu = services.FirstOrDefault(d => d.ServiceType == typeof(ICurrentUserService));
        cu.Should().NotBeNull();
        cu!.Lifetime.Should().Be(ServiceLifetime.Scoped);
        cu.ImplementationType.Should().Be(typeof(CurrentUserService));
    }

    [Fact]
    public void AddSharedInfrastructure_RegistersAuditableEntityInterceptor_AsScoped()
    {
        var services = BuildServices();

        var d = services.FirstOrDefault(x => x.ServiceType == typeof(AuditableEntityInterceptor));
        d.Should().NotBeNull();
        d!.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddSharedInfrastructure_RegistersCacheService_AsScoped()
    {
        var services = BuildServices();

        var d = services.FirstOrDefault(x => x.ServiceType == typeof(ICacheService));
        d.Should().NotBeNull();
        d!.Lifetime.Should().Be(ServiceLifetime.Scoped);
        d.ImplementationType.Should().Be(typeof(RedisCacheService));

        // IDistributedCache cũng được register (qua AddStackExchangeRedisCache).
        services.Should().Contain(x => x.ServiceType == typeof(IDistributedCache));
    }

    [Fact]
    public void AddSharedInfrastructure_RegistersMediatR_AndCorsAndSwagger()
    {
        var services = BuildServices();

        // MediatR registered:
        services.Should().Contain(d => d.ServiceType == typeof(IMediator));

        // CORS policy (#AUTH-05 đổi tên "AllowAll" -> AddCORS.PolicyName vì nay là whitelist,
        // giữ tên cũ sẽ gây hiểu nhầm là vẫn mở cho mọi origin):
        var sp = services.BuildServiceProvider();
        sp.GetRequiredService<IOptions<CorsOptions>>().Value.GetPolicy(SharedInfrastructure.DependencyInjection.Extensions.AddCORS.PolicyName).Should().NotBeNull();

        // Swagger với title "Test API":
        var swaggerOpts = sp.GetRequiredService<IOptions<SwaggerGeneratorOptions>>().Value;
        swaggerOpts.SwaggerDocs.Should().ContainKey("v1");
        swaggerOpts.SwaggerDocs["v1"].Title.Should().Be("Test API");
    }

    [Fact]
    public void AddSharedInfrastructure_ReturnsServiceCollection_ForChaining()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var returned = services.AddSharedInfrastructure(BuildConfig(), "SharedContracts", "API");

        returned.Should().BeSameAs(services);
    }

    [Fact]
    public void UseSharedInfrastructure_RegistersGlobalExceptionMiddleware()
    {
        var services = BuildServices();
        var sp = services.BuildServiceProvider();
        var app = new ApplicationBuilder(sp);

        var returned = app.UseSharedInfrastructure();

        returned.Should().BeSameAs(app);
        // Build pipeline — không throw → middleware đã append được.
        var pipeline = app.Build();
        pipeline.Should().NotBeNull();
    }
}
