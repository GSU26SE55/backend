namespace SmsService.Application.Interfaces.Services;

/// <summary>
/// Abstraction hash + verify API key plaintext của gateway device.
/// Infrastructure impl <c>BcryptGatewayApiKeyHasher</c> dùng BCrypt workFactor 11.
/// </summary>
public interface IGatewayApiKeyHasher
{
    string Hash(string apiKey);
    bool Verify(string apiKey, string hash);
}
