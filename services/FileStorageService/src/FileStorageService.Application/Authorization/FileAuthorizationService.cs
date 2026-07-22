using System.Security.Claims;
using FileStorageService.Application.Interfaces;
using FileStorageService.Domain.Entities;
using FileStorageService.Domain.Enums;
using Microsoft.AspNetCore.Http;

namespace FileStorageService.Application.Authorization;

public class FileAuthorizationService : IFileAuthorizationService
{
    private const string AdminRole = "Admin";
    private const string ManagerRole = "Manager";
    private const string StaffRole = "Staff";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public FileAuthorizationService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? CurrentUserId
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            var rawUserId = user?.FindFirstValue(ClaimTypes.NameIdentifier)
                            ?? user?.FindFirstValue("AccountId");

            return Guid.TryParse(rawUserId, out var userId) ? userId : null;
        }
    }

    public bool CanUpload(FilePurposeEnum purpose)
    {
        if (CurrentUserId is null)
            return false;

        return purpose switch
        {
            FilePurposeEnum.Firmware => HasRole(AdminRole),
            // Staff là người soạn bài Knowledge Base chính (BE cho Staff tạo/sửa bài
            // qua /api/internal/knowledge-base) nên phải được chèn ảnh vào bài viết.
            // Customer vẫn bị chặn — không thuộc 3 role nội bộ.
            FilePurposeEnum.KbImage => HasAnyRole(AdminRole, ManagerRole, StaffRole),
            _ => true
        };
    }

    public bool CanRead(UploadedFile file)
    {
        if (CurrentUserId is null)
            return false;

        if (HasRole(AdminRole))
            return true;

        return file.Purpose switch
        {
            FilePurposeEnum.Avatar => true,
            FilePurposeEnum.KbImage => true,
            FilePurposeEnum.Firmware => false,
            FilePurposeEnum.TicketAttachment or FilePurposeEnum.MaintenancePhoto => true,
            // => HasAnyRole(ManagerRole, StaffRole) || file.CreatedBy == CurrentUserId,
            _ => file.CreatedBy == CurrentUserId
        };
    }

    public bool CanDelete(UploadedFile file)
    {
        if (CurrentUserId is null)
            return false;

        if (HasRole(AdminRole))
            return true;

        if (file.Purpose == FilePurposeEnum.Firmware)
            return false;

        return file.CreatedBy == CurrentUserId;
    }

    private bool HasAnyRole(params string[] roles)
    {
        return roles.Any(HasRole);
    }

    private bool HasRole(string role)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user is null)
            return false;

        return user.Claims.Any(claim =>
            (claim.Type == ClaimTypes.Role || claim.Type == "role")
            && string.Equals(claim.Value, role, StringComparison.OrdinalIgnoreCase));
    }
}
