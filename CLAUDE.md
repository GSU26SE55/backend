# Backend — GSU26SE55

> Repo này là một BE microservice trong hệ thống Solar Battery Maintenance.
> Context dự án đầy đủ: `.claude/CLAUDE.md` | Rules đầy đủ: `.claude/rules/tech/be.md`

---

## ⚠️ Critical — hay sai nhất

```csharp
// ✅ ĐÚNG
var q = _unitOfWork.Batteries.GetAllAsync().Where(x => !x.IsDeleted); // SYNC
_unitOfWork.Batteries.UpdateAsync(entity);  // VOID — không await
_unitOfWork.Batteries.DeleteAsync(entity);  // VOID — không await
await _unitOfWork.Batteries.AddAsync(entity); // async — CÓ await

// ❌ SAI
var q = await _unitOfWork.Batteries.GetAllAsync(); // GetAllAsync là SYNC
await _unitOfWork.Batteries.UpdateAsync(entity);   // UpdateAsync là VOID
```

- Entity **PHẢI** extend `AuditableEntity`
- Enum bắt đầu từ `1`, không phải `0`
- Controller chỉ gọi `_mediator.Send()` — không chứa logic
- Handler chỉ inject `IUnitOfWork` — không inject `DbContext` trực tiếp

---

## Scaffold nhanh

| Lệnh | Output |
|------|--------|
| `/scaffold-crud {Service} {Entity}` | 16 files + migration |
| `/scaffold-entity {Service} {Entity}` | Entity + DbSet |
| `/scaffold-cqrs-command {Service} {Entity} {Action}` | Command + Handler |
| `/scaffold-cqrs-query {Service} {Entity} GetList\|GetById` | Query + Handler |
| `/scaffold-controller {Service} {Entity}` | Controller |
| `/scaffold-consumer {Service} {EventName}` | Consumer |
| `/scaffold-integration-event {EventName}` | Integration event |
| `/scaffold-unit-tests {Service} {Entity}` | Unit tests |
| `/run-migration {Service} {MigrationName}` | EF migration |

---

## Workflow

```
/kltn-implement [issue-number] → plan.md → approve → code → /kltn-reviewcode → /kltn-test → /kltn-ship [issue-number]
```
