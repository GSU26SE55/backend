using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using SharedInfrastructure.Middleware;

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
                    // Sprint BE-IoT-Realtime — SSE: EventSource native không set được header Authorization.
                    // Cho phép token qua query ?access_token= CHỈ cho endpoint stream (path chứa "/stream").
                    OnMessageReceived = context =>
                    {
                        if (string.IsNullOrEmpty(context.Token))
                        {
                            var accessToken = context.Request.Query["access_token"].ToString();
                            var path = context.HttpContext.Request.Path;
                            if (!string.IsNullOrEmpty(accessToken)
                                && path.HasValue
                                && path.Value!.Contains("/stream", StringComparison.OrdinalIgnoreCase))
                            {
                                context.Token = accessToken;
                            }
                        }
                        return Task.CompletedTask;
                    },

                    OnAuthenticationFailed = context =>
                    {
                        if (context.Exception.GetType() == typeof(SecurityTokenExpiredException))
                            context.Response.Headers.Append("Token-Expired", "true");
                        return Task.CompletedTask;
                    },

                    // 401 Unauthorized — chưa đăng nhập / token sai / token hết hạn.
                    OnChallenge = async context =>
                    {
                        // Ngăn behavior mặc định (trả body rỗng).
                        context.HandleResponse();

                        string errorMessage;
                        string errorCode;

                        if (context.AuthenticateFailure is SecurityTokenExpiredException)
                        {
                            errorMessage = "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại hoặc làm mới token.";
                            errorCode = "TOKEN_EXPIRED";
                        }
                        else if (context.AuthenticateFailure is SecurityTokenInvalidSignatureException)
                        {
                            errorMessage = "Token không hợp lệ (chữ ký không đúng).";
                            errorCode = "INVALID_SIGNATURE";
                        }
                        else if (context.AuthenticateFailure != null)
                        {
                            errorMessage = "Token không hợp lệ. Vui lòng đăng nhập lại.";
                            errorCode = "INVALID_TOKEN";
                        }
                        else if (!context.Request.Headers.ContainsKey("Authorization"))
                        {
                            errorMessage = "Không tìm thấy thông tin xác thực (thiếu Authorization header).";
                            errorCode = "MISSING_TOKEN";
                        }
                        else
                        {
                            errorMessage = "Bạn chưa đăng nhập. Vui lòng cung cấp token hợp lệ.";
                            errorCode = "UNAUTHORIZED";
                        }

                        await CommonResponseWriter.WriteAsync(
                            context.Response,
                            StatusCodes.Status401Unauthorized,
                            errorMessage,
                            errors: null,
                            data: new { errorCode });
                    },

                    // 403 Forbidden — đã đăng nhập nhưng không đủ quyền.
                    OnForbidden = async context =>
                    {
                        await CommonResponseWriter.WriteAsync(
                            context.Response,
                            StatusCodes.Status403Forbidden,
                            "Bạn không có quyền truy cập tài nguyên này.",
                            errors: null,
                            data: new { errorCode = "FORBIDDEN" });
                    }
                };
            });
    }
}
