# SignalR Setup — TicketChatHub

## Hub path
`/hubs/ticket-chats` — JWT qua query string `?access_token=<token>`

## Client methods (client → server)
| Method | Params | Mô tả |
|--------|--------|-------|
| `JoinTicket` | `ticketId: string` | Join group, verify quyền tại Hub |
| `LeaveTicket` | `ticketId: string` | Leave group |
| `Typing` | `ticketId: string` | Broadcast typing indicator |

## Server-push events (server → client)
| Event | Payload | Gửi tới |
|-------|---------|---------|
| `ChatAdded` | `TicketChatDTO` | Group `ticket:{id}:public` hoặc `:internal` |
| `ChatEdited` | `TicketChatDTO` | Cùng group logic |
| `ChatDeleted` | `{ chatId, byUserDisplayName }` | Cùng group logic |
| `ReactionChanged` | `{ chatId, reactions }` | Cùng group logic |
| `UserTyping` | `{ ticketId, userId, displayName }` | Others trong group |
| `MentionReceived` | `TicketChatDTO` | `Clients.User(mentionedUserId)` |

## Redis backplane (multi-instance)

Config trong `appsettings.json`:
```json
{
  "ConnectionStrings": {
    "Redis": "localhost:6379"
  }
}
```

Nếu `Redis` connection string trống → SignalR hoạt động single-instance (no backplane), không crash.

## Authorization
- Hub yêu cầu `[Authorize]`
- JWT phải có claim `AccountId` (hoặc `NameIdentifier`) là `Guid`
- `JoinTicket` verify quyền qua `IChatAuthorizationService.CanAccessTicketAsync`
