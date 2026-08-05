using System.Security.Claims;
using FileStorageService.Application.Interfaces;
using FileStorageService.Domain.Entities;
using FileStorageService.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using SharedKernels.Security;

namespace FileStorageService.Application.Authorization;

public class FileAuthorizationService : IFileAuthorizationService
{
    private const string AdminRole = "Admin";
    private const string ManagerRole = "Manager";
    private const string StaffRole = "Staff";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IConfiguration _configuration;

    public FileAuthorizationService(IHttpContextAccessor httpContextAccessor, IConfiguration configuration)
    {
        _httpContextAccessor = httpContextAccessor;
        _configuration = configuration;
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

            // GH-723 — trước đây trả `true` vô điều kiện ⇒ mọi user đã đăng nhập chỉ cần
            // biết fileId là đọc được attachment của ticket người khác.
            //
            // FileStorageService KHÔNG biết ai là participant của ticket (bảng UploadedFile
            // không có liên kết tới ticket, và không có kênh gRPC theo chiều này), nên
            // quyền "participant" được TicketService ký thành grant và mang sang.
            FilePurposeEnum.TicketAttachment =>
                HasAnyRole(ManagerRole, StaffRole)
                || file.CreatedBy == CurrentUserId
                || HasValidGrantFor(file),

            // Maintenance log là tài liệu nội bộ: MaintenanceLogsController chỉ mở cho
            // Staff/Manager/Admin, Customer không có đường vào ⇒ không cần grant.
            FilePurposeEnum.MaintenancePhoto =>
                HasAnyRole(ManagerRole, StaffRole)
                || file.CreatedBy == CurrentUserId,

            _ => file.CreatedBy == CurrentUserId
        };
    }

    /// <summary>
    /// GH-723 — kiểm grant ngắn hạn do TicketService cấp sau khi đã xác thực người gọi là
    /// participant của ticket chứa file này. Fail closed ở mọi nhánh không chắc chắn.
    /// </summary>
    private bool HasValidGrantFor(UploadedFile file)
    {
        var userId = CurrentUserId;
        if (userId is null)
            return false;

        var token = _httpContextAccessor.HttpContext?.Request.Query[FileAccessGrant.QueryParameterName].ToString();
        if (string.IsNullOrWhiteSpace(token))
            return false;

        var secretKey = _configuration["JwtSettings:SecretKey"];
        if (string.IsNullOrWhiteSpace(secretKey))
            return false;

        return FileAccessGrant.Validate(secretKey, token, file.Id, userId.Value, DateTimeOffset.UtcNow);
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
