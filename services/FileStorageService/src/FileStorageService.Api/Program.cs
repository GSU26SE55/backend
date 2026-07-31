using FileStorageService.Application.DependencyInjection;
using FileStorageService.Infrastructure.DependencyInjection;
using FileStorageService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Prometheus;
using SharedInfrastructure.DependencyInjection;
using SharedInfrastructure.Extensions;

EnvFileLoader.LoadIfExists();

var builder = WebApplication.CreateBuilder(args);
var grpcPort = builder.Configuration.GetValue<int?>("FILE_STORAGE_SERVICE_GRPC_SERVER_PORT")
    ?? builder.Configuration.GetValue<int?>("Grpc:Port")
    ?? throw new InvalidOperationException("FILE_STORAGE_SERVICE_GRPC_SERVER_PORT (or Grpc:Port) must be configured.");
if (grpcPort == 8080)
    throw new InvalidOperationException("Grpc:Port must differ from HTTP port 8080.");

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(8080, listen => listen.Protocols = HttpProtocols.Http1);
    options.ListenAnyIP(grpcPort, listen => listen.Protocols = HttpProtocols.Http2);
});

builder.Services.AddControllers();
builder.Services.AddGrpc();
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
builder.Services.AddFileStorageApplication();
builder.Services.AddFileStorageInfrastructure(builder.Configuration);
builder.Services.AddSharedInfrastructure(builder.Configuration, "FileStorageService.Application", "File Storage Service API");

// Sprint audit #AUDIT-29 — thêm MassTransit (FileStorage chưa có) cho audit pipeline + relay.
SharedInfrastructure.Bus.MassTransitExtensions.AddMessageBus(
    builder.Services, builder.Configuration,
    typeof(FileStorageService.Infrastructure.BackgroundJobs.FileAuditOutboxRelayBackgroundService).Assembly);
builder.Services.AddHostedService<FileStorageService.Infrastructure.BackgroundJobs.FileAuditOutboxRelayBackgroundService>();

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
}

// SecurityHeaders + CorrelationId + RequestLogging + GlobalException
app.UseSharedInfrastructure();

app.UseHttpMetrics();

if (!app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!app.Environment.IsEnvironment("Docker")
    && !builder.Configuration.GetValue("DisableHttpsRedirection", false))
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGrpcService<FileStorageService.Api.Grpc.FileInternalGrpcService>();

app.MapMetrics();

app.Run();

public partial class Program { }
