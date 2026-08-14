using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TicketService.Application.CQRS.Command.Chats;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Infrastructure.Implements.Services;


public class LocalGroupMentionResolver : IGroupMentionResolverService
{
    private readonly ITicketUnitOfWork _uow;
    private readonly ILogger<LocalGroupMentionResolver> _logger;

    public LocalGroupMentionResolver(ITicketUnitOfWork uow, ILogger<LocalGroupMentionResolver> logger)
    {
        _uow = uow;
        _logger = logger;
    }

    public async Task<List<(Guid UserId, ActorRoleEnum Role, string? DisplayName)>> ResolveAsync(
        GroupMentionInput input,
        List<TicketParticipant> activeParticipants,
        CancellationToken ct)
    {
        return input.GroupType.ToLowerInvariant() switch
        {
            "role" => await ResolveByRoleAsync(input.GroupIdentifier, activeParticipants, ct),
            "team" => await ResolveByTierAsync(input.GroupIdentifier, activeParticipants, ct),
            _ => new List<(Guid, ActorRoleEnum, string?)>()
        };
    }

    private async Task<List<(Guid UserId, ActorRoleEnum Role, string? DisplayName)>> ResolveByRoleAsync(
        string roleIdentifier,
        List<TicketParticipant> activeParticipants,
        CancellationToken ct)
    {
        var targetRole = ParseRole(roleIdentifier);
        if (targetRole is null)
            return new();

        var matched = activeParticipants
            .Where(p => p.UserRole == targetRole.Value)
            .Select(p => p.UserId)
            .ToList();

        if (matched.Count == 0)
            return new();

        if (targetRole.Value == ActorRoleEnum.Customer)
        {
            var accounts = await _uow.CustomerAccounts.GetAllAsync()
                .Where(a => matched.Contains(a.AccountId) && !a.IsDeleted)
                .Select(a => new { a.AccountId, a.FullName })
                .ToListAsync(ct);

            return accounts
                .Select(a => (a.AccountId, ActorRoleEnum.Customer, (string?)a.FullName))
                .ToList();
        }
        else
        {
            var accounts = await _uow.StaffAccounts.GetAllAsync()
                .Where(a => matched.Contains(a.AccountId) && !a.IsDeleted)
                .Select(a => new { a.AccountId, a.FullName })
                .ToListAsync(ct);

            var nameMap = accounts.ToDictionary(a => a.AccountId, a => a.FullName);

            var missingIds = matched.Where(uid => !nameMap.ContainsKey(uid)).ToList();
            if (missingIds.Count > 0)
                _logger.LogWarning(
                    "[GroupMention] {Count} participant(s) have no StaffAccount record and will be skipped: {Ids}",
                    missingIds.Count, string.Join(", ", missingIds));

            return matched
                .Where(uid => nameMap.ContainsKey(uid))
                .Select(userId => (userId, targetRole.Value, (string?)nameMap[userId]))
                .ToList();
        }
    }

    private async Task<List<(Guid UserId, ActorRoleEnum Role, string? DisplayName)>> ResolveByTierAsync(
        string teamIdentifier,
        List<TicketParticipant> activeParticipants,
        CancellationToken ct)
    {
        var tier = ParseTier(teamIdentifier);
        if (tier is null)
        {
            _logger.LogWarning("[GroupMention] Unknown team identifier: {Identifier}", teamIdentifier);
            return new();
        }

        var staffParticipantIds = activeParticipants
            .Where(p => p.UserRole == ActorRoleEnum.Staff)
            .Select(p => p.UserId)
            .ToHashSet();

        if (staffParticipantIds.Count == 0)
            return new();

        var matched = await _uow.StaffAccounts.GetAllAsync()
            .Where(a => staffParticipantIds.Contains(a.AccountId) && a.SkillTier == tier.Value && !a.IsDeleted)
            .Select(a => new { a.AccountId, a.FullName })
            .ToListAsync(ct);

        return matched
            .Select(a => (a.AccountId, ActorRoleEnum.Staff, (string?)a.FullName))
            .ToList();
    }

    private static ActorRoleEnum? ParseRole(string identifier) => identifier.ToLowerInvariant() switch
    {
        "manager" => ActorRoleEnum.Manager,
        "staff" => ActorRoleEnum.Staff,
        "admin" => ActorRoleEnum.Admin,
        "customer" => ActorRoleEnum.Customer,
        _ => null
    };

    private static StaffSkillTierEnum? ParseTier(string identifier) => identifier.ToLowerInvariant() switch
    {
        "tier1-staff" => StaffSkillTierEnum.Generalist,
        "tier2-staff" => StaffSkillTierEnum.ModuleSpecialist,
        "tier3-staff" => StaffSkillTierEnum.SeniorSpecialist,
        _ => null
    };
}
