using System.Text.Json;
using AuthService.Application.CQRS.Command.Admin;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Application.Interfaces.Services;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Handler.Admin;

/// <summary>
/// #AUTH-47: Merge secondary account vào primary account.
///
/// Implementation chi tiết:
/// 1. Load 2 account (verify exist, không deleted, chưa từng merge).
/// 2. Conflict resolution: primary thắng (giữ email/phone/fullName), secondary attributes copy
///    sang primary CHỈ KHI primary field null (GoogleId, AccountProfile, StaffProfile).
/// 3. Revoke tất cả RefreshToken active của secondary (force logout).
/// 4. Snapshot secondary trước khi tombstone → JSON lưu AccountMergeLog.
/// 5. Soft-delete secondary + MergedIntoId = primary.Id.
/// 6. Insert AccountMergeLog audit row.
/// 7. Publish AccountMerged audit notification + meta-audit cho secondary's AccountDeleted.
/// </summary>
public class MergeAccountCommandHandler : IRequestHandler<MergeAccountCommand, AccountActionResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IPublisher _publisher;
    private readonly ITokenRevocationStore _revocationStore;
    private readonly IMessageProducerService _messageProducer;

    public MergeAccountCommandHandler(
        IAuthUnitOfWork unitOfWork,
        IPublisher publisher,
        ITokenRevocationStore revocationStore,
        IMessageProducerService messageProducer)
    {
        _unitOfWork = unitOfWork;
        _publisher = publisher;
        _revocationStore = revocationStore;
        _messageProducer = messageProducer;
    }

    public async Task<AccountActionResponse> Handle(MergeAccountCommand request, CancellationToken cancellationToken)
    {
        var primary = await _unitOfWork.Accounts.GetAllAsync()
            .Include(a => a.Profile)
            .Include(a => a.StaffProfile)
            .FirstOrDefaultAsync(a => a.Id == request.PrimaryAccountId, cancellationToken);
        if (primary == null || primary.IsDeleted)
            return Fail(404, "Primary account does not exist or has been deleted.");

        if (primary.MergedIntoId != null)
            return Fail(409, "Primary account has already been merged into another account.");

        var secondary = await _unitOfWork.Accounts.GetAllAsync()
            .Include(a => a.Profile)
            .Include(a => a.StaffProfile)
            .FirstOrDefaultAsync(a => a.Id == request.SecondaryAccountId, cancellationToken);
        if (secondary == null || secondary.IsDeleted)
            return Fail(404, "Secondary account does not exist or has been deleted.");

        if (secondary.MergedIntoId != null)
            return Fail(409, "Secondary account has already been merged previously.");

        var now = DateTime.UtcNow;
        var conflictResolution = new Dictionary<string, object?>();
        var secondarySnapshot = new
        {
            secondary.Id,
            secondary.Email,
            secondary.PhoneNumber,
            secondary.FullName,
            secondary.GoogleId,
            secondary.Provider,
            secondary.RoleId,
            secondary.Status,
            secondary.TwoFactorEnabled,
            HasProfile = secondary.Profile != null,
            HasStaffProfile = secondary.StaffProfile != null
        };

        // ============ 1. Revoke tất cả RefreshToken active của secondary ============
        var activeTokens = await _unitOfWork.RefreshTokens.GetAllAsync()
            .Where(rt => rt.AccountId == secondary.Id && rt.Status == RefreshTokenStatus.Active && !rt.IsDeleted)
            .ToListAsync(cancellationToken);

        foreach (var rt in activeTokens)
        {
            rt.Status = RefreshTokenStatus.Revoked;
            rt.RevokedAt = now;
            rt.RevokedReason = $"Account merged into {primary.Id}";
            _unitOfWork.RefreshTokens.UpdateAsync(rt);
        }

        // ============ 2. Transfer GoogleId (nếu primary chưa có và secondary có) ============
        if (string.IsNullOrEmpty(primary.GoogleId) && !string.IsNullOrEmpty(secondary.GoogleId))
        {
            primary.GoogleId = secondary.GoogleId;
            primary.Provider ??= secondary.Provider;
            conflictResolution["googleId"] = "moved_from_secondary";
        }
        else if (!string.IsNullOrEmpty(primary.GoogleId) && !string.IsNullOrEmpty(secondary.GoogleId))
        {
            conflictResolution["googleId"] = "kept_primary_secondary_googleId_dropped";
        }

        // ============ 3. Transfer profile (nếu primary chưa có) ============
        if (primary.Profile == null && secondary.Profile != null)
        {
            secondary.Profile.AccountId = primary.Id;
            _unitOfWork.AccountProfiles.UpdateAsync(secondary.Profile);
            conflictResolution["accountProfile"] = "moved_from_secondary";
        }
        else if (primary.Profile != null && secondary.Profile != null)
        {
            // Primary đã có profile → soft-delete secondary's profile.
            secondary.Profile.IsDeleted = true;
            secondary.Profile.DeletedAt = now;
            _unitOfWork.AccountProfiles.UpdateAsync(secondary.Profile);
            conflictResolution["accountProfile"] = "kept_primary_secondary_softdeleted";
        }

        // ============ 4. Transfer StaffProfile (nếu primary chưa có) ============
        if (primary.StaffProfile == null && secondary.StaffProfile != null)
        {
            secondary.StaffProfile.AccountId = primary.Id;
            _unitOfWork.StaffProfiles.UpdateAsync(secondary.StaffProfile);
            conflictResolution["staffProfile"] = "moved_from_secondary";
        }
        else if (primary.StaffProfile != null && secondary.StaffProfile != null)
        {
            secondary.StaffProfile.IsDeleted = true;
            secondary.StaffProfile.DeletedAt = now;
            _unitOfWork.StaffProfiles.UpdateAsync(secondary.StaffProfile);
            conflictResolution["staffProfile"] = "kept_primary_secondary_softdeleted";
        }

        // Email/Phone/FullName: KHÔNG move — primary giữ. Note in resolution for audit.
        conflictResolution["email"] = "kept_primary";
        conflictResolution["phoneNumber"] = "kept_primary";
        conflictResolution["fullName"] = "kept_primary";

        // ============ 5. Tombstone secondary ============
        secondary.IsDeleted = true;
        secondary.DeletedAt = now;
        secondary.MergedIntoId = primary.Id;
        secondary.MergedAt = now;
        // Anonymize PII trên secondary để tránh duplicate email/phone với primary (unique index).
        secondary.Email = $"merged-{secondary.Id}@anonymized.local";
        secondary.PhoneNumber = null;
        secondary.GoogleId = null;
        _unitOfWork.Accounts.UpdateAsync(secondary);
        _unitOfWork.Accounts.UpdateAsync(primary);

        // ============ 6. Insert AccountMergeLog ============
        var auditLogsLinked = await _unitOfWork.AuditLogs.GetAllAsync()
            .CountAsync(a => a.TargetAccountId == secondary.Id, cancellationToken);

        var mergeLog = new AccountMergeLog
        {
            Id = Guid.NewGuid(),
            PrimaryAccountId = primary.Id,
            SecondaryAccountId = secondary.Id,
            PerformedBy = request.PerformedBy,
            Reason = request.Reason,
            SecondaryAccountSnapshotJson = JsonSerializer.Serialize(secondarySnapshot),
            ConflictResolutionJson = JsonSerializer.Serialize(conflictResolution),
            SessionsRevoked = activeTokens.Count,
            AuditLogsLinked = auditLogsLinked
        };
        await _unitOfWork.AccountMergeLogs.AddAsync(mergeLog);

        // ============ 7. Audit notifications ============
        await _publisher.Publish(new AuditTrailNotification(
            AuditActionEnum.AccountMerged, primary.Id, IsSuccess: true,
            TargetEmail: primary.Email,
            Reason: request.Reason,
            ActorAccountIdOverride: request.PerformedBy,
            Metadata: new Dictionary<string, object?>
            {
                ["primaryAccountId"] = primary.Id,
                ["secondaryAccountId"] = secondary.Id,
                ["sessionsRevoked"] = activeTokens.Count,
                ["auditLogsLinked"] = auditLogsLinked,
                ["mergeLogId"] = mergeLog.Id
            }), cancellationToken);

        // Meta-audit cho secondary (account effectively deleted).
        await _publisher.Publish(new AuditTrailNotification(
            AuditActionEnum.AccountDeleted, secondary.Id, IsSuccess: true,
            Reason: $"Merged into {primary.Id} by admin {request.PerformedBy}",
            ActorAccountIdOverride: request.PerformedBy,
            Metadata: new Dictionary<string, object?> { ["mergeLogId"] = mergeLog.Id }), cancellationToken);

        // Merge tombstone cũng là một account deletion đối với mọi projection downstream.
        // Outbox trước commit bảo đảm không có trạng thái "Auth đã merge nhưng service khác không
        // bao giờ biết" nếu tiến trình chết ngay sau SaveChanges.
        await _messageProducer.PublishAsync(new AccountDeletedEvent(
            secondary.Id,
            secondarySnapshot.Email,
            DeletionSource: "AccountMerge"), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // ============ 8. Bulk revoke JWT access tokens của secondary (Redis op, post-commit) ============
        await _revocationStore.RevokeAllByAccountAsync(secondary.Id, TimeSpan.FromHours(1), cancellationToken);

        return new AccountActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = $"Merged account {secondary.Id} into {primary.Id}. Revoked {activeTokens.Count} session(s).",
            Data = primary.Id
        };
    }

    private static AccountActionResponse Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message
    };
}
