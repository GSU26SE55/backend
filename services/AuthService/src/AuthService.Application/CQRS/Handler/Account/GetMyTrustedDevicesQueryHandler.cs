using AuthService.Application.CQRS.Query.Account;
using AuthService.Application.DTOs.Response.TrustedDevice;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Application.Interfaces.Repositories;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace AuthService.Application.CQRS.Handler.Account;

/// <summary>
/// #AUTH-48: Trả về danh sách trusted device active của account.
/// "Active" = not deleted + not revoked + ExpiresAt > now. Sort theo TrustedAt desc.
/// Đánh dấu device hiện tại (qua X-Device-Id + UA) bằng <see cref="TrustedDeviceDto.IsCurrentDevice"/>.
/// </summary>
public class GetMyTrustedDevicesQueryHandler
    : IRequestHandler<GetMyTrustedDevicesQuery, CommonResponse<List<TrustedDeviceDto>>>
{
    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IHttpContextAccessor? _httpContextAccessor;

    public GetMyTrustedDevicesQueryHandler(
        IAuthUnitOfWork unitOfWork,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _unitOfWork = unitOfWork;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<CommonResponse<List<TrustedDeviceDto>>> Handle(
        GetMyTrustedDevicesQuery request, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var devices = await _unitOfWork.TrustedDevices
            .GetAllAsync()
            .Where(td => td.AccountId == request.AccountId
                && td.RevokedAt == null
                && td.ExpiresAt > now)
            .OrderByDescending(td => td.TrustedAt)
            .ToListAsync(cancellationToken);

        var (_, userAgent, deviceId) = ClientInfoHelper.Resolve(_httpContextAccessor?.HttpContext);
        var currentFingerprint = TrustedDeviceFingerprintHelper.ComputeFingerprint(deviceId, userAgent);

        var items = devices.Select(td => new TrustedDeviceDto
        {
            Id = td.Id,
            Label = td.Label,
            IpPrefix = td.IpPrefix,
            UserAgentSnapshot = td.UserAgentSnapshot,
            TrustedAt = td.TrustedAt,
            ExpiresAt = td.ExpiresAt,
            LastUsedAt = td.LastUsedAt,
            UsageCount = td.UsageCount,
            IsCurrentDevice = currentFingerprint != null
                && string.Equals(td.DeviceFingerprintHash, currentFingerprint, StringComparison.Ordinal)
        }).ToList();

        return new CommonResponse<List<TrustedDeviceDto>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = items
        };
    }
}
