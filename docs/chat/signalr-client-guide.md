# Chat Hub — SignalR Client Guide

> 🔴 **Sửa nặng 2026-08-02.** Bản trước sai những thứ khiến client **không chạy được**:
> hub URL thiếu chữ `s` (`/hubs/ticket-chat`), `Typing` truyền 2 tham số, và **5 event bịa**
> (`ChatRestored`, `ReactionAdded`, `ReactionRemoved`, `TypingStarted`, `TypingStopped`).
> Nguồn đúng: `TicketChatHub.cs` + `SignalRTicketChatNotifier.cs`.

## Hub URL

```
wss://<host>/hubs/ticket-chats        ← CÓ chữ "s" ở cuối
```

> ⚠️ Bản cũ ghi `/hubs/ticket-chat` (thiếu `s`) — sai đường dẫn, connect sẽ fail.
> ApiGateway đã proxy sẵn `/hubs/ticket-chats` (`ticket-chats-hub-route`) → `ticketCluster`.

**Auth:** JWT. SignalR JS client **không gửi header** lúc bắt tay WebSocket — server đọc token từ
query `?access_token=...` (`Program.cs` override `JwtBearerEvents.OnMessageReceived`).
Dùng `accessTokenFactory` thì client lib tự ghép, không cần tự nối chuỗi.

## Web (React) Setup

```bash
npm install @microsoft/signalr
```

```typescript
import * as signalR from "@microsoft/signalr";

const connection = new signalR.HubConnectionBuilder()
  .withUrl("/hubs/ticket-chats", {
    accessTokenFactory: () => getAccessToken(), // luôn trả token MỚI NHẤT
  })
  .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
  .configureLogging(signalR.LogLevel.Warning)
  .build();

await connection.start();
await connection.invoke("JoinTicket", ticketId);   // BẮT BUỘC — hub không tự join group

connection.on("ChatAdded",   (chat) => dispatch(addChat(chat)));
connection.on("ChatEdited",  (chat) => dispatch(updateChat(chat)));

connection.on("ChatDeleted", ({ chatId, byUserDisplayName }) =>
  dispatch(removeChat(chatId)));

connection.on("ReactionChanged", ({ chatId, reactions }) =>
  dispatch(setReactions({ chatId, reactions })));   // reactions = object gộp 5 loại

connection.on("MentionReceived", (chat) =>
  showNotification(`Bạn được nhắc tên trong ticket ${chat.ticketId}`));

// UserTyping nhận 3 THAM SỐ RỜI, không phải object
connection.on("UserTyping", (ticketId, userId, displayName) =>
  setTypingUsers(prev => ({ ...prev, [userId]: displayName })));

// Gửi typing — CHỈ 1 tham số
await connection.invoke("Typing", ticketId);
```

## Mobile (Expo) Setup

```bash
npx expo install @microsoft/signalr
```

```typescript
const connection = new signalR.HubConnectionBuilder()
  .withUrl(`${API_BASE_URL}/hubs/ticket-chats`, {
    accessTokenFactory: async () => (await getToken("accessToken")) ?? "",
  })
  .withAutomaticReconnect()
  .build();

await connection.start();
await connection.invoke("JoinTicket", ticketId);
```

## Hub Events (Server → Client) — **đúng 6 event**

| Event | Payload | Gửi tới |
|-------|---------|---------|
| `ChatAdded` | `TicketChatDTO` | group `ticket:{id}:public` hoặc `:internal` theo `chat.isInternal` |
| `ChatEdited` | `TicketChatDTO` | như trên |
| `ChatDeleted` | `{ chatId, byUserDisplayName }` | như trên |
| `ReactionChanged` | `{ chatId, reactions }` — `reactions` là `TicketChatReactionsAggregateDTO` | như trên |
| `MentionReceived` | `TicketChatDTO` | **`Clients.User(mentionedUserId)`** — không cần JoinTicket |
| `UserTyping` | **3 tham số rời**: `(ticketId, userId, displayName)` | chỉ group **public**, trừ chính người gõ |

> ⚠️ **Event KHÔNG tồn tại** (bản cũ bịa): `ChatRestored`, `ReactionAdded`, `ReactionRemoved`,
> `TypingStarted`, `TypingStopped`. Đăng ký handler cho chúng sẽ **không bao giờ chạy**.
>
> - Reaction: chỉ có **`ReactionChanged`** và payload trả **toàn bộ cụm reaction đã gộp**, không phải
>   từng emoji lẻ. Cứ replace nguyên cụm, đừng cộng/trừ thủ công.
> - Khôi phục chat (`PATCH .../restore`, Admin only) **không phát event** — phải refetch.
> - `ChatDeleted` payload là `{ chatId, byUserDisplayName }` — **không có** `ticketId`.

## Hub Methods (Client → Server)

| Method | Args | Mô tả |
|--------|------|-------|
| `JoinTicket` | `ticketId: string` | Bắt buộc gọi sau mỗi lần connect/reconnect. Sai format Guid → `HubException("Invalid ticket ID format.")`; không có quyền → `HubException("Forbidden: No access to this ticket.")` |
| `LeaveTicket` | `ticketId: string` | Rời cả 2 group. Sai format → no-op |
| `Typing` | `ticketId: string` | **CHỈ 1 tham số.** Không có quyền → no-op |

> ⚠️ Bản cũ ghi `Typing(ticketId, isTyping: bool)` — **sai**, hub chỉ nhận `Typing(string ticketIdStr)`.
> Truyền thừa tham số sẽ lỗi invoke. Cũng không có cơ chế "typing stopped" — client tự đặt timeout để tắt chỉ báo.

## Group routing

- `JoinTicket` luôn join `ticket:{id}:public`.
- Role ∈ Admin/Manager/Staff join **thêm** `ticket:{id}:internal` ⇒ nhận cả chat `isInternal = true`.
- Customer chỉ ở group public.

## Reconnection

Hub **không tự join group** khi connect (`OnConnectedAsync` không override) ⇒ **phải gọi lại
`JoinTicket` sau mỗi lần reconnect**:

```typescript
connection.onreconnected(async () => {
  await connection.invoke("JoinTicket", currentTicketId);
});
```

Token hết hạn giữa session: hub không tự refresh — connection đóng. `accessTokenFactory` phải trả
token mới nhất mỗi lần reconnect.

## Multi-instance

Nếu backend chạy nhiều replica mà **thiếu `ConnectionStrings:Redis`**, SignalR không có backplane:
client nối instance A **không nhận** event phát từ instance B. Fallback im lặng, không log lỗi.

## Hub options (server-side)

| Option | Giá trị |
|---|---|
| `KeepAliveInterval` | 15 giây |
| `ClientTimeoutInterval` | 60 giây |
| `EnableDetailedErrors` | `true` (Development) / `false` (Production) |
| JSON protocol | camelCase + `JsonStringEnumConverter` |

> Client phải khai JSON protocol khớp server (camelCase + enum dạng chuỗi). Lệch là callback **im lặng
> không chạy**, nhìn hệt như tin nhắn bị rơi.
