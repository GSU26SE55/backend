using SmsService.Application.Interfaces.Services;

namespace SmsService.Infrastructure.Security;

/// <summary>
/// BCrypt impl của <see cref="IGatewayApiKeyHasher"/>.
/// workFactor=11 đủ chi phí brute-force cho 1 secret 32 bytes random base64.
/// </summary>
public class BcryptGatewayApiKeyHasher : IGatewayApiKeyHasher
{
    private const int WorkFactor = 11;

    public string Hash(string apiKey) => BCrypt.Net.BCrypt.HashPassword(apiKey, WorkFactor);

    public bool Verify(string apiKey, string hash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(apiKey, hash);
        }
        catch
        {
            // Hash format invalid → trả false thay vì throw 500 lên client.
            return false;
        }
    }
}
