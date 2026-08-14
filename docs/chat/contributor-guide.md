# Chat Hub — Contributor Guide

## Architecture Overview

The Chat Hub (`TicketChatHub`, `TicketChatsController`, `MyChatsController`) is built on Clean Architecture:

```
Api (Controller/Hub)
  → Application (CQRS Commands/Queries/Handlers)
    → Domain (Entities/Enums)
    → Infrastructure (Repository/SignalR/Consumers/Sagas)
```

## Key Components

| Component | File | Purpose |
|-----------|------|---------|
| SignalR Hub | `TicketChatHub.cs` | Real-time broadcast |
| REST | `TicketChatsController.cs` | CRUD + reply + pin + reaction + attachment + KB + AI + voice |
| Outbox writer | `IntegrationEventOutboxWriter` | Reliable event publish |
| Saga | `ChatEscalationReviewSagaStateMachine.cs` | P1 escalation flow |

## Adding a New Chat Feature

1. Add entity field (if needed) in `TicketChat.cs` + EF config + migration
2. Create `CQRS/Command/{Name}` or `CQRS/Query/{Name}` folder
3. Implement handler (inject `ITicketUnitOfWork` only, no DbContext)
4. Register with MediatR via assembly scan (automatic)
5. Add controller action in `TicketChatsController.cs`
6. Publish integration event via `IIntegrationEventOutboxWriter` (not direct bus)
7. **Ghi audit forensic** nếu thao tác làm **thay đổi nội dung hoặc trạng thái hiển thị** của tin nhắn
   (xem mục dưới)
8. **Cập nhật `docs/chat/chat-hub.postman.json`** nếu thêm/xoá endpoint — có test chặn
   (`TicketService.UnitTests/Docs/ChatPostmanCollectionTests.cs`), quên là CI đỏ

### Ghi audit cho thao tác Chat (Sprint Chat DoD, 2026-07-31)

Trước 2026-07-31 module Chat **không ghi audit nào**. Nay 7 action đã có:
`ChatCreated` · `ChatEdited` · `ChatDeleted` · `ChatPinned` · `ChatUnpinned` · `ChatReacted` ·
`ChatMentioned` (`TicketAuditActionEnum` 22–28).

```csharp
// Publish TRƯỚC SaveChangesAsync — audit + outbox + dữ liệu nghiệp vụ nằm CÙNG một transaction.
// Publish sau SaveChanges là audit có thể mất trong khi nghiệp vụ đã ghi (#AUDIT-25/26).
await _publisher.Publish(TicketAuditTrailNotification.For(
    TicketAuditActionEnum.ChatEdited,
    ticket.Id,                                  // ← targetId là TICKET, không phải chat
    targetDisplay: ticket.Code,
    metadata: new Dictionary<string, object?> { ["chatId"] = chat.Id }), ct);

await _uow.SaveChangesAsync(ct);
```

Ba điều bắt buộc theo đúng khuôn hiện có:

- **`targetId` = `ticket.Id`, KHÔNG phải `chat.Id`.** Id tin nhắn đi vào `metadata["chatId"]`. Nhờ vậy
  Admin lọc `?ticketId=` gom được cả thao tác chat của ticket đó. Đặt ngược lại là phá quy ước và
  màn hình Audit Explorer sẽ hiển thị sai nhãn.
- **`targetDisplay` = `ticket.Code`** (mã ticket dạng người đọc được).
- **Severity/category do `For()` quyết định**, đừng tự truyền. Mọi action Chat rơi vào nhánh mặc định
  ⇒ `Info` + `DataModification`.

Thêm action mới thì phải sửa **bốn** chỗ, thiếu chỗ nào cũng lệch:
`TicketAuditActionEnum` → `SharedContracts/Audit/ActionCodes.Ticket` →
`docs/audit/action-code-registry.md` → bảng action trong `docs/api-ticket.md`.

## Anti-Patterns (Do Not Do)

- ❌ `await _uow.TicketChats.UpdateAsync(c)` — UpdateAsync is VOID
- ❌ `await _uow.TicketChats.GetAllAsync()` — GetAllAsync is SYNC
- ❌ Inject `DbContext` in handlers — use `ITicketUnitOfWork`
- ❌ Publish events before `CommitTransactionAsync` (without outbox)
- ❌ Hard-delete chat rows (breaks FK audit trail)
- ❌ Publish `TicketAuditTrailNotification` **sau** `SaveChangesAsync` — audit rơi ra ngoài transaction
- ❌ Đặt `chat.Id` vào `targetId` của audit — phải là `ticket.Id`, chat id nằm ở `metadata["chatId"]`

## Running Locally

```bash
# Start TicketService
cd services/TicketService/src/TicketService.Api
dotnet run

# Run migrations
dotnet ef database update -p ../TicketService.Infrastructure -s .

# Run tests
cd ../../../tests
dotnet test --no-build --verbosity minimal
```
