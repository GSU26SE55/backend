using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;

namespace SharedInfrastructure.Swagger;

public static class SwaggerExtensions
{
    public static void AddSharedSwaggerGen(this IServiceCollection services, string apiTitle, string apiVersion = "v1")
    {
        services.AddSwaggerGen(c =>
        {
            // 1. Định nghĩa thông tin API cơ bản
            c.SwaggerDoc(apiVersion, new OpenApiInfo
            {
                Title = apiTitle,
                Version = apiVersion
            });

            // 2. CẤU HÌNH NÚT AUTHORIZE (Ổ KHÓA) - Dùng chung cho tất cả Service
            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Description = "Nhập token JWT vào bên dưới (Không cần gõ 'Bearer '):",
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

            // 3. Fix lỗi trùng tên Schema (nếu có)
            c.CustomSchemaIds(type => type.FullName);
        });
    }
}
