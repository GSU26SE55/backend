using System.Security.Cryptography;
using System.Text;
using AuthService.Application.Interfaces.Services;
using BCrypt.Net;

namespace AuthService.Infrastructure.Implements.Services;

public class BackupCodeGenerator : IBackupCodeGenerator
{
    // Bỏ ký tự dễ nhầm: 0/o/O, 1/l/L, i/I — chỉ giữ ký tự rõ ràng.
    private const string Alphabet = "abcdefghjkmnpqrstuvwxyz23456789";
    private const int CodeLength = 8;        // 8 ký tự + 1 dash giữa = 9 ký tự hiển thị
    private const int DashPosition = 4;
    private const int BcryptWorkFactor = 12; // #AUTH-01: align với PasswordHasher.WorkFactor (12) cho nhất quán cost factor.

    public IReadOnlyList<string> Generate(int count = 8)
    {
        if (count <= 0 || count > 50)
            throw new ArgumentOutOfRangeException(nameof(count));
        var list = new List<string>(count);
        for (var i = 0; i < count; i++)
            list.Add(GenerateOne());
        return list;
    }

    public string Hash(string plainCode)
    {
        var normalized = Normalize(plainCode);
        if (string.IsNullOrEmpty(normalized))
            throw new ArgumentException("plainCode required", nameof(plainCode));
        return BCrypt.Net.BCrypt.HashPassword(normalized, BcryptWorkFactor);
    }

    public bool Verify(string plainCode, string hash)
    {
        if (string.IsNullOrEmpty(plainCode) || string.IsNullOrEmpty(hash))
            return false;
        var normalized = Normalize(plainCode);
        if (string.IsNullOrEmpty(normalized))
            return false;
        try
        { return BCrypt.Net.BCrypt.Verify(normalized, hash); }
        catch (SaltParseException) { return false; }
    }

    public string Normalize(string plainCode)
    {
        if (string.IsNullOrEmpty(plainCode))
            return string.Empty;
        var sb = new StringBuilder(plainCode.Length);
        foreach (var c in plainCode)
        {
            if (c == '-' || char.IsWhiteSpace(c))
                continue;
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    private static string GenerateOne()
    {
        var sb = new StringBuilder(CodeLength + 1);
        for (var i = 0; i < CodeLength; i++)
        {
            if (i == DashPosition)
                sb.Append('-');
            sb.Append(Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]);
        }
        return sb.ToString();
    }
}
