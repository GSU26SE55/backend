using System.Security.Cryptography;
using System.Text;
using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.Interfaces.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SharedContracts.Common.Responses;

namespace AuthService.Application.CQRS.Handler.Auth;

public class Enable2FACommandHandler : IRequestHandler<Enable2FACommand, CommonResponse<TwoFactorSecretDto>>
{
    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IConfiguration _configuration;

    public Enable2FACommandHandler(IAuthUnitOfWork unitOfWork, IConfiguration configuration)
    {
        _unitOfWork = unitOfWork;
        _configuration = configuration;
    }

    public async Task<CommonResponse<TwoFactorSecretDto>> Handle(Enable2FACommand request, CancellationToken cancellationToken)
    {
        var account = await _unitOfWork.Accounts
            .GetAllAsync()
            .FirstOrDefaultAsync(a => a.Id == request.AccountId && !a.IsDeleted, cancellationToken);
        if (account == null)
            return Fail(404, "Không tìm thấy tài khoản.");

        if (account.TwoFactorEnabled && !string.IsNullOrEmpty(account.TwoFactorSecret))
            return Fail(409, "2FA đã được bật trên tài khoản này.");

        var secret = GenerateBase32Secret(20);
        account.TwoFactorSecret = secret;
        account.TwoFactorEnabled = true;
        _unitOfWork.Accounts.UpdateAsync(account);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var issuer = _configuration["JwtSettings:Issuer"] ?? "AuthService";
        var label = $"{issuer}:{account.Email}";
        var otpAuthUri = $"otpauth://totp/{Uri.EscapeDataString(label)}?secret={secret}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits=6&period=30";

        return new CommonResponse<TwoFactorSecretDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Bật 2FA thành công. Quét QR bằng ứng dụng Authenticator.",
            Data = new TwoFactorSecretDto
            {
                Secret = secret,
                OtpAuthUri = otpAuthUri
            }
        };
    }

    private static string GenerateBase32Secret(int byteLength)
    {
        const string base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new byte[byteLength];
        RandomNumberGenerator.Fill(bytes);

        var sb = new StringBuilder();
        var bitBuffer = 0;
        var bitCount = 0;
        foreach (var b in bytes)
        {
            bitBuffer = (bitBuffer << 8) | b;
            bitCount += 8;
            while (bitCount >= 5)
            {
                bitCount -= 5;
                var index = (bitBuffer >> bitCount) & 0x1F;
                sb.Append(base32Alphabet[index]);
            }
        }
        if (bitCount > 0)
        {
            var index = (bitBuffer << (5 - bitCount)) & 0x1F;
            sb.Append(base32Alphabet[index]);
        }
        return sb.ToString();
    }

    private static CommonResponse<TwoFactorSecretDto> Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message,
        ListErrors = { new Errors { Field = "TwoFactor", Detail = message } }
    };
}
