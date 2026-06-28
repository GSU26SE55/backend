using Microsoft.EntityFrameworkCore;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;

namespace NotificationService.Infrastructure.Services;

/// <summary>
/// GH-604 — Resolve recipient theo role từ read-model account. Query đọc-thuần
/// qua <see cref="INotificationUnitOfWork"/> (không SaveChanges). LUÔN filter
/// <c>!IsDeleted</c> (dự án không dùng global query filter).
/// </summary>
public class RecipientResolver : IRecipientResolver
{
    private readonly INotificationUnitOfWork _unitOfWork;

    public RecipientResolver(INotificationUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<IReadOnlyList<Guid>> GetActiveByRoleAsync(
        CancellationToken cancellationToken, params string[] roles)
    {
        if (roles is null || roles.Length == 0)
            return Array.Empty<Guid>();

        var normalized = roles
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim().ToLower())
            .Distinct()
            .ToArray();

        if (normalized.Length == 0)
            return Array.Empty<Guid>();

        return await _unitOfWork.Accounts.GetAllAsync()
            .Where(x => !x.IsDeleted && x.IsActive && normalized.Contains(x.Role.ToLower()))
            .Select(x => x.Id)
            .Distinct()
            .ToListAsync(cancellationToken);
    }
}
