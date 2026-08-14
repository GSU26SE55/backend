namespace TicketService.Application.Common.Models;

/// <summary>
/// Literal permission code dùng phía TicketService cho chat — PHẢI khớp giá trị string với
/// <c>AuthService.Application.Authorization.PermissionCodes</c> (2 service độc lập, không share assembly).
/// Nguồn JWT claim <c>perm</c> được AuthService cấp theo đúng các giá trị này.
/// </summary>
public static class ChatPermissionCodes
{
    public const string ChatCreatePublic = "chat.create.public";
    public const string ChatCreateInternal = "chat.create.internal";
    public const string ChatEditOwn = "chat.edit.own";
    public const string ChatDeleteOwn = "chat.delete.own";
    public const string ChatPin = "chat.pin";
    public const string ChatViewInternal = "chat.view.internal";
}
