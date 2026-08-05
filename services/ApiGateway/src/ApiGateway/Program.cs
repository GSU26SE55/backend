using ApiGateway.Extensions;
using Microsoft.OpenApi.Models;
using Prometheus;
using SharedInfrastructure.DependencyInjection.Extensions;
using SharedInfrastructure.Extensions;
using SharedInfrastructure.Middleware;

EnvFileLoader.LoadIfExists();

var builder = WebApplication.CreateBuilder(args);

// #AUTH-05 — dùng CHUNG policy với 7 service phía sau thay vì tự khai `AllowAll` tại chỗ.
//
// Gateway là CỬA TRƯỚC của hệ thống: nó mà mở cho mọi origin thì việc siết whitelist ở downstream
// gần như vô nghĩa với trình duyệt. Trước đây khối này đăng ký policy tên "AllowAll" trong khi
// `app.UseCors(...)` bên dưới lại yêu cầu policy tên "AppCors" — tên không khớp thì
// `CorsMiddleware` ném `InvalidOperationException: The CORS policy 'AppCors' was not found`
// trên MỌI request. Gọi `AddCorsExtentions` khép lại cả hai vấn đề: đúng tên, đúng whitelist.
//
// ApiGateway KHÔNG gọi `AddSharedInfrastructure` (không cần MediatR/EF/Swagger dùng chung) nên
// phải gọi thẳng ở đây — bỏ dòng này là gateway chết ngay request đầu tiên.
builder.Services.AddCorsExtentions(builder.Configuration, builder.Environment);

// Sprint 7 #115 — validate JWT tại gateway (cùng JwtSettings với downstream) + forward claim.
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddAuthorization();

// Sprint 7 #115 — rate limiting (fixed-window theo userId/IP).
builder.Services.AddGatewayRateLimiting(builder.Configuration);

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddGatewayClaimForwarding();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("auth", new OpenApiInfo { Title = "Auth Service API", Version = "v1" });
    c.SwaggerDoc("filestorage", new OpenApiInfo { Title = "File Storage Service API", Version = "v1" });
    c.SwaggerDoc("battery", new OpenApiInfo { Title = "Battery Service API", Version = "v1" });
    c.SwaggerDoc("ticket", new OpenApiInfo { Title = "Ticket Service API", Version = "v1" });
    c.SwaggerDoc("notification", new OpenApiInfo { Title = "Notification Service API", Version = "v1" });
    c.SwaggerDoc("sms", new OpenApiInfo { Title = "SMS Service API", Version = "v1" });
    c.SwaggerDoc("auditaggregator", new OpenApiInfo { Title = "Audit Aggregator Service API", Version = "v1" });

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

// GH-800 — PHẢI đứng ngay SAU UseHttpMetrics, tức nằm BÊN TRONG nó.
// `http_requests_received_total` đọc Response.StatusCode sau khi phần còn lại của pipeline chạy
// xong; đặt ở đây thì trạng thái đã được chuẩn hoá trước lúc bộ đếm nhìn vào. Đặt trước
// UseHttpMetrics là bộ đếm vẫn ghi 502 và dashboard vẫn báo 5xx giả mỗi lần client đóng luồng SSE.
app.UseMiddleware<ClientDisconnectStatusMiddleware>();

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
        options.SwaggerEndpoint("/sms-service/swagger/v1/swagger.json", "SMS Service API");
        options.SwaggerEndpoint("/audit-aggregator-service/swagger/v1/swagger.json", "Audit Aggregator Service API");
    });
}

// HTTPS redirect chỉ áp dụng khi gateway listen HTTPS.
// Trong Docker (HTTP-only), bỏ qua để tránh redirect loop.
if (!app.Environment.IsEnvironment("Docker")
    && !builder.Configuration.GetValue("DisableHttpsRedirection", false))
{
    app.UseHttpsRedirection();
}

// WebSockets BẮT BUỘC cho SignalR Hub forwarding (SmsService /hubs/sms-gateway).
// YARP forward WebSocket upgrade headers tự động, nhưng app.UseWebSockets() phải enable
// để middleware pipeline chấp nhận upgrade từ Connection: Upgrade + Upgrade: websocket.
app.UseWebSockets();

app.UseCors(SharedInfrastructure.DependencyInjection.Extensions.AddCORS.PolicyName);

// Sprint 7 #115 — validate JWT (populate HttpContext.User cho claim forwarding + rate-limit partition).
// KHÔNG RequireAuthorization trên proxy route → request ẩn danh vẫn pass (downstream tự authorize);
// gateway chỉ parse token nếu có để forward claim.
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// Bọc mọi response status-only (404/401/403/405/...) thành CommonResponse để client luôn parse cùng schema.
// Phải đặt TRƯỚC MapReverseProxy/MapMetrics để bắt được cả 404 do YARP không match route lẫn 404 do controller.
app.UseCommonResponseStatusCodes();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));
app.MapReverseProxy();

// Expose /metrics cho Prometheus scrape — đặt sau MapReverseProxy để không bị YARP route capture.
app.MapMetrics();

app.Run();
