using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace SharedInfrastructure.DependencyInjection.Extensions;

public static class AddJwtAuthenticationAuthorization
{
    public static void AddJwtAuthentication(this IServiceCollection service, IConfiguration configuration)
    {
        var jwtSettings = configuration.GetSection("JwtSettings");
        var key = Encoding.ASCII.GetBytes(jwtSettings["SecretKey"]!);

        service.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.RequireHttpsMetadata = false;
                options.SaveToken = true;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateIssuer = true,
                    ValidIssuer = jwtSettings["Issuer"],
                    ValidateAudience = true,
                    ValidAudience = jwtSettings["Audience"],
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero,

                };
                options.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        if (context.Exception.GetType() == typeof(SecurityTokenExpiredException))
                            context.Response.Headers.Append("Token-Expired", "true");
                        return Task.CompletedTask;
                    },
                    // 1. Xử lý khi chưa đăng nhập hoặc Token sai (401 Unauthorized)
                    OnChallenge = context =>
                    {
                        // Ngăn chặn hành vi mặc định (trả về rỗng)
                        context.HandleResponse();

                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        context.Response.ContentType = "application/json";

                        string errorMessage = "Bạn chưa đăng nhập. Vui lòng cung cấp Token hợp lệ.";
                        string errorCode = "UNAUTHORIZED";

                        // 2. Phân tích chi tiết nguyên nhân lỗi
                        if (context.AuthenticateFailure != null)
                        {
                            if (context.AuthenticateFailure is SecurityTokenExpiredException)
                            {
                                errorMessage = "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại hoặc làm mới Token.";
                                errorCode = "TOKEN_EXPIRED";
                            }
                            else if (context.AuthenticateFailure is SecurityTokenInvalidSignatureException)
                            {
                                errorMessage = "Token không hợp lệ (Chữ ký bị sai).";
                                errorCode = "INVALID_SIGNATURE";
                            }
                            else
                            {
                                errorMessage = "Token không hợp lệ. Vui lòng đăng nhập lại.";
                                errorCode = "INVALID_TOKEN";
                            }
                        }
                        // Trường hợp không có header Authorization
                        else if (!context.Request.Headers.ContainsKey("Authorization"))
                        {
                            errorMessage = "Không tìm thấy thông tin xác thực (Missing Authorization Header).";
                            errorCode = "MISSING_TOKEN";
                        }

                        var response = new
                        {
                            isSuccess = false,
                            statusCode = 401,
                            message = errorMessage,
                            data = new { errorCode },
                            listErrors = Array.Empty<object>()
                        };

                        return context.Response.WriteAsync(JsonSerializer.Serialize(response));
                    },

                    // 2. Xử lý khi đã đăng nhập nhưng không đủ quyền (403 Forbidden)
                    OnForbidden = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        context.Response.ContentType = "application/json";

                        var response = new
                        {
                            isSuccess = false,
                            message = "You are not allowed to access this endpoint.",
                            data = (object?)null,
                            listErrors = Array.Empty<object>()
                        };

                        return context.Response.WriteAsync(JsonSerializer.Serialize(response));
                    }
                };
            });
    }
}
