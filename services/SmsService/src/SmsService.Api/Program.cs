using Prometheus;
using SharedInfrastructure.Bus;
using SharedInfrastructure.DependencyInjection;
using SharedInfrastructure.Extensions;
using SharedInfrastructure.Idempotency;
using SharedInfrastructure.Middleware;
using SmsService.Infrastructure.Consumers;
using SmsService.Infrastructure.Options;
using SmsService.Infrastructure.Services;

EnvFileLoader.LoadIfExists();

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<SmsOptions>(builder.Configuration.GetSection(SmsOptions.SectionName));
builder.Services.AddSingleton<ISmsSender, FakeSmsSender>();
builder.Services.AddMessageBus(builder.Configuration, typeof(SendPhoneOtpConsumer).Assembly);

// Inbox Pattern (chống consume duplicate)
builder.Services.AddInboxIdempotency(builder.Configuration);

var app = builder.Build();

// SecurityHeaders + CorrelationId + RequestLogging + GlobalException
app.UseSharedInfrastructure();

app.UseHttpMetrics();

app.MapGet("/", () => "SMS Service is Running...");

app.MapMetrics();

app.Run();

public partial class Program { }
