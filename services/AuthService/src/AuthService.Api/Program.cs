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
builder.Services.AddOtpRateLimiting();

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
            $"Ephemeral fallback bị disable trong production để tránh mất 2FA secret hàng loạt khi restart. " +
            $"Hành động: kiểm tra Docker volume `auth-dataprotection-keys` mount đúng + quyền ghi cho user container. " +
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

app.UseCors("AllowAll");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Idempotency-Key middleware (sau Auth, trước MapControllers) — chống duplicate POST/PUT/PATCH
// khi client gửi cùng header "Idempotency-Key".
app.UseIdempotencyKey();

app.MapControllers();

// Expose /metrics endpoint cho Prometheus scrape.
app.MapMetrics();

app.Run();

/// <summary>
/// Marker class để WebApplicationFactory&lt;Program&gt; trong integration tests có thể reference Program.
/// Top-level statements file phải có partial class declaration để ngoài assembly truy cập được.
/// </summary>
public partial class Program { }
