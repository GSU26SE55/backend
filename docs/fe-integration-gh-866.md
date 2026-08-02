# GH-866 — Ticket and Chat integration contract

This document supersedes the removed Chat Template, chat PDF export, sentiment analysis, and mention acknowledge flows.

## Customer ticket creation

`POST /api/customer/tickets` accepts one point-in-time `incidentDetectedAt`, not an incident range. It must be UTC and not in the future.

`batteryAssetIds` is required, contains one or more distinct non-empty IDs, and every asset must be authorized for the customer. The automatic ticket creation command is unchanged.

Ticket attachments are client-supplied metadata:

```json
{
  "fileId": "guid",
  "fileName": "photo.jpg",
  "contentType": "image/jpeg",
  "sizeBytes": 12345,
  "url": "https://..."
}
```

The API validates structure and rejects duplicate `fileId` values in a request. An active `fileId` cannot be attached twice to the same ticket. File ownership is intentionally not checked by TicketService.

## Chat duplicate protection

Two identical chat bodies from the same user on the same ticket are accepted within five minutes. The third is rejected with status `400` and message `CHAT_DUPLICATE_MESSAGE_LIMIT`; it is not persisted. This is distinct from HTTP `429` rate limiting.

Concurrent spam checks may return `409` with `CHAT_SPAM_CHECK_IN_PROGRESS`. Clients may retry that response with a short backoff.

## Mentions and chat visibility

Use the existing `GET /api/chats/mentions/me` endpoint. It no longer accepts `unreadOnly` and there is no acknowledge endpoint.

Every mention includes `isInternal`:

```json
{
  "id": "guid",
  "chatId": "guid",
  "ticketId": "guid",
  "mentionedUserId": "guid",
  "mentionedUserRole": 2,
  "mentionedDisplayName": "Staff A",
  "isInternal": false,
  "createdAt": "2026-08-02T13:24:00Z"
}
```

Use `ticketId` and `chatId` to open and focus the source conversation. Use `isInternal` to select the public/internal chat view and show the correct visual indicator. It is not an authorization check: the backend filters the mentions and validates access again for every chat API.

- Customers receive only their own mentions from public chats.
- Staff, Manager, and Admin receive internal mentions only when they are authorized active participants of the ticket.
- Mention objects embedded in chat responses also include `isInternal`.

## Removed endpoints

Do not call these endpoints:

- `POST /api/tickets/{ticketId}/chats/from-template/{templateId}`
- `GET /api/tickets/{ticketId}/chats/export-pdf`
- `POST /api/tickets/{ticketId}/chats/sentiment-check`
- `PATCH /api/chats/mentions/{id}/acknowledge`

Chat Template APIs have also been removed permanently.

## Manager queue

Queue items and `totalItems` include only tickets that are `New`, not deleted, and not merged.
