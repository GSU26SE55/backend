# ADR 0008 — Ticket Chat Hub Architecture

**Date:** 2026-06-26
**Status:** Accepted
**Scope:** TicketService — Wave 6 Phase 8+9

## Context

The system needed real-time chat within maintenance tickets to allow Staff, Managers, and Customers to communicate without leaving the platform. Key requirements:
- Real-time broadcast to all participants
- Internal (staff-only) and public chat segregation
- Mentions, reactions, threading, pinning
- GDPR compliance (right-to-erasure)
- KB integration (attach/suggest)
- P1 escalation when Manager is mentioned

## Decision

### 1. SignalR for Real-Time

Use ASP.NET Core SignalR (`TicketChatHub`) with group-per-ticket pattern:
- `ticket:{id}:public` — all participants
- `ticket:{id}:internal` — Staff/Manager/Admin only

**Rejected alternative:** Long-polling — too resource-intensive for many concurrent tickets.

### 2. Outbox Pattern for Event Reliability

All integration events (`ChatCreatedEvent`, `ChatMentionedEvent`, etc.) go through `IIntegrationEventOutboxWriter` → persisted to `outbox_messages` table → `OutboxRelayBackgroundService` publishes to RabbitMQ.

**Why:** Direct publish to RabbitMQ inside a DB transaction would risk event loss on crash. Outbox ensures at-least-once delivery.

### 3. Saga for P1 Escalation (#566)

`ChatEscalationReviewSagaStateMachine` (MassTransit + Quartz) handles the 30-minute review window when Manager is mentioned on P1 ticket. Quartz provides persistent timeout even across service restarts.

**Why not simple delayed task:** `Task.Delay` is lost on restart; Quartz persists to PostgreSQL.

### 4. Soft Delete + Redaction for GDPR (#569)

Chat rows are NEVER hard-deleted. GDPR erasure replaces `Body` with `[REDACTED — GDPR erasure]` and sets `IsRedacted = true`. Retention archiving sets `IsDeleted = true` after 2 years.

**Why:** `ticket_activities`, `ticket_chat_mentions`, `ticket_chat_reads` all FK-reference chat rows. Hard delete would cascade or orphan them.

### 5. QuestPDF for PDF Export (#568)

Community license, no server-side dependency. Pure .NET, no Chromium.

**Rejected alternatives:** Puppeteer (requires Chromium, heavy), WeasyPrint (Python).

### 6. EF ILike for KB Suggestions (#564)

PostgreSQL `ILike` for case-insensitive keyword matching instead of full-text `ts_rank`. Simpler implementation, sufficient for capstone scope.

**Future:** Replace with PostgreSQL `tsvector` if KB grows > 10k articles.

## Consequences

- SignalR groups are in-memory — horizontal scaling requires Redis backplane (add Sprint 8 if needed)
- Quartz cluster mode enabled (PostgreSQL) — supports multi-instance deployment
- Outbox adds ~1 second latency to first SignalR broadcast (relay job interval)
