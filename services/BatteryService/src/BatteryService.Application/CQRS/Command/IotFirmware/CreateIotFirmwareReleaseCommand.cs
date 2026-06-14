using System.Text.Json.Serialization;
using BatteryService.Application.DTOs;
using BatteryService.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace BatteryService.Application.CQRS.Command.IotFirmware;

public class CreateIotFirmwareReleaseCommand : IRequest<CommonResponse<IotFirmwareReleaseDto>>, IValidatable<CommonResponse<IotFirmwareReleaseDto>>
{
    /// <summary>Phiên bản theo SemVer X.Y.Z.</summary>
    public string Version { get; set; } = string.Empty;
    /// <summary>Hardware revision (vd "v1.0-S3-MAX485").</summary>
    public string HardwareRevision { get; set; } = string.Empty;
    /// <summary>URL absolute của artifact .bin (FileStorage presigned).</summary>
    public string ArtifactUrl { get; set; } = string.Empty;
    /// <summary>SHA-256 hex (64 chars) của artifact để device verify.</summary>
    public string Sha256Checksum { get; set; } = string.Empty;
    /// <summary>Kích thước artifact (bytes, &le; 50MB).</summary>
    public long ArtifactSizeBytes { get; set; }
    /// <summary>Changelog/release notes (markdown).</summary>
    public string? ReleaseNotes { get; set; }
    /// <summary>Publish ngay sau khi tạo (skip bước publish riêng).</summary>
    public bool PublishImmediately { get; set; }

    // Sprint IoT-2 #IoT2-35 — spec fields.
    /// <summary>Force update — device bắt buộc apply.</summary>
    public bool IsRequired { get; set; }
    /// <summary>Channel sensor (vd "voltage", "current", "temperature").</summary>
    public IotFirmwareChannelEnum Channel { get; set; } = IotFirmwareChannelEnum.Stable;
    /// <summary>Model device (vd "ESP32-S3-WROOM-1").</summary>
    public string? DeviceModel { get; set; }

    public Task<CommonResponse<IotFirmwareReleaseDto>> ValidateAsync()
    {
        var response = new CommonResponse<IotFirmwareReleaseDto>();
        if (string.IsNullOrWhiteSpace(Version))
            AddError(response, nameof(Version), "Version là bắt buộc.");
        else if (!System.Text.RegularExpressions.Regex.IsMatch(Version, @"^\d+\.\d+\.\d+$"))
            AddError(response, nameof(Version), "Version phải theo SemVer X.Y.Z.");
        if (string.IsNullOrWhiteSpace(HardwareRevision))
            AddError(response, nameof(HardwareRevision), "HardwareRevision là bắt buộc.");
        if (string.IsNullOrWhiteSpace(ArtifactUrl) || !Uri.IsWellFormedUriString(ArtifactUrl, UriKind.Absolute))
            AddError(response, nameof(ArtifactUrl), "ArtifactUrl phải là URL hợp lệ.");
        if (string.IsNullOrWhiteSpace(Sha256Checksum) || Sha256Checksum.Length != 64)
            AddError(response, nameof(Sha256Checksum), "Sha256Checksum phải dài 64 ký tự hex.");
        if (ArtifactSizeBytes <= 0 || ArtifactSizeBytes > 50_000_000)
            AddError(response, nameof(ArtifactSizeBytes), "ArtifactSizeBytes phải nằm trong (0, 50MB].");
        return Task.FromResult(response);
    }

    private static void AddError(CommonResponse<IotFirmwareReleaseDto> r, string field, string detail)
    {
        r.IsSuccess = false;
        r.StatusCode = 400;
        r.Message = "Dữ liệu firmware không hợp lệ.";
        r.ListErrors.Add(new Errors { Field = field, Detail = detail });
    }
}

public class PublishIotFirmwareReleaseCommand : IRequest<CommonResponse<IotFirmwareReleaseDto>>
{
    /// <summary>Định danh resource.</summary>
    [JsonIgnore]
    [BindNever]
    public Guid Id { get; set; }
}

public class ArchiveIotFirmwareReleaseCommand : IRequest<CommonResponse<object>>
{
    /// <summary>Định danh resource.</summary>
    [JsonIgnore]
    [BindNever]
    public Guid Id { get; set; }
}
