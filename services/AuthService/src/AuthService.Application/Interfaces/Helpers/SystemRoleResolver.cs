using AuthService.Application.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Application.Interfaces.Helpers;

/// <summary>
/// Resolve Id của system role theo <c>Role.NormalizedName</c> tại runtime.
///
/// Lý do tồn tại: migration <c>RemoveRoleHasDataSeed</c> đã bỏ seed role với GUID cố định;
/// <c>AuthDataSeeder</c> giờ seed system roles bằng <c>Guid.NewGuid()</c>. Vì vậy handler KHÔNG
/// được hardcode GUID role (vd "44444444-4444-4444-4444-444444444444") — GUID hardcode sẽ KHÔNG
/// khớp role thực trong DB, khiến INSERT/UPDATE Account vi phạm foreign key (PostgreSQL 23503)
/// và trả lỗi 500. Luôn lookup theo NormalizedName ổn định ("CUSTOMER", "ADMIN", ...).
/// </summary>
public static class SystemRoleResolver
{
    public const string CustomerNormalizedName = "CUSTOMER";

    /// <summary>
    /// Trả về Id của system role theo <paramref name="normalizedName"/>, hoặc <c>null</c> nếu role
    /// chưa tồn tại trong DB (chưa seed). Caller chịu trách nhiệm xử lý trường hợp null.
    /// </summary>
    public static async Task<Guid?> ResolveRoleIdAsync(
        IAuthUnitOfWork unitOfWork,
        string normalizedName,
        CancellationToken cancellationToken = default)
    {
        var id = await unitOfWork.Roles
            .GetAllAsync()
            .Where(r => r.NormalizedName == normalizedName && !r.IsDeleted)
            .Select(r => r.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return id == Guid.Empty ? null : id;
    }

    /// <summary>Shortcut resolve Customer role — default role cho self-register / Google sign-up.</summary>
    public static Task<Guid?> ResolveCustomerRoleIdAsync(
        IAuthUnitOfWork unitOfWork,
        CancellationToken cancellationToken = default)
        => ResolveRoleIdAsync(unitOfWork, CustomerNormalizedName, cancellationToken);
}
