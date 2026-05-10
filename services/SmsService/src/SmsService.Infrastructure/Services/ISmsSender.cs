namespace SmsService.Infrastructure.Services;

public interface ISmsSender
{
    Task SendAsync(string phoneNumber, string message, CancellationToken cancellationToken = default);
}
