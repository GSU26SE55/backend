using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmsService.Infrastructure.Options;

namespace SmsService.Infrastructure.Services;

public class FakeSmsSender : ISmsSender
{
    private readonly ILogger<FakeSmsSender> _logger;
    private readonly SmsOptions _options;

    public FakeSmsSender(ILogger<FakeSmsSender> logger, IOptions<SmsOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public Task SendAsync(string phoneNumber, string message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            throw new ArgumentException("Phone number is required.", nameof(phoneNumber));
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("SMS message is required.", nameof(message));

        _logger.LogInformation(
            "[FAKE SMS] Provider={Provider}, From={From}, To={PhoneNumber}, Message={Message}",
            _options.Provider,
            _options.From,
            phoneNumber,
            message);

        return Task.CompletedTask;
    }
}
