using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Prometheus;
using SharedInfrastructure.DependencyInjection;
using SharedInfrastructure.Extensions;
using SharedInfrastructure.RateLimiting;
using TicketService.Api.Extensions;
using TicketService.Api.Middleware;
using TicketService.Application.DependencyInjection;
using TicketService.Infrastructure.BackgroundJobs;
using TicketService.Infrastructure.DependencyInjection;
using TicketService.Infrastructure.Persistence;
using TicketService.Infrastructure.Persistence.Seeders;
using TicketService.Infrastructure.Realtime;

EnvFileLoader.LoadIfExists();

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // Include XML comments from all relevant projects
    var projects = new[] { "TicketService.Api", "TicketService.Application", "TicketService.Domain", "SharedContracts" };
    foreach (var project in projects)
    {
        var xmlFile = $"{project}.xml";
        var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
        if (File.Exists(xmlPath))
            options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
    }
});

builder.Services.AddTicketServiceApplication(builder.Configuration);
builder.Services.AddTicketServiceInfrastructure(builder.Configuration);
// Hạn mức nền cho mọi endpoint (60 req/30s ẩn danh · 500 req/30s đã đăng nhập).
builder.Services.AddStandardRateLimiting(builder.Configuration);
// Policy chặt hơn cho chat write — chạy chồng lên hạn mức nền.
builder.Services.AddChatRateLimiting();

var signalRBuilder = builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
})
.AddJsonProtocol(options =>
{
    options.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    options.PayloadSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});

// Redis backplane cho SignalR scale-out (multi-instance). Fallback gracefully nếu Redis chưa config.
var signalRRedisConn = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(signalRRedisConn))
{
    signalRBuilder.AddStackExchangeRedis(signalRRedisConn, o =>
    {
        o.Configuration.ChannelPrefix = new StackExchange.Redis.RedisChannel("TicketChat", StackExchange.Redis.RedisChannel.PatternMode.Literal);
    });
}

builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
{
    var existingOnMessageReceived = options.Events.OnMessageReceived;
    options.Events.OnMessageReceived = async context =>
    {
        if (existingOnMessageReceived != null)
        {
            await existingOnMessageReceived(context);
        }

        var accessToken = context.Request.Query["access_token"];
        var path = context.HttpContext.Request.Path;
        if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/ticket-chats"))
        {
            context.Token = accessToken;
        }
    };
});

builder.Services.AddHealthChecks()
    .AddDbContextCheck<TicketDbContext>("TicketDb");

var app = builder.Build();

// Database Migration & Seeding
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TicketDbContext>();
    var conn = db.Database.GetConnectionString();
    Console.WriteLine($"[TicketService] Connection string configured: {!string.IsNullOrWhiteSpace(conn)}");

    var pending = db.Database.GetPendingMigrations().ToList();
    Console.WriteLine($"[TicketService] Pending migrations: {pending.Count}");

    var skipMigration = builder.Configuration.GetValue<bool>("SkipMigration");
    if (pending.Any() && !skipMigration)
    {
        Console.WriteLine("[TicketService] Running database migrations...");
        db.Database.Migrate();
        Console.WriteLine("[TicketService] Migration completed.");
    }
    else if (skipMigration)
    {
        Console.WriteLine("[TicketService] Skipping database migrations (SkipMigration=true).");
    }
    else
    {
        Console.WriteLine("[TicketService] No pending migrations.");
    }

    // Skip seeder khi SkipSeeder=true (integration test với SQLite — xmin column Postgres-only).
    var skipSeeder = app.Configuration.GetValue("SkipSeeder", false);
    if (!app.Environment.IsProduction() && !skipSeeder)
    {
        var seeder = scope.ServiceProvider.GetRequiredService<TicketDataSeeder>();
        await seeder.SeedAsync();
        Console.WriteLine("[TicketService] Seed data checked for non-production environment.");
    }
    else if (skipSeeder)
    {
        Console.WriteLine("[TicketService] Skipping seed (SkipSeeder=true).");
    }
}

app.UseSharedInfrastructure();
app.UseMiddleware<TicketConcurrencyExceptionMiddleware>();

// UseHttpMetrics
app.UseHttpMetrics();

if (!app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Ticket Service API V1");
    });
}

if (!app.Environment.IsEnvironment("Docker"))
    app.UseHttpsRedirection();

app.UseCors(SharedInfrastructure.DependencyInjection.Extensions.AddCORS.PolicyName);
app.UseAuthentication();
app.UseAuthorization();
// PHẢI đứng sau hai dòng trên — xem StandardRateLimitingExtensions.UseStandardRateLimiter.
app.UseStandardRateLimiter();

app.MapControllers();
app.MapHub<TicketChatHub>("/hubs/ticket-chats").RequireAuthorization();
app.MapMetrics();
app.MapHealthChecks("/health");

app.Run();

public partial class Program { }
