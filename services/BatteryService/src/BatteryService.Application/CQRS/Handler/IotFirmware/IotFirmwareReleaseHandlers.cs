using BatteryService.Application.CQRS.Command.IotFirmware;
using BatteryService.Application.CQRS.Query.IotFirmware;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Mapping;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Requests;
using SharedContracts.Common.Responses;
using FirmwareEntity = BatteryService.Domain.Entities.IotFirmwareRelease;

namespace BatteryService.Application.CQRS.Handler.IotFirmware;

public class CreateIotFirmwareReleaseCommandHandler : IRequestHandler<CreateIotFirmwareReleaseCommand, CommonResponse<IotFirmwareReleaseDto>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    public CreateIotFirmwareReleaseCommandHandler(IBatteryUnitOfWork uow) => _unitOfWork = uow;

    public async Task<CommonResponse<IotFirmwareReleaseDto>> Handle(CreateIotFirmwareReleaseCommand request, CancellationToken ct)
    {
        var hw = request.HardwareRevision.Trim();
        var ver = request.Version.Trim();
        var dup = await _unitOfWork.IotFirmwareReleases.GetAllAsync()
            .AnyAsync(f => !f.IsDeleted && f.Version == ver && f.HardwareRevision == hw, ct);
        if (dup)
            return new CommonResponse<IotFirmwareReleaseDto> { IsSuccess = false, StatusCode = 409, Message = "Version đã tồn tại cho hardware này." };

        var entity = new FirmwareEntity
        {
            Id = Guid.NewGuid(),
            Version = ver,
            HardwareRevision = hw,
            ArtifactUrl = request.ArtifactUrl.Trim(),
            Sha256Checksum = request.Sha256Checksum.Trim().ToLowerInvariant(),
            ArtifactSizeBytes = request.ArtifactSizeBytes,
            ReleaseNotes = request.ReleaseNotes?.Trim(),
            IsPublished = request.PublishImmediately,
            PublishedAt = request.PublishImmediately ? DateTime.UtcNow : null,
            // Sprint IoT-2 #IoT2-35.
            IsRequired = request.IsRequired,
            Channel = request.Channel,
            DeviceModel = request.DeviceModel?.Trim()
        };
        await _unitOfWork.IotFirmwareReleases.AddAsync(entity);
        await _unitOfWork.SaveChangesAsync(ct);

        return new CommonResponse<IotFirmwareReleaseDto>
        {
            IsSuccess = true,
            StatusCode = 201,
            Message = "Tạo firmware release thành công.",
            Data = IotDeviceMapper.ToDto(entity)
        };
    }
}

public class PublishIotFirmwareReleaseCommandHandler : IRequestHandler<PublishIotFirmwareReleaseCommand, CommonResponse<IotFirmwareReleaseDto>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    public PublishIotFirmwareReleaseCommandHandler(IBatteryUnitOfWork uow) => _unitOfWork = uow;

    public async Task<CommonResponse<IotFirmwareReleaseDto>> Handle(PublishIotFirmwareReleaseCommand request, CancellationToken ct)
    {
        var entity = await _unitOfWork.IotFirmwareReleases.GetAllAsync()
            .FirstOrDefaultAsync(f => f.Id == request.Id && !f.IsDeleted, ct);
        if (entity is null)
            return new CommonResponse<IotFirmwareReleaseDto> { IsSuccess = false, StatusCode = 404, Message = "Không tìm thấy firmware release." };
        if (entity.IsArchived)
            return new CommonResponse<IotFirmwareReleaseDto> { IsSuccess = false, StatusCode = 409, Message = "Release đã archived." };

        entity.IsPublished = true;
        entity.PublishedAt ??= DateTime.UtcNow;
        _unitOfWork.IotFirmwareReleases.UpdateAsync(entity);
        await _unitOfWork.SaveChangesAsync(ct);

        return new CommonResponse<IotFirmwareReleaseDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Đã publish.",
            Data = IotDeviceMapper.ToDto(entity)
        };
    }
}

public class ArchiveIotFirmwareReleaseCommandHandler : IRequestHandler<ArchiveIotFirmwareReleaseCommand, CommonResponse<object>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    public ArchiveIotFirmwareReleaseCommandHandler(IBatteryUnitOfWork uow) => _unitOfWork = uow;

    public async Task<CommonResponse<object>> Handle(ArchiveIotFirmwareReleaseCommand request, CancellationToken ct)
    {
        var entity = await _unitOfWork.IotFirmwareReleases.GetAllAsync()
            .FirstOrDefaultAsync(f => f.Id == request.Id && !f.IsDeleted, ct);
        if (entity is null)
            return new CommonResponse<object> { IsSuccess = false, StatusCode = 404, Message = "Không tìm thấy firmware release." };
        entity.IsArchived = true;
        _unitOfWork.IotFirmwareReleases.UpdateAsync(entity);
        await _unitOfWork.SaveChangesAsync(ct);
        return new CommonResponse<object> { IsSuccess = true, StatusCode = 200, Message = "Đã archive." };
    }
}

public class GetIotFirmwareReleasesQueryHandler : IRequestHandler<GetIotFirmwareReleasesQuery, CommonResponse<PaginationResponse<IotFirmwareReleaseDto>>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    public GetIotFirmwareReleasesQueryHandler(IBatteryUnitOfWork uow) => _unitOfWork = uow;

    public async Task<CommonResponse<PaginationResponse<IotFirmwareReleaseDto>>> Handle(GetIotFirmwareReleasesQuery request, CancellationToken ct)
    {
        var page = request.Page <= 0 ? 1 : request.Page;
        var size = Math.Clamp(request.PageSize, 1, 100);

        var query = _unitOfWork.IotFirmwareReleases.GetAllAsync().Where(f => !f.IsDeleted);
        if (!string.IsNullOrWhiteSpace(request.HardwareRevision))
            query = query.Where(f => f.HardwareRevision == request.HardwareRevision);
        if (request.PublishedOnly == true)
            query = query.Where(f => f.IsPublished && !f.IsArchived);

        var total = await query.CountAsync(ct);

        var descending = SortHelper.IsDescending(request.SortDir);
        // Whitelist: version | hardwareRevision | channel | status(rank) | artifactSizeBytes | createdAt (default).
        // status rank: Draft=0 < Published=1 < Archived=2 (lifecycle) — derived vì entity không có field Status đơn.
        var ordered = (request.SortBy?.Trim().ToLowerInvariant()) switch
        {
            "version" => descending ? query.OrderByDescending(f => f.Version) : query.OrderBy(f => f.Version),
            "hardwarerevision" => descending ? query.OrderByDescending(f => f.HardwareRevision) : query.OrderBy(f => f.HardwareRevision),
            "channel" => descending ? query.OrderByDescending(f => f.Channel) : query.OrderBy(f => f.Channel),
            "status" => descending
                ? query.OrderByDescending(f => f.IsArchived ? 2 : (f.IsPublished ? 1 : 0))
                : query.OrderBy(f => f.IsArchived ? 2 : (f.IsPublished ? 1 : 0)),
            "artifactsizebytes" => descending ? query.OrderByDescending(f => f.ArtifactSizeBytes) : query.OrderBy(f => f.ArtifactSizeBytes),
            _ => descending ? query.OrderByDescending(f => f.CreatedAt) : query.OrderBy(f => f.CreatedAt),
        };

        var items = await ordered
            .ThenBy(f => f.Id) // tie-breaker cố định — pagination ổn định
            .Skip((page - 1) * size).Take(size).ToListAsync(ct);

        return new CommonResponse<PaginationResponse<IotFirmwareReleaseDto>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new PaginationResponse<IotFirmwareReleaseDto>
            {
                Items = items.Select(IotDeviceMapper.ToDto).ToList(),
                TotalItems = total,
                PageNumber = page,
                PageSize = size
            }
        };
    }
}
