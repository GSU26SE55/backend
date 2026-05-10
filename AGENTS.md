# Codex Project Instructions - SolarBatteryMaintainance

This repository contains the SU27SE001 Solar Battery Maintenance backend and SRS material.

Codex reads this file automatically when started from the repository root. For any non-trivial task, also read:

- `.codex/AGENTS.md` - full Codex working guide for this project.
- `.codex/context/project-context.md` - current architecture, services, ports, commands.
- `.codex/context/business-flow.md` - SU27SE001 Ticket, SLA, Battery business flow.
- `.codex/project-context-full.md` - full detailed project context.
- `.codex/reference/INDEX.md` - detailed Codex reference index if a task needs deeper rules/recipes.
- `srs.html` - visual business flow and SRS-ready diagrams.

## Critical Context

- Business scope: Solar Battery Maintenance, ITIL Ticket & SLA Management.
- Roles: Admin, Manager, Staff, Customer, System.
- Current codebase: foundation backend services exist; core Battery/Ticket/SLA domain is documented but not fully implemented.
- Prefer a future `MaintenanceService` for BatteryAsset, Alert, Ticket, SLA, MaintenanceLog, Feedback, KnowledgeBase, and Incident.
- Keep AuthService focused on identity/account/role/session/auth flows.

## Detailed Codex Reference

The `.codex` folder contains the full detailed rule/workflow/scaffold reference for this project.

Use it when the summarized Codex context is not enough:

- `.codex/rules/*` for full coding rules.
- `.codex/commands/*` for workflow commands.
- `.codex/agents/*` for audit/build/design agent instructions.
- `.codex/skills/*` for scaffold recipes.
- `.codex/hooks/*` for hook intent.

## Current Services

- ApiGateway: YARP reverse proxy.
- AuthService: accounts, roles, OTP, JWT, refresh tokens, 2FA, Google OAuth, admin account/role/session management. Owns PostgreSQL DB.
- EmailService: RabbitMQ consumers and Mailjet email sending.
- SmsService: RabbitMQ consumer and fake SMS sender for dev.
- FileStorageService: S3-compatible upload/download/presigned URL/delete.
- Shared: contracts, kernels, infrastructure, middleware, idempotency, Redis, MassTransit helpers.

## Hard Rules

- Use Clean Architecture boundaries.
- Controllers must stay thin and call MediatR.
- Domain entities must extend `AuditableEntity` unless there is a deliberate exception.
- `UpdateAsync()` and `DeleteAsync()` are void in the repository pattern. Do not `await` them.
- `GetAllAsync()` returns `IQueryable<T>`. Do not `await` it.
- AuthService uses Outbox: publish integration events before `SaveChangesAsync`/commit.
- Services without Outbox publish integration events after commit.
- Consumers with external side effects must use Inbox/idempotent processing.
- Do not print `.env` or `.env.Docker`; they may contain secrets.

## Verification

- For C# changes, run targeted `dotnet build` or `dotnet test` for the affected project.
- For shared code changes, build/test the solution or all impacted services.
- For docs/context-only changes, no runtime tests are required, but verify file paths and markdown readability.
