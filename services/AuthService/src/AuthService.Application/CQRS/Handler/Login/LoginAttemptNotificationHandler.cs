using AuthService.Application.CQRS.Notification.Login;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Entities;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace AuthService.Application.CQRS.Handler.Login;

/// <summary>
/// Subscribe <see cref="LoginAttemptNotification"/> và Add entry vào DbContext.LoginAttempts.
/// KHÔNG SaveChanges — để Command Handler đang publish notification SaveChanges atomic.
/// Không throw nếu fail — login attempt log không được phá vỡ business flow.
/// </summary>
public class LoginAttemptNotificationHandler : INotificationHandler<LoginAttemptNotification>
{
    private const int MaxNoteLength = 500;
    private const int MaxUserAgentLength = 500;
    private const int MaxEmailLength = 256;

    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly ILogger<LoginAttemptNotificationHandler> _logger;

    public LoginAttemptNotificationHandler(
        IAuthUnitOfWork unitOfWork,
        ILogger<LoginAttemptNotificationHandler> logger,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task Handle(LoginAttemptNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            var (ip, userAgent, deviceId) = ClientInfoHelper.Resolve(_httpContextAccessor?.HttpContext);

            var entry = new LoginAttempt
            {
                Id = Guid.NewGuid(),
                AccountId = notification.AccountId,
                AttemptedEmail = Truncate(notification.AttemptedEmail, MaxEmailLength) ?? string.Empty,
                Result = notification.Result,
                Method = notification.Method,
                IpAddress = ip,
                UserAgent = Truncate(userAgent, MaxUserAgentLength),
                DeviceId = deviceId,
                Note = Truncate(notification.Note, MaxNoteLength)
            };

            await _unitOfWork.LoginAttempts.AddAsync(entry);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to append login attempt. AccountId={AccountId} Email={Email}",
                notification.AccountId, notification.AttemptedEmail);
        }
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }
}
