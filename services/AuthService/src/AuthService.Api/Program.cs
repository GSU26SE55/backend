using AuthService.Api.Extensions;
using AuthService.Infrastructure.DependencyInjection;
using AuthService.Infrastructure.Persistence;
using AuthService.Infrastructure.Persistence.Seeders;
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
if (!app.Environment.IsEnvironment("Docker"))
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
