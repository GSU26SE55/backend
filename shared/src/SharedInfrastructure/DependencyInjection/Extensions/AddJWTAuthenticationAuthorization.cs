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
                            errorMessage = "Your session has expired. Please log in again or refresh your token.";
                            errorCode = "TOKEN_EXPIRED";
                        }
                        else if (context.AuthenticateFailure is SecurityTokenInvalidSignatureException)
                        {
                            errorMessage = "Invalid token (signature mismatch).";
                            errorCode = "INVALID_SIGNATURE";
                        }
                        else if (context.AuthenticateFailure != null)
                        {
                            errorMessage = "Invalid token. Please log in again.";
                            errorCode = "INVALID_TOKEN";
                        }
                        else if (!context.Request.Headers.ContainsKey("Authorization"))
                        {
                            errorMessage = "Authentication information not found (missing Authorization header).";
                            errorCode = "MISSING_TOKEN";
                        }
                        else
                        {
                            errorMessage = "You are not logged in. Please provide a valid token.";
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
                            "You do not have permission to access this resource.",
                            errors: null,
                            data: new { errorCode = "FORBIDDEN" });
                    }
                };
            });
    }
}
