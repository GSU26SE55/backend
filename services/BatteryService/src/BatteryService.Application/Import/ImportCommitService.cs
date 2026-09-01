using System.Text.Json;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedContracts.Common.Responses;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using BatteryAssetEntity = BatteryService.Domain.Entities.BatteryAsset;
using SiteEntity = BatteryService.Domain.Entities.Site;

namespace BatteryService.Application.Import;

/// <summary>
/// Hiện thực pha ghi thật: cấp tài khoản trước, rồi mới dựng site, pin và thiết bị.
/// </summary>
/// <remarks>
/// <para>
/// Thứ tự bắt buộc là khách hàng → site → pin, vì mỗi bậc tham chiếu bậc trên. Bậc
/// khách hàng lại nằm ở service khác và chỉ liên lạc được qua message bus, nên giữa bậc một và bậc
/// hai có một khoảng chờ thật sự — đó là lý do lô phải quay lại nhiều nhịp chứ không chạy một mạch.
/// </para>
/// <para>
/// Mỗi nhịp chỉ tiến một bậc rồi lưu. Làm vậy để một lỗi ở bậc dưới không cuốn theo kết quả đã ghi
/// được ở bậc trên, và để tiến độ hiển thị nhích dần thay vì đứng im rồi nhảy một phát.
/// </para>
/// </remarks>
public class ImportCommitService : IImportCommitService
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly IImportRowValidator _validator;
    private readonly IBatteryTypeResolver _batteryTypeResolver;
    private readonly IIntegrationEventOutboxWriter _outbox;
    private readonly ImportOptions _options;
    private readonly ILogger<ImportCommitService> _logger;

    /// <summary>
    /// (KhachHangId, TenSite) đã tạo trong lượt này. Cùng bẫy như <see cref="IBatteryTypeResolver"/>:
    /// site vừa <c>AddAsync</c> chưa được lưu xuống nên truy vấn DB không thấy nó — hai dòng site
    /// cùng tên dưới cùng một khách trong CHUNG một lô sẽ cùng lọt qua kiểm tra "trùng tên" rồi vỡ
    /// ràng buộc duy nhất <c>IX_sites_customer_id_name</c> lúc lưu, đánh sập toàn bộ lượt tiến độ
    /// thay vì chỉ đánh hỏng đúng dòng thứ hai.
    /// </summary>
    private readonly HashSet<(Guid CustomerId, string Name)> _pendingSiteNames = new();

    /// <summary>
    /// Serial number các pin mới đã <c>AddAsync</c> trong lượt này. Cùng bẫy như
    /// <see cref="_pendingSiteNames"/>: hai dòng pin cùng serial (sau chuẩn hoá) trong CHUNG một lô
    /// cùng lọt qua kiểm tra "serial đã tồn tại" (query DB không thấy entity vừa <c>AddAsync</c>
    /// nhưng chưa <c>SaveChanges</c>) rồi vỡ ràng buộc duy nhất <c>IX_battery_assets_serial_number</c>
    /// lúc lưu — đánh sập toàn bộ lượt tiến độ thay vì chỉ đánh hỏng đúng dòng thứ hai.
    /// </summary>
    private readonly HashSet<string> _pendingSerialNumbers = new();

    public ImportCommitService(
        IBatteryUnitOfWork unitOfWork,
        IImportRowValidator validator,
        IBatteryTypeResolver batteryTypeResolver,
        IIntegrationEventOutboxWriter outbox,
        IOptions<ImportOptions> options,
        ILogger<ImportCommitService> logger)
    {
        _unitOfWork = unitOfWork;
        _validator = validator;
        _batteryTypeResolver = batteryTypeResolver;
        _outbox = outbox;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<bool> AdvanceAsync(Guid batchId, CancellationToken cancellationToken)
    {
        var batch = await _unitOfWork.ImportBatches
            .GetAllAsync()
            .FirstOrDefaultAsync(b => b.Id == batchId && !b.IsDeleted, cancellationToken);

        if (batch is null)
            return true;

        if (batch.Status is not (ImportBatchStatusEnum.Committing or ImportBatchStatusEnum.AwaitingAccountSync))
            return true;

        _batteryTypeResolver.Reset();
        _pendingSiteNames.Clear();
        _pendingSerialNumbers.Clear();

        var rows = await _unitOfWork.ImportRows
            .GetAllAsync()
            .Where(row => row.ImportBatchId == batchId && !row.IsDeleted)
            .ToListAsync(cancellationToken);

        // Bậc 1 — yêu cầu cấp tài khoản cho các dòng khách hàng chưa gửi đi.
        var pendingCustomers = rows
            .Where(row => row.EntityType == ImportEntityTypeEnum.Customer && row.Status == ImportRowStatusEnum.Valid)
            .ToList();

        if (pendingCustomers.Count > 0)
        {
            await RequestCustomerProvisioningAsync(batch, pendingCustomers, cancellationToken);
            batch.Status = ImportBatchStatusEnum.AwaitingAccountSync;
            _unitOfWork.ImportBatches.UpdateAsync(batch);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return false;
        }

        // Bậc 1b — còn dòng đang chờ tài khoản thì dừng lại, trừ khi đã quá hạn chờ.
        var awaiting = rows.Where(row => row.Status == ImportRowStatusEnum.AwaitingAccount).ToList();
        if (awaiting.Count > 0)
        {
            var deadline = (batch.StartedAt ?? batch.CreatedAt).AddMinutes(_options.AccountSyncTimeoutMinutes);
            if (DateTime.UtcNow < deadline)
                return false;

            foreach (var row in awaiting)
            {
                FailRow(row, "provisioning",
                    $"The customer account was not synchronized within {_options.AccountSyncTimeoutMinutes} minutes. Check that AuthService and the message bus are running, then retry the batch.");
            }

            _logger.LogWarning(
                "Import batch {BatchId} timed out waiting for {Count} customer account(s).", batch.Id, awaiting.Count);
        }

        // Bậc 2, 3, 4 — mỗi nhịp chỉ làm một bậc để tiến độ nhìn thấy được.
        var progressed =
            await ProcessSitesAsync(batch, rows, cancellationToken)
            || await ProcessAssetsAsync(batch, rows, cancellationToken);

        RecomputeCounters(batch, rows);

        if (!progressed)
        {
            batch.Status = batch.FailedRows > 0 || batch.InvalidRows > 0
                ? ImportBatchStatusEnum.CompletedWithErrors
                : ImportBatchStatusEnum.Completed;
            batch.CompletedAt = DateTime.UtcNow;
        }

        _unitOfWork.ImportBatches.UpdateAsync(batch);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return !progressed;
    }

    private async Task RequestCustomerProvisioningAsync(
        ImportBatch batch, List<ImportRow> rows, CancellationToken cancellationToken)
    {
        foreach (var row in rows)
        {
            var cells = ImportRowPayload.FromRawJson(row.RawJson);
            var validation = _validator.ValidateCustomer(ImportRowPayload.ToCustomer(cells));

            if (!validation.IsValid)
            {
                FailRow(row, "validation", "The row no longer passes validation.");
                continue;
            }

            var customer = validation.Value!;

            // Kiểu cụ thể, không phải kiểu cha: bộ ghi outbox lấy tên loại từ tham số generic, nên
            // truyền biến khai kiểu cha sẽ ghi sai tên và bộ chuyển tiếp không nhận ra loại nữa.
            var evt = new PartnerCustomerProvisionRequestedEvent(
                batch.Id, row.Id, customer.ExternalCode, customer.Email,
                customer.FullName, customer.Phone);

            await _outbox.WriteAsync(evt, cancellationToken);

            row.Status = ImportRowStatusEnum.AwaitingAccount;
            row.AttemptCount++;
            _unitOfWork.ImportRows.UpdateAsync(row);
        }
    }

    private async Task<bool> ProcessSitesAsync(
        ImportBatch batch, List<ImportRow> rows, CancellationToken cancellationToken)
    {
        var pending = rows
            .Where(row => row.EntityType == ImportEntityTypeEnum.Site && row.Status == ImportRowStatusEnum.Valid)
            .ToList();

        if (pending.Count == 0)
            return false;

        foreach (var row in pending)
        {
            var cells = ImportRowPayload.FromRawJson(row.RawJson);
            var validation = _validator.ValidateSite(ImportRowPayload.ToSite(cells));
            if (!validation.IsValid)
            {
                FailRow(row, "validation", "The row no longer passes validation.");
                continue;
            }

            var site = validation.Value!;

            var customerId = await ResolveLinkAsync(ImportEntityTypeEnum.Customer, site.ExternalCustomerCode, cancellationToken);
            if (customerId is null)
            {
                FailRow(row, "ExternalCustomerCode",
                    $"No imported customer matches code \"{site.ExternalCustomerCode}\".");
                continue;
            }

            // Bản sao khách hàng phải đang hoạt động — đây là điều kiện mà mọi đường tạo site đều
            // đòi, và cũng là lý do luồng nhập phải cấp tài khoản ở trạng thái hoạt động.
            var mirrorActive = await _unitOfWork.CustomerAccounts
                .GetAllAsync()
                .AnyAsync(account => account.Id == customerId.Value && account.IsActive && !account.IsDeleted, cancellationToken);

            if (!mirrorActive)
            {
                FailRow(row, "ExternalCustomerCode",
                    "The customer account exists but has not been mirrored into BatteryService yet.");
                continue;
            }

            var existingId = await ResolveLinkAsync(ImportEntityTypeEnum.Site, site.ExternalCode, cancellationToken);
            if (existingId is not null)
            {
                var existing = await _unitOfWork.Sites
                    .GetAllAsync()
                    .FirstOrDefaultAsync(s => s.Id == existingId.Value && !s.IsDeleted, cancellationToken);

                if (existing is null)
                {
                    FailRow(row, "ExternalSiteCode", "The previously imported site no longer exists.");
                    continue;
                }

                ApplySite(existing, site, customerId.Value);
                _unitOfWork.Sites.UpdateAsync(existing);
                MarkRow(row, ImportRowStatusEnum.Updated, existing.Id);
                continue;
            }

            var nameKey = (customerId.Value, site.Name);
            var nameTaken = _pendingSiteNames.Contains(nameKey) || await _unitOfWork.Sites
                .GetAllAsync()
                .AnyAsync(s => !s.IsDeleted && s.CustomerId == customerId.Value && s.Name == site.Name, cancellationToken);

            if (nameTaken)
            {
                FailRow(row, "SiteName",
                    $"This customer already has a site named \"{site.Name}\" that was not created by an import.");
                continue;
            }

            var created = new SiteEntity
            {
                Id = Guid.NewGuid(),
                CustomerId = customerId.Value,
                Status = SiteStatusEnum.Active
            };
            ApplySite(created, site, customerId.Value);

            await _unitOfWork.Sites.AddAsync(created);
            _pendingSiteNames.Add(nameKey);
            await AddLinkAsync(batch, ImportEntityTypeEnum.Site, site.ExternalCode, site.ExternalCodeRaw, created.Id);
            MarkRow(row, ImportRowStatusEnum.Created, created.Id);
        }

        return true;
    }

    private async Task<bool> ProcessAssetsAsync(
        ImportBatch batch, List<ImportRow> rows, CancellationToken cancellationToken)
    {
        var pending = rows
            .Where(row => row.EntityType == ImportEntityTypeEnum.BatteryAsset && row.Status == ImportRowStatusEnum.Valid)
            .ToList();

        if (pending.Count == 0)
            return false;

        foreach (var row in pending)
        {
            var cells = ImportRowPayload.FromRawJson(row.RawJson);
            var validation = _validator.ValidateAsset(ImportRowPayload.ToAsset(cells));
            if (!validation.IsValid)
            {
                FailRow(row, "validation", "The row no longer passes validation.");
                continue;
            }

            var asset = validation.Value!;

            var siteId = await ResolveLinkAsync(ImportEntityTypeEnum.Site, asset.ExternalSiteCode, cancellationToken);
            if (siteId is null)
            {
                FailRow(row, "ExternalSiteCode", $"No imported site matches code \"{asset.ExternalSiteCode}\".");
                continue;
            }

            var site = await _unitOfWork.Sites
                .GetAllAsync()
                .FirstOrDefaultAsync(s => s.Id == siteId.Value && !s.IsDeleted, cancellationToken);

            if (site is null)
            {
                FailRow(row, "ExternalSiteCode", "The previously imported site no longer exists.");
                continue;
            }

            var typeResolution = await _batteryTypeResolver.ResolveAsync(asset, cancellationToken);
            if (!typeResolution.IsSuccess)
            {
                FailRow(row, "BatteryTypeName", typeResolution.FailureReason ?? "Battery type could not be resolved.");
                continue;
            }

            var existingId = await ResolveLinkAsync(ImportEntityTypeEnum.BatteryAsset, asset.ExternalCode, cancellationToken);
            if (existingId is not null)
            {
                var existing = await _unitOfWork.BatteryAssets
                    .GetAllAsync()
                    .FirstOrDefaultAsync(a => a.Id == existingId.Value && !a.IsDeleted, cancellationToken);

                if (existing is null)
                {
                    FailRow(row, "ExternalAssetCode", "The previously imported battery asset no longer exists.");
                    continue;
                }

                ApplyAsset(existing, asset, site, typeResolution.BatteryTypeId);
                _unitOfWork.BatteryAssets.UpdateAsync(existing);
                MarkRow(row, ImportRowStatusEnum.Updated, existing.Id);
                continue;
            }

            var serialTaken = _pendingSerialNumbers.Contains(asset.SerialNumber) || await _unitOfWork.BatteryAssets
                .GetAllAsync()
                .AnyAsync(a => !a.IsDeleted && a.SerialNumber == asset.SerialNumber, cancellationToken);

            if (serialTaken)
            {
                FailRow(row, "SerialNumber",
                    $"Serial number \"{asset.SerialNumber}\" already belongs to a battery asset that was not created by an import.");
                continue;
            }

            var created = new BatteryAssetEntity
            {
                Id = Guid.NewGuid(),
                Status = BatteryStatusEnum.Active
            };
            ApplyAsset(created, asset, site, typeResolution.BatteryTypeId);

            await _unitOfWork.BatteryAssets.AddAsync(created);
            _pendingSerialNumbers.Add(asset.SerialNumber);
            await AddLinkAsync(batch, ImportEntityTypeEnum.BatteryAsset, asset.ExternalCode, asset.ExternalCodeRaw, created.Id);

            var evt = new BatteryAssetCreatedEvent(
                BatteryAssetId: created.Id,
                CustomerId: created.CustomerId,
                SiteId: created.SiteId,
                BatteryTypeId: created.BatteryTypeId,
                SerialNumber: created.SerialNumber,
                CreatedAt: DateTime.UtcNow);
            await _outbox.WriteAsync(evt, cancellationToken);

            MarkRow(row, ImportRowStatusEnum.Created, created.Id);
        }

        return true;
    }


    private static void ApplySite(SiteEntity entity, ValidatedSite source, Guid customerId)
    {
        entity.Name = source.Name;
        entity.CustomerId = customerId;
        entity.Address = source.Address;
        entity.Latitude = source.Latitude;
        entity.Longitude = source.Longitude;
        entity.InstallDate = source.InstallDate;
        entity.ContactPersonName = source.ContactPersonName;
        entity.ContactPersonPhone = source.ContactPersonPhone;
    }

    private static void ApplyAsset(BatteryAssetEntity entity, ValidatedAsset source, SiteEntity site, Guid batteryTypeId)
    {
        entity.SerialNumber = source.SerialNumber;
        entity.BatteryTypeId = batteryTypeId;
        entity.SiteId = site.Id;
        entity.CustomerId = site.CustomerId;
        entity.InstallDate = source.InstallDate;
        entity.WarrantyEndDate = source.WarrantyEndDate;
        entity.WarrantyStatus = source.WarrantyEndDate.HasValue && source.WarrantyEndDate.Value <= DateTime.UtcNow
            ? WarrantyStatusEnum.Expired
            : WarrantyStatusEnum.Active;
        entity.Location = source.Location;
        entity.Notes = source.Notes;
    }

    private async Task<Guid?> ResolveLinkAsync(
        ImportEntityTypeEnum entityType, string externalRef, CancellationToken cancellationToken)
    {
        if (externalRef.Length == 0)
            return null;

        var link = await _unitOfWork.ImportEntityLinks
            .GetAllAsync()
            .Where(l => !l.IsDeleted && l.EntityType == entityType && l.ExternalRef == externalRef)
            .Select(l => new { l.InternalId })
            .FirstOrDefaultAsync(cancellationToken);

        return link?.InternalId;
    }

    private async Task AddLinkAsync(
        ImportBatch batch, ImportEntityTypeEnum entityType, string externalRef, string externalRefRaw, Guid internalId)
    {
        await _unitOfWork.ImportEntityLinks.AddAsync(new ImportEntityLink
        {
            Id = Guid.NewGuid(),
            EntityType = entityType,
            ExternalRef = externalRef,
            ExternalRefRaw = externalRefRaw,
            InternalId = internalId,
            CreatedByBatchId = batch.Id
        });
    }

    private void MarkRow(ImportRow row, ImportRowStatusEnum status, Guid entityId)
    {
        row.Status = status;
        row.CreatedEntityId = entityId;
        row.ProcessedAt = DateTime.UtcNow;
        row.AttemptCount++;
        _unitOfWork.ImportRows.UpdateAsync(row);
    }

    private void FailRow(ImportRow row, string field, string detail)
    {
        var errors = string.IsNullOrEmpty(row.ErrorsJson)
            ? new List<Errors>()
            : JsonSerializer.Deserialize<List<Errors>>(row.ErrorsJson) ?? new List<Errors>();

        errors.Add(new Errors { Field = field, Detail = detail });

        row.ErrorsJson = JsonSerializer.Serialize(errors);
        row.Status = ImportRowStatusEnum.Failed;
        row.ProcessedAt = DateTime.UtcNow;
        row.AttemptCount++;
        _unitOfWork.ImportRows.UpdateAsync(row);
    }

    private static void RecomputeCounters(ImportBatch batch, List<ImportRow> rows)
    {
        batch.CreatedRows = rows.Count(r => r.Status == ImportRowStatusEnum.Created);
        batch.UpdatedRows = rows.Count(r => r.Status == ImportRowStatusEnum.Updated);
        batch.SkippedRows = rows.Count(r => r.Status == ImportRowStatusEnum.Skipped);
        batch.FailedRows = rows.Count(r => r.Status == ImportRowStatusEnum.Failed);
        batch.InvalidRows = rows.Count(r => r.Status == ImportRowStatusEnum.Invalid);
    }
}
