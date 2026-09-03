using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using NotificationService.Infrastructure.DependencyInjection;
using NotificationService.Infrastructure.Persistence;
using NotificationService.Infrastructure.Realtime;
using Prometheus;
using SharedInfrastructure.DependencyInjection;
using SharedInfrastructure.Extensions;
using SharedInfrastructure.Idempotency;
using SharedInfrastructure.RateLimiting;

EnvFileLoader.LoadIfExists();

var builder = WebApplication.CreateBuilder(args);

// Hạn mức nền cho mọi endpoint (60 req/30s ẩn danh · 500 req/30s đã đăng nhập).
builder.Services.AddStandardRateLimiting(builder.Configuration);

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

builder.Services.AddNotificationServiceInfrastructure(builder.Configuration);
builder.Services.AddIdempotencyKey(builder.Configuration);

// Self-hosted transport: both the live feed and push-policy payloads travel through this hub.
// Android turns push-policy payloads into native notifications and bubbles locally.
var signalRBuilder = builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
})
.AddJsonProtocol(options =>
{
    options.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    // Do not add JsonStringEnumConverter here. NotificationReceived must match the numeric
    // enums returned by the notification REST API and consumed by the mobile application.
});

// Share SignalR groups across replicas when Redis is configured. A single instance still
// works without a backplane in local development.
var signalRRedisConnection = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(signalRRedisConnection))
{
    signalRBuilder.AddStackExchangeRedis(signalRRedisConnection, options =>
    {
        options.Configuration.ChannelPrefix = new StackExchange.Redis.RedisChannel(
            "Notification",
            StackExchange.Redis.RedisChannel.PatternMode.Literal);
    });
}

// The SignalR JavaScript client sends the bearer token in the access_token query parameter
// during WebSocket negotiation. Restrict query-token handling to this hub only.
builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
{
    var existingOnMessageReceived = options.Events.OnMessageReceived;
    options.Events.OnMessageReceived = async context =>
    {
        if (existingOnMessageReceived is not null)
            await existingOnMessageReceived(context);

        var accessToken = context.Request.Query["access_token"];
        if (!string.IsNullOrEmpty(accessToken)
            && context.HttpContext.Request.Path.StartsWithSegments("/hubs/notifications"))
        {
            context.Token = accessToken;
        }
    };
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var conn = db.Database.GetConnectionString();
    Console.WriteLine($"Connection string configured: {!string.IsNullOrWhiteSpace(conn)}");

    var pending = db.Database.GetPendingMigrations().ToList();
    Console.WriteLine($"Pending migrations: {pending.Count}");

    if (pending.Any())
    {
        Console.WriteLine("Running database migrations...");
        db.Database.Migrate();
        Console.WriteLine("Migration completed.");
    }
    else
    {
        Console.WriteLine("No pending migrations.");
    }

    // Sprint 6.4 NOTI4-04 — 4 nhóm hệ thống theo vai trò. PHẢI chạy mỗi lần khởi động: sau
    // NOTI4-05, RecipientResolver phân giải "toàn bộ Manager/Admin" qua chính các nhóm này;
    // thiếu một cái là thông báo tự động cho vai trò đó im lặng biến mất.
    var groupSeeder = scope.ServiceProvider.GetRequiredService<NotificationService.Infrastructure.Persistence.Seeders.NotificationGroupSeeder>();
    await groupSeeder.SeedAsync();

    Console.WriteLine("Notification system groups checked.");
}

app.UseSharedInfrastructure();
app.UseHttpMetrics();

if (!app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Notification Service API");
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
// PHẢI đứng sau hai dòng trên — xem StandardRateLimitingExtensions.UseStandardRateLimiter.
app.UseStandardRateLimiter();
app.UseIdempotencyKey();

app.MapControllers();

// Sprint 6.3 NOTI3-13 (#713) — bắt buộc đăng nhập: hub phát thông báo riêng của từng người.
app.MapHub<NotificationHub>("/hubs/notifications").RequireAuthorization();

app.MapMetrics();

app.Run();

public partial class Program { }
