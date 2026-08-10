using System.Threading.RateLimiting;
using AuditAggregatorService.Infrastructure.DependencyInjection;
using AuditAggregatorService.Infrastructure.Persistence;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Prometheus;
using SharedInfrastructure.DependencyInjection;
using SharedInfrastructure.Extensions;
using SharedInfrastructure.RateLimiting;

EnvFileLoader.LoadIfExists();

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.AddAuditAggregatorServiceInfrastructure(builder.Configuration);

// Hạn mức nền cho mọi endpoint (60 req/30s ẩn danh · 500 req/30s đã đăng nhập).
builder.Services.AddStandardRateLimiting(builder.Configuration);

// #AUDIT-18 — rate limit "audit": Admin 200 req/min (fixed window). Chạy chồng lên hạn mức nền.
// KHÔNG đặt OnRejected/RejectionStatusCode ở đây: xem StandardRateLimitingExtensions.
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("audit", o =>
    {
        o.Window = TimeSpan.FromMinutes(1);
        o.PermitLimit = 200;
        o.QueueLimit = 0;
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AuditAggregateDbContext>();
    var pending = db.Database.GetPendingMigrations().ToList();
    Console.WriteLine($"[AuditAggregator] Pending migrations: {pending.Count}");
    if (pending.Any())
    {
        db.Database.Migrate();
        Console.WriteLine("[AuditAggregator] Migration completed.");
    }
}

app.UseSharedInfrastructure();
app.UseHttpMetrics();

if (!app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Audit Aggregator Service API");
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

app.MapControllers();
app.MapMetrics();

app.Run();

public partial class Program { }
