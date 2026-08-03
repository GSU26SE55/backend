using Microsoft.EntityFrameworkCore;
using NotificationService.Infrastructure.DependencyInjection;
using NotificationService.Infrastructure.Persistence;
using NotificationService.Infrastructure.Realtime;
using Prometheus;
using SharedInfrastructure.DependencyInjection;
using SharedInfrastructure.Extensions;
using SharedInfrastructure.Idempotency;

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

builder.Services.AddNotificationServiceInfrastructure(builder.Configuration);
builder.Services.AddIdempotencyKey(builder.Configuration);

// Sprint 6.3 NOTI3-13 (#713) — realtime feed in-app. Polling REST vẫn giữ nguyên làm đường dự phòng.
builder.Services.AddSignalR();

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

    var seeder = scope.ServiceProvider.GetRequiredService<NotificationService.Infrastructure.Persistence.Seeders.NotificationDataSeeder>();
    await seeder.SeedAsync();
    Console.WriteLine("Notification seed data checked.");
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
app.UseIdempotencyKey();

app.MapControllers();

// Sprint 6.3 NOTI3-13 (#713) — bắt buộc đăng nhập: hub phát thông báo riêng của từng người.
app.MapHub<NotificationHub>("/hubs/notifications").RequireAuthorization();

app.MapMetrics();

app.Run();

public partial class Program { }
