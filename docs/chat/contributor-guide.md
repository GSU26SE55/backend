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
| REST | `TicketChatsController.cs` | CRUD + KB + GDPR + PDF |
| Outbox writer | `IntegrationEventOutboxWriter` | Reliable event publish |
| Saga | `ChatEscalationReviewSagaStateMachine.cs` | P1 escalation flow |

## Adding a New Chat Feature

1. Add entity field (if needed) in `TicketChat.cs` + EF config + migration
2. Create `CQRS/Command/{Name}` or `CQRS/Query/{Name}` folder
3. Implement handler (inject `ITicketUnitOfWork` only, no DbContext)
4. Register with MediatR via assembly scan (automatic)
5. Add controller action in `TicketChatsController.cs`
6. Publish integration event via `IIntegrationEventOutboxWriter` (not direct bus)

## Anti-Patterns (Do Not Do)

- ❌ `await _uow.TicketChats.UpdateAsync(c)` — UpdateAsync is VOID
- ❌ `await _uow.TicketChats.GetAllAsync()` — GetAllAsync is SYNC
- ❌ Inject `DbContext` in handlers — use `ITicketUnitOfWork`
- ❌ Publish events before `CommitTransactionAsync` (without outbox)
- ❌ Hard-delete chat rows (breaks FK audit trail)

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
