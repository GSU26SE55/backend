# Chat Hub — Permission Matrix

## Chat Actions

| Action | Admin | Manager | Staff | Customer |
|--------|-------|---------|-------|---------|
| Read public chats | ✅ | ✅ | ✅ | ✅ |
| Read internal chats | ✅ | ✅ | ✅ | ❌ |
| Create public chat | ✅ | ✅ | ✅ | ✅ |
| Create internal chat | ✅ | ✅ | ✅ | ❌ |
| Edit own chat | ✅ | ✅ | ✅ | ✅ (within edit window) |
| Delete own chat | ✅ | ✅ | ✅ | ✅ |
| Delete any chat | ✅ | ✅ | ❌ | ❌ |
| Restore deleted chat | ✅ | ✅ | ❌ | ❌ |
| Pin/unpin chat | ✅ | ✅ | ✅ | ❌ |
| React to chat | ✅ | ✅ | ✅ | ✅ |

## KB Integration (#564)

| Action | Admin | Manager | Staff | Customer |
|--------|-------|---------|-------|---------|
| Attach KB article | ✅ | ✅ | ✅ | ❌ |
| Convert to KB draft | ✅ | ✅ | ✅ | ❌ |
| Get KB suggestions | ✅ | ✅ | ✅ | ❌ |

## Escalation Saga (#566)

| Action | Admin | Manager | Staff | Customer |
|--------|-------|---------|-------|---------|
| Trigger saga (via P1 mention) | ✅ | ✅ | ✅ | ❌ |
| ACK escalation review | ✅ | ✅ | ❌ | ❌ |

## PDF Export (#568)

| Action | Admin | Manager | Staff | Customer |
|--------|-------|---------|-------|---------|
| Export PDF (all chats) | ✅ | ✅ | ✅ | ❌ |

> **Note:** Customer-generated PDF (if ever added) would exclude `IsInternal=true` chats automatically.

## GDPR (#569)

| Action | Any Auth | Notes |
|--------|---------|-------|
| Erase own chat data | ✅ | Only own chats (AuthorUserId match) |
| Admin erase other user | ❌ | Not supported — request via support ticket |

## SignalR Hub

| Action | Admin | Manager | Staff | Customer |
|--------|-------|---------|-------|---------|
| JoinTicket | ✅ | ✅ | ✅ | ✅ (own tickets) |
| LeaveTicket | ✅ | ✅ | ✅ | ✅ |
| Receive public events | ✅ | ✅ | ✅ | ✅ |
| Receive internal events | ✅ | ✅ | ✅ | ❌ |
| Typing indicator | ✅ | ✅ | ✅ | ✅ |
