using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SharedInfrastructure.Services;
using SharedKernels.Domain;

namespace SharedInfrastructure.Persistence.Interceptors;

public class AuditableEntityInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUserService _currentUserService;
    public AuditableEntityInterceptor(ICurrentUserService currentUserService)
    {
        _currentUserService = currentUserService;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        UpdateEntities(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        UpdateEntities(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void UpdateEntities(DbContext? context)
    {
        if (context == null)
            return;

        foreach (var entry in context.ChangeTracker.Entries<AuditableEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = DateTime.UtcNow;
                string? currentUserId = _currentUserService.UserId;
                if (Guid.TryParse(currentUserId, out var uid))
                {
                    Console.WriteLine($"[AuditableEntityInterceptor] User found: {uid}. Setting CreatedBy.");
                    entry.Entity.CreatedBy = uid;
                }
                else
                {
                    Console.WriteLine($"[AuditableEntityInterceptor] No user found (UserId: '{currentUserId}'). Setting CreatedBy to Guid.Empty.");
                    entry.Entity.CreatedBy = Guid.Empty;
                }
            }

            if (entry.State == EntityState.Deleted)
            {
                if (entry.Entity is IHardDeleteEntity)
                {
                    return;
                }
                entry.State = EntityState.Modified;
                entry.Entity.IsDeleted = true;
                entry.Entity.DeletedAt = DateTime.UtcNow;
            }

            if (entry.State == EntityState.Modified || entry.HasChangedOwnedEntities())
            {
                entry.Entity.UpdatedAt = DateTime.UtcNow;
                //entry.Entity.LastModifiedBy = _currentUserService.UserId;
            }
        }
    }
}

public static class Extensions
{
    public static bool HasChangedOwnedEntities(this EntityEntry entry) =>
    entry.References.Any(r =>
        r.TargetEntry != null &&
        r.TargetEntry.Metadata.IsOwned() &&
        (r.TargetEntry.State == EntityState.Added || r.TargetEntry.State == EntityState.Modified));
}
