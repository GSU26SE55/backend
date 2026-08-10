using BatteryService.Api.Authentication;
using BatteryService.Application.DependencyInjection;
using BatteryService.Infrastructure.DependencyInjection;
using BatteryService.Infrastructure.Persistence;
using BatteryService.Infrastructure.Persistence.Seeders;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Prometheus;
using SharedInfrastructure.DependencyInjection;
using SharedInfrastructure.Extensions;
using SharedInfrastructure.RateLimiting;

var isEfTooling = string.Equals(
    System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name,
    "ef",
    StringComparison.OrdinalIgnoreCase);

if (isEfTooling)
    return;

EnvFileLoader.LoadIfExists();

var builder = WebApplication.CreateBuilder(args);

// GH-verify-sensor-grpc — Kestrel 2 listener tách biệt để KHÔNG phá REST hiện có:
//   :8080 → HTTP/1 (REST controllers)
//   :8081 → HTTP/2 (gRPC BatteryInternal, nội bộ solar-net)
// gRPC bắt buộc HTTP/2; tách port riêng tránh phải cấu hình Http1AndHttp2 cho REST.
// LƯU Ý: ConfigureKestrel + Listen* GHI ĐÈ hoàn toàn ASPNETCORE_URLS → phải bind LẠI
// :8080 ở đây (nếu không REST mất binding). httpPort đọc từ ASPNETCORE_URLS hoặc mặc định 8080.
if (!EF.IsDesignTime)
{
    var grpcPort = builder.Configuration.GetValue("Grpc:Port", 8081);
    var httpPort = builder.Configuration.GetValue("Http:Port", 8080);
    builder.WebHost.ConfigureKestrel(options =>
    {
        // REST — HTTP/1.1 (giữ nguyên hành vi cũ của :8080).
        options.ListenAnyIP(httpPort, listen => listen.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1AndHttp2);
        // gRPC — HTTP/2 thuần.
        options.ListenAnyIP(grpcPort, listen => listen.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2);
    });
}

builder.Services.AddGrpc();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.DocInclusionPredicate((_, _) => true);

    // Include XML doc từ:
    //   - BatteryService.Api.xml      → controller summaries + remarks
    //   - BatteryService.Application.xml → DTOs / Commands / Queries → Swagger schema fields
    //   - BatteryService.Domain.xml      → entity / enum doc nếu reference từ DTO
    foreach (var asm in new[] { "BatteryService.Api", "BatteryService.Application", "BatteryService.Domain" })
    {
        var xmlPath = Path.Combine(AppContext.BaseDirectory, $"{asm}.xml");
        if (File.Exists(xmlPath))
            options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
    }

    // Bọc enum $ref trong allOf để giữ được <summary> mô tả của property enum.
    // OpenAPI 3.0 bỏ qua description đứng cạnh $ref trần → không có option này thì
    // các field enum (classification, staffFeedback, feedback...) mất mô tả trên Swagger.
    options.UseAllOfToExtendReferenceSchemas();

    options.AddSecurityDefinition(ApiKeyAuthenticationHandler.SchemeName, new OpenApiSecurityScheme
    {
        Description = "API key for IoT sensor ingest. Send it via X-Api-Key.",
        Name = ApiKeyAuthenticationHandler.HeaderName,
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = ApiKeyAuthenticationHandler.SchemeName
    });
});

builder.Services.AddBatteryApplication();
builder.Services.AddBatteryInfrastructure(builder.Configuration);
// Hạn mức nền cho mọi endpoint (60 req/30s ẩn danh · 500 req/30s đã đăng nhập).
builder.Services.AddStandardRateLimiting(builder.Configuration);

builder.Services
    .AddAuthentication()
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationHandler.SchemeName,
        _ => { });

var app = builder.Build();

if (!EF.IsDesignTime)
{
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var conn = db.Database.GetConnectionString();
        Console.WriteLine($"Connection string configured: {!string.IsNullOrWhiteSpace(conn)}");

        var pending = db.Database.GetPendingMigrations().ToList();
        Console.WriteLine($"?? Pending migrations: {pending.Count}");

        if (pending.Any())
        {
            Console.WriteLine("?? Running database migrations...");
            db.Database.Migrate();
            Console.WriteLine("? Migration completed.");
        }
        else
        {
            Console.WriteLine("? No pending migrations.");
        }

        var seeder = scope.ServiceProvider.GetRequiredService<BatteryDataSeeder>();
        await seeder.SeedAsync();
        Console.WriteLine("? Battery seed data checked.");

        var envSeeder = scope.ServiceProvider.GetRequiredService<EnvironmentDataSeeder>();
        await envSeeder.SeedAsync();
        Console.WriteLine("? Environment seed data checked.");
    }

    app.UseSharedInfrastructure();

    app.UseHttpMetrics();

    if (!app.Environment.IsProduction())
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "Battery Service API");
        });
    }

    if (!app.Environment.IsEnvironment("Docker")
        && !builder.Configuration.GetValue("DisableHttpsRedirection", false))
        app.UseHttpsRedirection();

    app.UseCors(SharedInfrastructure.DependencyInjection.Extensions.AddCORS.PolicyName);

    // Sprint IoT-2 #IoT2-35 — serve firmware binary đã upload (multipart) qua static path.
    var firmwareRoot = builder.Configuration["Firmware:StorageRoot"];
    if (string.IsNullOrWhiteSpace(firmwareRoot))
        firmwareRoot = Path.Combine(app.Environment.ContentRootPath, "firmware-storage");
    Directory.CreateDirectory(firmwareRoot);
    app.UseStaticFiles(new Microsoft.AspNetCore.Builder.StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(firmwareRoot),
        RequestPath = "/firmware-storage",
        ServeUnknownFileTypes = true,
        DefaultContentType = "application/octet-stream"
    });

    app.UseAuthentication();
    app.UseAuthorization();
    // PHẢI đứng sau hai dòng trên — xem StandardRateLimitingExtensions.UseStandardRateLimiter.
    app.UseStandardRateLimiter();

    app.MapControllers();
    app.MapMetrics();

    // GH-verify-sensor-grpc — gRPC endpoint chỉ bind trên listener HTTP/2 (:8081).
    app.MapGrpcService<BatteryService.Api.Grpc.BatteryInternalService>();

    app.Run();
}

public partial class Program { }
