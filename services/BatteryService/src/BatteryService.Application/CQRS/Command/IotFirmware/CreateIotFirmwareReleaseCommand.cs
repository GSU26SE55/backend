using System.Text.Json.Serialization;
using BatteryService.Application.DTOs;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace BatteryService.Application.CQRS.Command.IotFirmware;

public class CreateIotFirmwareReleaseCommand : IRequest<CommonResponse<IotFirmwareReleaseDto>>, IValidatable<CommonResponse<IotFirmwareReleaseDto>>
{
    public string Version { get; set; } = string.Empty;
    public string HardwareRevision { get; set; } = string.Empty;
    public string ArtifactUrl { get; set; } = string.Empty;
    public string Sha256Checksum { get; set; } = string.Empty;
    public long ArtifactSizeBytes { get; set; }
    public string? ReleaseNotes { get; set; }
    public bool PublishImmediately { get; set; }

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
    [JsonIgnore]
    [BindNever]
    public Guid Id { get; set; }
}

public class ArchiveIotFirmwareReleaseCommand : IRequest<CommonResponse<object>>
{
    [JsonIgnore]
    [BindNever]
    public Guid Id { get; set; }
}
