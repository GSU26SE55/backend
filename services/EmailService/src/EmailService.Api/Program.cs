using EmailService.Infrastructure.Consumers;
using EmailService.Infrastructure.Services;
using EmailService.Infrastructure.Templates;
using Prometheus;
using SharedInfrastructure.Bus;
using SharedInfrastructure.DependencyInjection;
using SharedInfrastructure.Extensions;
using SharedInfrastructure.Idempotency;
using SharedInfrastructure.Middleware;

EnvFileLoader.LoadIfExists();

var builder = WebApplication.CreateBuilder(args);

// 1. Register EmailSender (MailJet via HttpClient)
builder.Services.AddHttpClient<EmailSenderService>();

// 2. Register Email template renderer (file-based, cached)
builder.Services.AddSingleton<IEmailTemplateRenderer, EmailTemplateRenderer>();

// 3. Register MassTransit (RabbitMQ) with Consumer Assembly
builder.Services.AddMessageBus(builder.Configuration, typeof(SendOtpRegisterConsumer).Assembly);

// 4. Inbox Pattern (chống consume duplicate)
builder.Services.AddInboxIdempotency(builder.Configuration);

var app = builder.Build();

// SecurityHeaders + CorrelationId + RequestLogging + GlobalException
app.UseSharedInfrastructure();

// Prometheus HTTP metrics
app.UseHttpMetrics();

app.MapGet("/", () => "Email Service is Running...");

// Expose /metrics cho Prometheus scrape
app.MapMetrics();

app.Run();

// Marker để WebApplicationFactory<Program> trong integration tests reference được Program class.
public partial class Program { }
