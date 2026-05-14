<!--
PR title nên theo Conventional Commits:
  feat: thêm tính năng X
  fix: sửa bug Y
  refactor: tổ chức lại Z
  docs: cập nhật README
  test: thêm test cho ABC
  chore: bump dependency
-->

## Mô tả

<!-- Mô tả ngắn gọn thay đổi này làm gì, tại sao cần. -->

## Loại thay đổi

- [ ] feat — Tính năng mới
- [ ] fix — Bug fix
- [ ] refactor — Tổ chức lại code, không đổi behavior
- [ ] docs — Cập nhật documentation
- [ ] test — Thêm/sửa test
- [ ] chore — Maintenance (bump dependency, config)
- [ ] breaking — Breaking change (cần ghi rõ migration plan)

## Service ảnh hưởng

- [ ] AuthService
- [ ] EmailService
- [ ] SmsService
- [ ] FileStorageService
- [ ] BatteryService
- [ ] ApiGateway
- [ ] SharedInfrastructure / SharedContracts / SharedKernels

## Checklist project rules

- [ ] Entity mới extend `AuditableEntity`
- [ ] `UpdateAsync` / `DeleteAsync` KHÔNG bị await (chúng là void)
- [ ] `GetAllAsync` KHÔNG bị await (trả `IQueryable`)
- [ ] Controller THIN — chỉ `_mediator.Send()`
- [ ] Publish event SAU `SaveChangesAsync` (trừ AuthService — Outbox pattern publish TRƯỚC)
- [ ] Consumer mới wrap `ProcessOnceAsync` nếu có side-effect ngoài (gửi email/SMS)
- [ ] Endpoint mutating có `[EnableRateLimiting]` nếu là OTP/anonymous
- [ ] Migration mới đã review (DropColumn/AlterColumn an toàn?)
- [ ] Không hardcode secret / API key / connection string

## Test

- [ ] Unit tests pass local
- [ ] Integration tests pass (nếu touch handler / consumer)
- [ ] Manual test các flow chính
- [ ] CI green

## Cross-service impact

<!-- Nếu PR touch shared contract (event/proto) hoặc API public:
     liệt kê service nào cần update theo. -->

## Migration plan

<!-- Nếu có DB migration:
     - Có DropColumn/DropTable không?
     - Có cần backfill data không?
     - Plan rollback nếu fail? -->

## Screenshot / Demo

<!-- (Optional) Screenshot UI / curl response cho API change. -->
