using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using SmsService.Infrastructure.Security;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SmsService.Api.Swagger;

/// <summary>
/// Extend Swagger với scheme thứ 2 <c>GatewayApiKey</c> (BCrypt API key plaintext + <c>X-Device-Code</c> header)
/// dành cho Flutter app endpoints. JWT Bearer (đã add bởi <c>AddSharedSwaggerGen</c>) giữ nguyên cho admin endpoints.
/// <para>
/// Operation filter ánh xạ scheme theo <c>[Authorize(AuthenticationSchemes = ...)]</c> trên từng endpoint:
/// </para>
/// <list type="bullet">
///   <item><c>/api/sms-gateway/*</c> + <c>/hubs/sms-gateway</c> → require <c>GatewayApiKey</c> + <c>X-Device-Code</c></item>
///   <item><c>/api/admin/sms-gateway/*</c> → require <c>Bearer</c> (JWT)</item>
/// </list>
/// </summary>
public static class SmsGatewaySwaggerExtensions
{
    public const string GatewaySchemeName = "GatewayApiKey";
    public const string DeviceCodeHeader = "X-Device-Code";

    public static IServiceCollection AddSmsGatewaySwagger(this IServiceCollection services)
    {
        services.AddSwaggerGen(c =>
        {
            // 1. Scheme `GatewayApiKey` — BCrypt API key plaintext qua Authorization: Bearer <key>
            c.AddSecurityDefinition(GatewaySchemeName, new OpenApiSecurityScheme
            {
                Description = "Enter the gateway device's plaintext (BCrypt) API key — do NOT use a user JWT. "
                              + "The backend hashes and verifies it via " + nameof(BcryptGatewayApiKeyHasher) + ". "
                              + "Must be accompanied by the `" + DeviceCodeHeader + "` header (defined below).",
                Name = "Authorization",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.Http,
                Scheme = "Bearer"
            });

            // 2. Scheme `X-Device-Code` — header phụ trợ, đi kèm GatewayApiKey
            c.AddSecurityDefinition(DeviceCodeHeader, new OpenApiSecurityScheme
            {
                Description = "The gateway's device code (e.g. `android-gateway-001`). Required alongside `"
                              + GatewaySchemeName + "` when calling `/api/sms-gateway/*` or `/hubs/sms-gateway`.",
                Name = DeviceCodeHeader,
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.ApiKey
            });

            // 3. Operation filter — apply scheme đúng theo Authorize attribute trên từng endpoint.
            // Override global Bearer requirement do AddSharedSwaggerGen set: gateway endpoint chỉ cần GatewayApiKey.
            c.OperationFilter<SmsGatewayAuthOperationFilter>();

            // 4. Include XML doc — render <summary>/<remarks>/<response> trên Swagger UI.
            // Yêu cầu csproj có <GenerateDocumentationFile>true</GenerateDocumentationFile>.
            var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            if (File.Exists(xmlPath))
            {
                c.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
            }

            // Bao gồm XML doc của Application project — để render mô tả cho command/query DTO.
            var appXml = Path.Combine(AppContext.BaseDirectory, "SmsService.Application.xml");
            if (File.Exists(appXml))
            {
                c.IncludeXmlComments(appXml);
            }
        });
        return services;
    }
}

/// <summary>
/// Per-operation: đọc <c>AuthenticationSchemes</c> từ <see cref="AuthorizeAttribute"/> rồi
/// override <c>operation.Security</c> để Swagger UI hiển thị đúng scheme + cho phép gọi đúng auth header.
/// </summary>
internal sealed class SmsGatewayAuthOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var authorizeAttrs = context.ApiDescription.ActionDescriptor.EndpointMetadata
            .OfType<AuthorizeAttribute>()
            .ToList();

        if (authorizeAttrs.Count == 0)
        {
            // Endpoint không có [Authorize] → bỏ luôn security requirement (global default từ AddSharedSwaggerGen).
            operation.Security = new List<OpenApiSecurityRequirement>();
            return;
        }

        // Lấy union các scheme name khai báo trên Authorize attributes của endpoint.
        var schemes = authorizeAttrs
            .Select(a => a.AuthenticationSchemes ?? string.Empty)
            .SelectMany(s => s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var requirements = new List<OpenApiSecurityRequirement>();

        // GatewayApiKey scheme → cần kèm X-Device-Code header.
        if (schemes.Any(s => string.Equals(s, SmsGatewaySwaggerExtensions.GatewaySchemeName, StringComparison.OrdinalIgnoreCase)))
        {
            requirements.Add(new OpenApiSecurityRequirement
            {
                [Ref(SmsGatewaySwaggerExtensions.GatewaySchemeName)] = new List<string>(),
                [Ref(SmsGatewaySwaggerExtensions.DeviceCodeHeader)] = new List<string>(),
            });
        }

        // JWT Bearer scheme → admin endpoints.
        if (schemes.Any(s => string.Equals(s, JwtBearerDefaults.AuthenticationScheme, StringComparison.OrdinalIgnoreCase)))
        {
            requirements.Add(new OpenApiSecurityRequirement
            {
                [Ref("Bearer")] = new List<string>(),
            });
        }

        // Nếu controller có [Authorize] nhưng không khai báo scheme cụ thể → giữ Bearer mặc định (JWT từ AuthService).
        if (requirements.Count == 0)
        {
            requirements.Add(new OpenApiSecurityRequirement
            {
                [Ref("Bearer")] = new List<string>(),
            });
        }

        operation.Security = requirements;
    }

    private static OpenApiSecurityScheme Ref(string schemeId) => new()
    {
        Reference = new OpenApiReference
        {
            Type = ReferenceType.SecurityScheme,
            Id = schemeId
        }
    };
}
