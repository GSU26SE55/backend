using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Prometheus;
using SharedContracts.Interfaces;
using SharedInfrastructure.Bus;
using SharedInfrastructure.DependencyInjection;
using SharedInfrastructure.Extensions;
using SharedInfrastructure.Idempotency;
using SharedInfrastructure.RateLimiting;
using SmsService.Api.Swagger;
using SmsService.Application.Interfaces.Repositories;
using SmsService.Application.Interfaces.Services;
using SmsService.Infrastructure.BackgroundJobs;
using SmsService.Infrastructure.Implements.Repositories;
using SmsService.Infrastructure.Implements.Services;
using SmsService.Infrastructure.Options;
using SmsService.Infrastructure.Persistence;
using SmsService.Infrastructure.Realtime;
using SmsService.Infrastructure.Security;
using AppDI = SmsService.Application.DependencyInjection.ManageDependencyInjection;

EnvFileLoader.LoadIfExists();

var builder = WebApplication.CreateBuilder(args);

// ── 1. Config ─────────────────────────────────────────────────────────────
builder.Services.Configure<SmsOptions>(builder.Configuration.GetSection(SmsOptions.SectionName));
builder.Services.Configure<OutboxOptions>(builder.Configuration.GetSection(OutboxOptions.SectionName));

// ── 2. DbContext + UoW ────────────────────────────────────────────────────
var connectionString = builder.Configuration.GetConnectionString("SmsDb")
                       ?? builder.Configuration["SmsDb"]
                       ?? builder.Configuration["Sms_Db"]
                       ?? builder.Configuration["SMS_DB"]
                       ?? throw new InvalidOperationException(
                           "Missing SMS database connection string. Expected ConnectionStrings__SmsDb / SmsDb / Sms_Db / SMS_DB.");

builder.Services.AddDbContext<SmsDbContext>(opt => opt.UseNpgsql(connectionString));
builder.Services.AddScoped<DbContext>(sp => sp.GetRequiredService<SmsDbContext>());
builder.Services.AddScoped<ISmsUnitOfWork, SmsUnitOfWork>();
// GH-794 — giành quyền publish từng dòng outbox, chặn hai replica cùng gửi một tin nhắn.
builder.Services.AddScoped<SmsService.Application.Interfaces.Services.IOutboxClaimService,
    SmsService.Infrastructure.Implements.Services.OutboxClaimService>();

// Sprint 6.3 NOTI3-05 (#705) — seam để sau này cắm provider SMS thứ hai (Twilio/Vonage) mà không
// phải sửa business logic. Hiện tại chỉ có gateway Android — giới hạn đã ghi nhận ở R-44.
builder.Services.AddScoped<ISmsProvider, GatewaySmsProvider>();

// ── 3. SharedInfrastructure ───────────────────────────────────────────────
// CRITICAL: assemblyName là "SmsService.Application" — KHÔNG phải Api.
// `AddMediatRInfrastructure` dùng Assembly.Load(assemblyName) để scan handlers; sai tên → runtime crash.
builder.Services.AddSharedInfrastructure(
    builder.Configuration,
    assemblyName: "SmsService.Application",
    apiTitle: "SMS Service API");

// Sprint SMS — SmsService có 2 auth scheme:
//   - JWT Bearer (default từ AddSharedSwaggerGen) cho admin endpoints
//   - GatewayApiKey (BCrypt key + X-Device-Code header) cho Flutter gateway endpoints
// SmsGatewaySwaggerExtensions add scheme thứ 2 + operation filter map đúng từng endpoint.
builder.Services.AddSmsGatewaySwagger();

// ── 4. Inbox (Redis) ──────────────────────────────────────────────────────
builder.Services.AddInboxIdempotency(builder.Configuration);

// ── 5. MassTransit consumers ──────────────────────────────────────────────
// Consumer assembly: SmsService.Application chứa SendSmsCommandConsumer + SendPhoneOtpConsumer (Phase 5).
// AddMessageBus đăng ký IMessageProducerService = MassTransitProducer — step 6 OVERRIDE bằng OutboxMessagePublisher.
// GH-728 — thêm assembly Infrastructure để MassTransit thấy SmsAuditReplayRequestedConsumer.
builder.Services.AddMessageBus(
    builder.Configuration,
    configure: null,
    AppDI.ApplicationAssembly,
    typeof(SmsService.Infrastructure.Consumers.SmsAuditReplayRequestedConsumer).Assembly);

// ── 6. Outbox publisher (PHẢI đăng ký SAU AddMessageBus để override) ──────
// Last-registration-wins: handler resolve IMessageProducerService → OutboxMessagePublisher.
// OutboxRelayBackgroundService poll DB → IPublishEndpoint của MassTransit publish thật.
builder.Services.AddScoped<IMessageProducerService, OutboxMessagePublisher>();
builder.Services.AddHostedService<OutboxRelayBackgroundService>();

// ── 7. SignalR + Notifier ─────────────────────────────────────────────────
builder.Services.AddSignalR(o =>
{
    o.EnableDetailedErrors = builder.Environment.IsDevelopment();
    o.KeepAliveInterval = TimeSpan.FromSeconds(15);
    o.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
});
builder.Services.AddSingleton<ISmsGatewayNotifier, SignalRSmsGatewayNotifier>();

// ── 8. Security — GatewayApiKey scheme (Flutter) ──────────────────────────
builder.Services.AddSingleton<IGatewayApiKeyHasher, BcryptGatewayApiKeyHasher>();
builder.Services
    .AddAuthentication() // JwtBearer đã add bởi AddSharedInfrastructure
    .AddScheme<GatewayAuthOptions, GatewayApiKeyAuthenticationHandler>(GatewayAuthOptions.SchemeName, _ => { });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("GatewayDevice", p => p
        .AddAuthenticationSchemes(GatewayAuthOptions.SchemeName)
        .RequireAuthenticatedUser()
        .RequireClaim("device_code"));

// ── 9. Rate limiter ───────────────────────────────────────────────────────
// Hạn mức nền cho mọi endpoint (60 req/30s ẩn danh · 500 req/30s đã đăng nhập).
builder.Services.AddStandardRateLimiting(builder.Configuration);

// Policy chặt hơn cho endpoint gateway của thiết bị SMS (60 req/phút/device) — chạy chồng lên hạn mức nền.
// KHÔNG đặt OnRejected/RejectionStatusCode ở đây: xem StandardRateLimitingExtensions.
builder.Services.AddRateLimiter(o =>
{
    o.AddPolicy("gateway", httpContext =>
    {
        var deviceCode = httpContext.Request.Headers["X-Device-Code"].ToString();
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: string.IsNullOrEmpty(deviceCode) ? "anon" : deviceCode,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
    });
});

// ── 10. Background workers (StaleReaper + Redactor) ───────────────────────
builder.Services.AddHostedService<StaleSmsReaperBackgroundService>();
builder.Services.AddHostedService<SmsMessageRedactorBackgroundService>();
builder.Services.AddHostedService<SmsService.Infrastructure.BackgroundJobs.SmsAuditOutboxRelayBackgroundService>(); // Sprint audit #AUDIT-35

// ── 11. Controllers + Swagger ─────────────────────────────────────────────
builder.Services.AddControllers();

// ── 12. Build & pipeline ──────────────────────────────────────────────────
var app = builder.Build();

// Auto-migrate khi startup — pattern khớp AuthService.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SmsDbContext>();
    var pending = db.Database.GetPendingMigrations().ToList();
    if (pending.Count > 0)
    {
        Console.WriteLine($"[SmsService] Running {pending.Count} pending migration(s)...");
        db.Database.Migrate();
        Console.WriteLine("[SmsService] Migration completed.");
    }
}

// SecurityHeaders + CorrelationId + RequestLogging + GlobalException + CommonResponseStatusCodes
app.UseSharedInfrastructure();
app.UseHttpMetrics();

if (!app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "SMS Service API");
    });
}

if (!app.Environment.IsEnvironment("Docker")
    && !builder.Configuration.GetValue("DisableHttpsRedirection", false))
{
    app.UseHttpsRedirection();
}

app.UseCors(SharedInfrastructure.DependencyInjection.Extensions.AddCORS.PolicyName);
app.UseAuthentication();
app.UseAuthorization();
// PHẢI đứng sau Authentication/Authorization. Trước đây dòng này nằm trước cả hai, nên
// HttpContext.User còn rỗng và mọi request đều bị xếp vào bậc ẩn danh.
app.UseStandardRateLimiter();

// ── 13. Endpoints ─────────────────────────────────────────────────────────
app.MapGet("/", () => "SMS Gateway Service is Running...");
app.MapMetrics();
app.MapControllers();
app.MapHub<SmsGatewayHub>("/hubs/sms-gateway");

app.Run();

public partial class Program { }
