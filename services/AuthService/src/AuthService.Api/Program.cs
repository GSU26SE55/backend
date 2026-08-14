using AuthService.Api.Extensions;
using AuthService.Infrastructure.DependencyInjection;
using AuthService.Infrastructure.Persistence;
using AuthService.Infrastructure.Persistence.Seeders;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Prometheus;
using SharedInfrastructure.DependencyInjection;
using SharedInfrastructure.Extensions;
using SharedInfrastructure.Idempotency;
using SharedInfrastructure.RateLimiting;

EnvFileLoader.LoadIfExists();

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.DocInclusionPredicate((_, _) => true);

    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
    }
});

builder.Services.AddAuthServiceInfrastructure(builder.Configuration);
builder.Services.AddIdempotencyKey(builder.Configuration);
// Hạn mức nền cho mọi endpoint (60 req/30s ẩn danh · 500 req/30s đã đăng nhập).
builder.Services.AddStandardRateLimiting(builder.Configuration);
// Các policy chặt hơn theo từng endpoint nhạy cảm (login, OTP, 2FA) — chạy chồng lên hạn mức nền.
builder.Services.AddOtpRateLimiting();

// #AUTH-60: health checks chuẩn k8s — /live (liveness, app process alive),
// /ready (readiness, deps ready: DB + Redis + RabbitMQ), /health (full report).
// Tag "live" = không touch deps. Tag "ready" = check deps. Custom checks tránh phụ thuộc
// package extra (Microsoft.Extensions.Diagnostics.HealthChecks.*).
builder.Services.AddHealthChecks()
    .AddCheck<AuthService.Api.HealthChecks.PostgresHealthCheck>("postgres", tags: new[] { "ready" })
    .AddCheck<AuthService.Api.HealthChecks.RedisHealthCheck>("redis", tags: new[] { "ready" })
    .AddCheck<AuthService.Api.HealthChecks.RabbitMqHealthCheck>("rabbitmq", tags: new[] { "ready" });

// Data Protection — encrypt TwoFactorSecret at rest (GH-295).
// Keys persist tới /app/keys (mount Docker volume `auth-dataprotection-keys` để cross-restart).
//
// **Production behavior:** PHẢI persist được. Nếu path unwritable → THROW preventing startup.
//   Lý do: silent ephemeral fallback trong production = mỗi container restart wipes key
//   → mọi 2FA secret encrypted trong DB không decrypt được nữa → user lock-out hàng loạt.
//   Better fail-loud at startup để OPS phát hiện ngay + fix volume mount.
//
// **Local dev (Development/Testing):** Forgiving — fallback ephemeral nếu path không có (Mac/Win local).
//   Mất key dev = chỉ ảnh hưởng user enroll dev, chấp nhận được.
var dpKeysPath = builder.Configuration["DataProtection:KeysPath"]
                 ?? Environment.GetEnvironmentVariable("DATAPROTECTION_KEYS_PATH")
                 ?? "/app/keys";
var isProductionLike = builder.Environment.IsProduction()
                        || string.Equals(builder.Environment.EnvironmentName, "Docker", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(builder.Environment.EnvironmentName, "Staging", StringComparison.OrdinalIgnoreCase);
try
{
    Directory.CreateDirectory(dpKeysPath);
    // Sanity check: phải có quyền ghi vào path (không chỉ tạo folder).
    var probeFile = Path.Combine(dpKeysPath, $".write-probe-{Guid.NewGuid():N}");
    File.WriteAllText(probeFile, "ok");
    File.Delete(probeFile);

    builder.Services.AddDataProtection()
        .SetApplicationName("AuthService")
        .PersistKeysToFileSystem(new DirectoryInfo(dpKeysPath));
    Console.WriteLine($"[DataProtection] Keys persisted to: {dpKeysPath}");
}
catch (Exception ex)
{
    if (isProductionLike)
    {
        // Fail loud — block startup. OPS phải fix volume mount trước khi cho service chạy lại.
        throw new InvalidOperationException(
            $"[DataProtection] CRITICAL — Cannot write to '{dpKeysPath}' in {builder.Environment.EnvironmentName} environment. " +
            $"Ephemeral fallback is disabled in production to avoid mass-losing 2FA secrets on restart. " +
            $"Action: verify the Docker volume `auth-dataprotection-keys` is mounted correctly and writable by the container user. " +
            $"Inner: {ex.Message}", ex);
    }
    Console.WriteLine($"[DataProtection] Local dev fallback to ephemeral keys (path '{dpKeysPath}' unwritable): {ex.Message}");
    builder.Services.AddDataProtection().SetApplicationName("AuthService");
}

var app = builder.Build();
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

    var seeder = scope.ServiceProvider.GetRequiredService<AuthDataSeeder>();
    await seeder.SeedAsync();
    Console.WriteLine("? Auth seed data checked.");
}

app.UseSharedInfrastructure();

// Prometheus HTTP metrics — auto-collect request count, latency, status code cho mọi endpoint.
app.UseHttpMetrics();
// Bật Swagger cho mọi non-Production env (Development, Docker, Staging).
if (!app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Auth Service API");
    });
}

// HTTPS redirect chỉ khi service listen HTTPS. Docker chạy HTTP-only.
if (!app.Environment.IsEnvironment("Docker")
    && !builder.Configuration.GetValue("DisableHttpsRedirection", false))
{
    app.UseHttpsRedirection();
}

app.UseCors(SharedInfrastructure.DependencyInjection.Extensions.AddCORS.PolicyName);
app.UseAuthentication();
// #AUTH-54: chạy SAU JwtBearer authentication, TRƯỚC Authorization.
// Nếu jti hoặc account đã bị revoke → trả 401 ngay, không cho qua Authorization.
app.UseMiddleware<AuthService.Api.Middleware.TokenRevocationMiddleware>();
app.UseAuthorization();
// PHẢI đứng sau Authentication/Authorization. Trước đây dòng này nằm ngay sau UseCors, tức là
// chạy khi HttpContext.User còn rỗng — nên các policy khai là "theo UserId" (AuthOtp,
// TwoFactorDisable, BackupCodeRegenerate) thực tế đều rơi xuống nhánh dự phòng và gom theo IP.
app.UseStandardRateLimiter();

// Idempotency-Key middleware (sau Auth, trước MapControllers) — chống duplicate POST/PUT/PATCH
// khi client gửi cùng header "Idempotency-Key".
app.UseIdempotencyKey();

app.MapControllers();

// #AUTH-60: k8s probes.
// /live — liveness: app process alive. Predicate: skip all deps checks → luôn 200 nếu app run.
// /ready — readiness: full deps probe (postgres, redis). Predicate: chỉ check "ready" tag.
// /health — full report (alias /ready).
app.MapHealthChecks("/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});
app.MapHealthChecks("/health");

// Expose /metrics endpoint cho Prometheus scrape.
app.MapMetrics();

app.Run();

/// <summary>
/// Marker class để WebApplicationFactory&lt;Program&gt; trong integration tests có thể reference Program.
/// Top-level statements file phải có partial class declaration để ngoài assembly truy cập được.
/// </summary>
public partial class Program { }
