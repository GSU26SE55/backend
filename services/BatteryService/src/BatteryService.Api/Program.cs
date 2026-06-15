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

var isEfTooling = string.Equals(
    System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name,
    "ef",
    StringComparison.OrdinalIgnoreCase);

if (isEfTooling)
    return;

EnvFileLoader.LoadIfExists();

var builder = WebApplication.CreateBuilder(args);

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

    app.UseCors("AllowAll");

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

    app.MapControllers();
    app.MapMetrics();

    app.Run();
}

public partial class Program { }
