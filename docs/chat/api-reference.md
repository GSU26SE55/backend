# Chat Hub — API Reference

All endpoints under `/api/tickets/{ticketId}/chats` unless noted.
Auth: Bearer JWT. Role-specific access noted per endpoint.

## Chat CRUD

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/api/tickets/{ticketId}/chats` | Any | Paginated chat list |
| GET | `/api/tickets/{ticketId}/chats/{id}` | Any | Chat detail |
| POST | `/api/tickets/{ticketId}/chats` | Any (active participant) | Create chat |
| PUT | `/api/tickets/{ticketId}/chats/{id}` | Author | Edit chat |
| DELETE | `/api/tickets/{ticketId}/chats/{id}` | Author/Manager/Admin | Soft delete |
| PATCH | `/api/tickets/{ticketId}/chats/{id}/restore` | Manager/Admin | Restore |

## Reactions & Reads

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| POST | `.../chats/{id}/react` | Any participant | Add reaction |
| DELETE | `.../chats/{id}/react/{emoji}` | Author of reaction | Remove reaction |
| POST | `.../chats/mark-as-read` | Any | Bulk mark read |

## KB Integration (#564)

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| POST | `.../chats/{id}/attach-kb` | Staff/Manager/Admin | Attach KB article |
| POST | `.../chats/{id}/to-kb-draft` | Staff/Manager/Admin | Convert to KB draft |
| GET | `.../chats/{id}/kb-suggestions?topN=3` | Staff/Manager/Admin | Suggest KB articles |

### KB Suggestion Response
```json
{
  "isSuccess": true,
  "data": [
    {
      "id": "...",
      "code": "KB-001",
      "title": "How to check battery voltage",
      "category": "Electrical",
      "helpfulCount": 15
    }
  ]
}
```

## Escalation Saga (#566)

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| POST | `.../chats/{id}/escalation-review/ack` | Manager/Admin | ACK review within 30 min |

## PDF Export (#568)

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/api/tickets/{ticketId}/chats/export-pdf` | Staff/Manager/Admin | Download PDF |

Response: `Content-Type: application/pdf`, filename `ticket-{id}-chats.pdf`

## GDPR (#569)

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| POST | `/api/chats/erase-my-data` | Any authenticated | Erase own chat data |

## My Chats

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/api/chats` | Any | List chats authored by current user |

## Notification Preferences (#570)

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/api/notification-preferences` | Any | Get preference |
| PUT | `/api/notification-preferences` | Any | Update preference |

Fields: `notifyOnChat`, `notifyOnMention`, `notifyOnReaction`, `digestWindowMinutes`
