using Microsoft.EntityFrameworkCore;
using Prometheus;
using SharedInfrastructure.DependencyInjection;
using SharedInfrastructure.Extensions;
using TicketService.Application.DependencyInjection;
using TicketService.Infrastructure.DependencyInjection;
using TicketService.Infrastructure.Persistence;
using TicketService.Infrastructure.Persistence.Seeders;

EnvFileLoader.LoadIfExists();

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
        options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
});

builder.Services.AddTicketServiceApplication();
builder.Services.AddTicketServiceInfrastructure(builder.Configuration);

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

    if (pending.Any())
    {
        Console.WriteLine("[TicketService] Running database migrations...");
        db.Database.Migrate();
        Console.WriteLine("[TicketService] Migration completed.");
    }
    else
    {
        Console.WriteLine("[TicketService] No pending migrations.");
    }

    var seeder = scope.ServiceProvider.GetRequiredService<TicketDataSeeder>();
    await seeder.SeedAsync();
    Console.WriteLine("[TicketService] Seed data checked.");
}

app.UseSharedInfrastructure();

// Prometheus HTTP metrics
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

app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapMetrics();
app.MapHealthChecks("/health");

app.Run();

public partial class Program { }
