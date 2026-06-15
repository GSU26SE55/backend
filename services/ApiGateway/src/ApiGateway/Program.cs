using Microsoft.OpenApi.Models;
using Prometheus;
using SharedInfrastructure.Extensions;
using SharedInfrastructure.Middleware;

EnvFileLoader.LoadIfExists();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("auth", new OpenApiInfo { Title = "Auth Service API", Version = "v1" });
    c.SwaggerDoc("filestorage", new OpenApiInfo { Title = "File Storage Service API", Version = "v1" });
    c.SwaggerDoc("battery", new OpenApiInfo { Title = "Battery Service API", Version = "v1" });
    c.SwaggerDoc("ticket", new OpenApiInfo { Title = "Ticket Service API", Version = "v1" });
    c.SwaggerDoc("notification", new OpenApiInfo { Title = "Notification Service API", Version = "v1" });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Nhập token theo định dạng: Bearer {token}",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] { }
        }
    });

    c.CustomSchemaIds(type => type.FullName);
});

var app = builder.Build();

// SecurityHeaders + CorrelationId + RequestLogging — đặt sớm nhất để mọi route đều có.
// Gateway forward Correlation ID xuống upstream service qua YARP (header pass-through tự nhiên).
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

// Prometheus HTTP metrics — track latency, status code, route distribution at gateway layer.
app.UseHttpMetrics();

// Bật Swagger UI cho mọi non-Production environment (Development, Docker, Staging...).
// Production thực sự nên tắt để giảm attack surface.
if (!app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/auth-service/swagger/v1/swagger.json", "Auth Service API");
        options.SwaggerEndpoint("/file-storage-service/swagger/v1/swagger.json", "File Storage Service API");
        options.SwaggerEndpoint("/battery-service/swagger/v1/swagger.json", "Battery Service API");
        options.SwaggerEndpoint("/ticket-service/swagger/v1/swagger.json", "Ticket Service API");
        options.SwaggerEndpoint("/notification-service/swagger/v1/swagger.json", "Notification Service API");
    });
}

// HTTPS redirect chỉ áp dụng khi gateway listen HTTPS.
// Trong Docker (HTTP-only), bỏ qua để tránh redirect loop.
if (!app.Environment.IsEnvironment("Docker")
    && !builder.Configuration.GetValue("DisableHttpsRedirection", false))
{
    app.UseHttpsRedirection();
}

//app.UseWebSockets();

app.UseCors("AllowAll");

// Bọc mọi response status-only (404/401/403/405/...) thành CommonResponse để client luôn parse cùng schema.
// Phải đặt TRƯỚC MapReverseProxy/MapMetrics để bắt được cả 404 do YARP không match route lẫn 404 do controller.
app.UseCommonResponseStatusCodes();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));
app.MapReverseProxy();

// Expose /metrics cho Prometheus scrape — đặt sau MapReverseProxy để không bị YARP route capture.
app.MapMetrics();

app.Run();
