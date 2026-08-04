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

// Sprint 6.3 NOTI3-05 (#705) — consumer phụ thuộc IEmailProvider, không phụ thuộc lớp Mailjet cụ thể.
// Cắm provider thứ hai sau này chỉ là đổi dòng đăng ký này (xem R-44).
builder.Services.AddTransient<IEmailProvider>(sp => sp.GetRequiredService<EmailSenderService>());

// 2. Register Email template renderer (file-based, cached)
builder.Services.AddSingleton<IEmailTemplateRenderer, EmailTemplateRenderer>();

// 3. Register MassTransit (RabbitMQ) with Consumer Assembly
builder.Services.AddMessageBus(builder.Configuration, typeof(SendOtpRegisterConsumer).Assembly);

// 4. Inbox Pattern (chống consume duplicate)
builder.Services.AddInboxIdempotency(builder.Configuration);

// EmailService là service THUẦN TIÊU THỤ message — không có database, không có REST endpoint
// nghiệp vụ. NOTI3-03 (suppression list) từng thêm `email_db` vào đây nhưng đã được gỡ bỏ
// (quyết định 30/07/2026 — xem overall.md §17.6.3.5): giữ service stateless quan trọng hơn một
// tính năng chỉ có giá trị khi vận hành ở quy mô thật.

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
