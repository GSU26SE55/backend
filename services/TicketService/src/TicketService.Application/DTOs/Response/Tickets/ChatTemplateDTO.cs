using System;
using TicketService.Domain.Enums;

namespace TicketService.Application.DTOs.Response.Tickets;

public class ChatTemplateDTO
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public ChatTemplateCategoryEnum Category { get; set; }
    public bool IsInternalDefault { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public ChatTemplateScopeEnum Scope { get; set; }
    public int UsageCount { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
