# Plan — GH-86: Implement Ticket Query Handlers

## Metadata
- **Status:** SHIPPED
- **Role:** BE
- **Ngày:** 2026-05-30
- **Issue:** #86
- **PR:** #213 — https://github.com/GSU26SE55/backend/pull/213
- **Sprint:** Sprint 3

## Mục tiêu
Implement 6 query handlers cho TicketService: GetList (Admin/Manager), GetById (all roles), ManagerQueue (Manager), MyTicketsAsCustomer (Customer), MyTicketsAsStaff (Staff), ActivityTimeline (all roles). Kèm access control per-role và SLA timer mapping.

## Files
| File | Action | Ghi chú |
|------|--------|---------|
| `src/TicketService.Application/CQRS/Query/TicketGetList/TicketGetListQuery.cs` | create | Admin/Manager filter query |
| `src/TicketService.Application/CQRS/Query/TicketGetList/TicketGetListQueryHandler.cs` | create | |
| `src/TicketService.Application/CQRS/Query/TicketGetById/TicketGetByIdQuery.cs` | create | With access control |
| `src/TicketService.Application/CQRS/Query/TicketGetById/TicketGetByIdQueryHandler.cs` | create | |
| `src/TicketService.Application/CQRS/Query/ManagerQueue/ManagerQueueQuery.cs` | create | Open tickets sorted by priority |
| `src/TicketService.Application/CQRS/Query/ManagerQueue/ManagerQueueQueryHandler.cs` | create | |
| `src/TicketService.Application/CQRS/Query/MyTicketsAsCustomer/MyTicketsAsCustomerQuery.cs` | create | |
| `src/TicketService.Application/CQRS/Query/MyTicketsAsCustomer/MyTicketsAsCustomerQueryHandler.cs` | create | |
| `src/TicketService.Application/CQRS/Query/MyTicketsAsStaff/MyTicketsAsStaffQuery.cs` | create | |
| `src/TicketService.Application/CQRS/Query/MyTicketsAsStaff/MyTicketsAsStaffQueryHandler.cs` | create | |
| `src/TicketService.Application/CQRS/Query/TicketActivityTimeline/TicketActivityTimelineQuery.cs` | create | |
| `src/TicketService.Application/CQRS/Query/TicketActivityTimeline/TicketActivityTimelineQueryHandler.cs` | create | |
| `src/TicketService.Application/Helpers/TicketQueryHelper.cs` | create | Shared mapping + access control |
| `src/TicketService.Application/DTOs/Response/TicketDTO.cs` | create | |
| `src/TicketService.Application/DTOs/Response/TicketDetailDTO.cs` | create | |
| `src/TicketService.Application/DTOs/Response/SlaTimerDTO.cs` | create | |
| `src/TicketService.Application/DTOs/Response/TicketActivityDTO.cs` | create | |
| `src/TicketService.Application/DTOs/Response/TicketCommentDTO.cs` | create | |
| `src/TicketService.Application/DTOs/Response/MaintenanceLogDTO.cs` | create | |
| `src/TicketService.Api/Controllers/TicketController.cs` | create | 6 endpoints |
| `tests/TicketService.UnitTests/` | create | 29 unit tests, coverage 82% |

## Approach
- CQRS pattern: 1 Query + 1 Handler per folder
- Access control qua helper `TicketQueryHelper.CanAccessTicket` (Customer chỉ xem ticket của mình, Staff chỉ xem ticket được assign)
- Mapping tập trung trong `TicketQueryHelper.MapToTicketDTO` / `MapToSlaTimerDTO` — tránh duplicate 4 lần
- Controller nhận userId + roles từ JWT claim, truyền vào query

## Steps
- [x] Implement 6 query classes
- [x] Implement 6 query handler classes
- [x] Tạo 9 DTO response files
- [x] Tạo TicketController với 6 endpoints
- [x] Refactor: extract TicketQueryHelper (MapToTicketDTO, MapToSlaTimerDTO, CanAccessTicket)
- [x] Viết 29 unit tests — PASS, coverage 82.15%
