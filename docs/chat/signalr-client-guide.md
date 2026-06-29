# Chat Hub — SignalR Client Guide

## Hub URL

```
wss://<host>/hubs/ticket-chat
```

Auth: Bearer token in query string or Authorization header.

## Web (React) Setup

```bash
npm install @microsoft/signalr
```

```typescript
import * as signalR from "@microsoft/signalr";

const connection = new signalR.HubConnectionBuilder()
  .withUrl("/hubs/ticket-chat", {
    accessTokenFactory: () => getAccessToken(), // returns JWT
  })
  .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
  .configureLogging(signalR.LogLevel.Warning)
  .build();

await connection.start();

// Join a ticket room
await connection.invoke("JoinTicket", ticketId);

// Listen for new chats
connection.on("ChatAdded", (chat: TicketChatDTO) => {
  dispatch(addChat(chat));
});

// Listen for mentions
connection.on("MentionReceived", (chat: TicketChatDTO) => {
  showNotification(`You were mentioned in ticket ${chat.ticketId}`);
});

// Typing indicator
connection.on("TypingStarted", ({ userId, displayName }) => {
  setTypingUsers(prev => ({ ...prev, [userId]: displayName }));
});

// Send typing indicator
await connection.invoke("Typing", ticketId, true); // isTyping
```

## Mobile (Expo) Setup

```bash
npx expo install @microsoft/signalr
```

```typescript
import * as signalR from "@microsoft/signalr";
import { getToken } from "../lib/secureStore";

const connection = new signalR.HubConnectionBuilder()
  .withUrl(`${API_BASE_URL}/hubs/ticket-chat`, {
    accessTokenFactory: async () => (await getToken("accessToken")) ?? "",
  })
  .withAutomaticReconnect()
  .build();

// Usage is identical to web
await connection.start();
await connection.invoke("JoinTicket", ticketId);
```

## Hub Events (Server → Client)

| Event | Payload | Description |
|-------|---------|-------------|
| `ChatAdded` | `TicketChatDTO` | New chat message |
| `ChatEdited` | `TicketChatDTO` | Chat was edited |
| `ChatDeleted` | `{ chatId, ticketId }` | Chat was deleted |
| `ChatRestored` | `TicketChatDTO` | Chat was restored |
| `ReactionAdded` | `{ chatId, emoji, userId }` | Reaction added |
| `ReactionRemoved` | `{ chatId, emoji, userId }` | Reaction removed |
| `MentionReceived` | `TicketChatDTO` | Current user was mentioned |
| `TypingStarted` | `{ userId, displayName, ticketId }` | User started typing |
| `TypingStopped` | `{ userId, ticketId }` | User stopped typing |

## Hub Methods (Client → Server)

| Method | Args | Description |
|--------|------|-------------|
| `JoinTicket` | `ticketId: string` | Subscribe to ticket room |
| `LeaveTicket` | `ticketId: string` | Unsubscribe |
| `Typing` | `ticketId: string, isTyping: bool` | Send typing indicator |

## Reconnection Strategy

`withAutomaticReconnect([0, 2000, 5000, 10000, 30000])` — exponential backoff. On reconnect, re-invoke `JoinTicket` to rejoin the group:

```typescript
connection.onreconnected(async () => {
  await connection.invoke("JoinTicket", currentTicketId);
});
```
