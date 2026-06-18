using AuthService.Application.CQRS.Query.Account;
using AuthService.Application.Interfaces.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace AuthService.Application.CQRS.Handler.Account;

/// <summary>
/// #AUTH-62: aggregate toàn bộ data của 1 account thành snapshot JSON-ready.
/// AuditLog limit 1000 row mới nhất để response không khổng lồ.
/// </summary>
public class ExportMyDataQueryHandler : IRequestHandler<ExportMyDataQuery, CommonResponse<AccountDataExportDto>>
{
    private const int AuditLogLimit = 1000;

    private readonly IAuthUnitOfWork _unitOfWork;

    public ExportMyDataQueryHandler(IAuthUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<CommonResponse<AccountDataExportDto>> Handle(ExportMyDataQuery request, CancellationToken cancellationToken)
    {
        var account = await _unitOfWork.Accounts.GetAllAsync()
            .AsNoTracking()
            .Include(a => a.Role)
            .FirstOrDefaultAsync(a => a.Id == request.AccountId && !a.IsDeleted, cancellationToken);

        if (account == null)
            return new CommonResponse<AccountDataExportDto>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy tài khoản."
            };

        var export = new AccountDataExportDto
        {
            ExportedAt = DateTime.UtcNow,
            Account = new AccountSnapshot
            {
                Id = account.Id,
                Email = account.Email,
                PhoneNumber = account.PhoneNumber,
                FullName = account.FullName,
                AvatarUrl = account.AvatarUrl,
                DateOfBirth = account.DateOfBirth,
                Address = account.Address,
                EmailConfirmed = account.EmailConfirmed,
                PhoneConfirmed = account.PhoneConfirmed,
                TwoFactorEnabled = account.TwoFactorEnabled,
                Status = account.Status.ToString(),
                GoogleId = account.GoogleId,
                Provider = account.Provider,
                LastLoginAt = account.LastLoginAt,
                LastLoginIp = account.LastLoginIp,
                Role = account.Role?.Name ?? string.Empty,
                CreatedAt = account.CreatedAt
            }
        };

        var profile = await _unitOfWork.AccountProfiles.GetAllAsync()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.AccountId == account.Id && !p.IsDeleted, cancellationToken);
        if (profile != null)
        {
            export.Profile = new AccountProfileSnapshot
            {
                ExternalAvatarUrl = profile.ExternalAvatarUrl,
                AvatarSource = profile.AvatarSource.ToString(),
                Address = profile.Address,
                BirthDate = profile.BirthDate,
                TimeZone = profile.TimeZone
            };
        }

        var staffProfile = await _unitOfWork.StaffProfiles.GetAllAsync()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.AccountId == account.Id && !s.IsDeleted, cancellationToken);
        if (staffProfile != null)
        {
            export.StaffProfile = new StaffProfileSnapshot
            {
                EmployeeCode = staffProfile.EmployeeCode,
                Department = staffProfile.Department,
                SkillTier = staffProfile.SkillTier.ToString(),
                Notes = staffProfile.Notes
            };
        }

        export.Sessions = await _unitOfWork.RefreshTokens.GetAllAsync()
            .AsNoTracking()
            .Where(rt => rt.AccountId == account.Id)
            .OrderByDescending(rt => rt.IssuedAt)
            .Select(rt => new SessionSnapshot
            {
                Id = rt.Id,
                IssuedAt = rt.IssuedAt,
                ExpiredAt = rt.ExpiredAt,
                Status = rt.Status.ToString(),
                IpAddress = rt.IpAddress,
                UserAgent = rt.UserAgent,
                DeviceId = rt.DeviceId,
                RevokedAt = rt.RevokedAt,
                RevokedReason = rt.RevokedReason
            })
            .ToListAsync(cancellationToken);

        export.AuditLogs = await _unitOfWork.AuditLogs.GetAllAsync()
            .AsNoTracking()
            .Where(a => a.TargetAccountId == account.Id || a.ActorAccountId == account.Id)
            .OrderByDescending(a => a.CreatedAt)
            .Take(AuditLogLimit)
            .Select(a => new AuditLogSnapshot
            {
                Id = a.Id,
                Action = a.Action.ToString(),
                OccurredAt = a.CreatedAt,
                IsSuccess = a.IsSuccess,
                IpAddress = a.IpAddress,
                UserAgent = a.UserAgent,
                Reason = a.Reason
            })
            .ToListAsync(cancellationToken);

        export.BackupCodes = await _unitOfWork.BackupCodes.GetAllAsync()
            .AsNoTracking()
            .Where(b => b.AccountId == account.Id && !b.IsDeleted)
            .Select(b => new BackupCodeSnapshot
            {
                Id = b.Id,
                CreatedAt = b.CreatedAt,
                RedeemedAt = b.RedeemedAt
            })
            .ToListAsync(cancellationToken);

        return new CommonResponse<AccountDataExportDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = export
        };
    }
}
