using System.Text.RegularExpressions;
using SharedContracts.Common.Responses;

namespace AuthService.Application.Validation;

/// <summary>
/// Ràng buộc dùng chung cho các field hồ sơ account (email/full name/phone/date of birth/address).
/// Trước đây mỗi command tự copy lại luật nên FE và BE lệch nhau ở min length, định dạng phone
/// và năm sinh tối thiểu. Gom về một chỗ để FE chỉ phải khớp với đúng một nguồn.
/// </summary>
public static class AccountFieldPolicy
{
    public const int EmailMaxLength = 256;
    public const int FullNameMinLength = 2;
    public const int FullNameMaxLength = 150;
    public const int PhoneMaxLength = 20;
    public const int AddressMaxLength = 500;
    public const int MinBirthYear = 1900;

    private static readonly Regex EmailRegex = new(
        @"^[^\s@]+@[^\s@]+\.[^\s@]+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Số điện thoại di động Việt Nam: 0 + đầu số 3/5/7/8/9 + 8 chữ số.</summary>
    private static readonly Regex PhoneRegex = new(
        @"^0[35789][0-9]{8}$",
        RegexOptions.Compiled);

    public static void AddEmailErrors(ICollection<Errors> errors, string? email, string field = "Email")
    {
        if (string.IsNullOrWhiteSpace(email))
            errors.Add(new Errors { Field = field, Detail = "Email is required." });
        else if (email.Trim().Length > EmailMaxLength)
            errors.Add(new Errors { Field = field, Detail = $"Email must not exceed {EmailMaxLength} characters." });
        else if (!EmailRegex.IsMatch(email.Trim()))
            errors.Add(new Errors { Field = field, Detail = "Invalid email format." });
    }

    public static void AddFullNameErrors(ICollection<Errors> errors, string? fullName, string field = "FullName")
    {
        if (string.IsNullOrWhiteSpace(fullName))
            errors.Add(new Errors { Field = field, Detail = "Full name is required." });
        else if (fullName.Trim().Length < FullNameMinLength)
            errors.Add(new Errors { Field = field, Detail = $"Full name must be at least {FullNameMinLength} characters." });
        else if (fullName.Trim().Length > FullNameMaxLength)
            errors.Add(new Errors { Field = field, Detail = $"Full name must not exceed {FullNameMaxLength} characters." });
    }

    /// <summary>Phone là optional ở mọi command hiện tại — bỏ trống thì bỏ qua, có nhập thì phải đúng định dạng.</summary>
    public static void AddPhoneErrors(ICollection<Errors> errors, string? phoneNumber, string field = "PhoneNumber")
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            return;

        var trimmed = phoneNumber.Trim();
        if (trimmed.Length > PhoneMaxLength)
            errors.Add(new Errors { Field = field, Detail = $"Phone number must not exceed {PhoneMaxLength} characters." });
        else if (!PhoneRegex.IsMatch(trimmed))
            errors.Add(new Errors { Field = field, Detail = "Invalid phone number." });
    }

    public static void AddDateOfBirthErrors(ICollection<Errors> errors, DateTime? dateOfBirth, string field = "DateOfBirth")
    {
        if (!dateOfBirth.HasValue)
            return;

        if (dateOfBirth.Value > DateTime.UtcNow)
            errors.Add(new Errors { Field = field, Detail = "Invalid date of birth." });
        else if (dateOfBirth.Value.Year < MinBirthYear)
            errors.Add(new Errors { Field = field, Detail = "Invalid birth year." });
    }

    public static void AddAddressErrors(ICollection<Errors> errors, string? address, string field = "Address")
    {
        if (!string.IsNullOrWhiteSpace(address) && address.Trim().Length > AddressMaxLength)
            errors.Add(new Errors { Field = field, Detail = $"Address must not exceed {AddressMaxLength} characters." });
    }
}
