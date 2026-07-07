// Chat Hub — TypeScript DTO Types
// Auto-generated from BE response shapes. Wave 6 Phase 8+9.

// ===== Enums =====

export const ActorRoleEnum = {
  Admin: "Admin",
  Manager: "Manager",
  Staff: "Staff",
  Customer: "Customer",
} as const;
export type ActorRoleEnum = (typeof ActorRoleEnum)[keyof typeof ActorRoleEnum];

export const ChatBodyFormatEnum = {
  PlainText: "PlainText",
  Markdown: "Markdown",
} as const;
export type ChatBodyFormatEnum = (typeof ChatBodyFormatEnum)[keyof typeof ChatBodyFormatEnum];

export const KbReferenceTypeEnum = {
  Related: "Related",
  Resolved: "Resolved",
  Source: "Source",
} as const;
export type KbReferenceTypeEnum = (typeof KbReferenceTypeEnum)[keyof typeof KbReferenceTypeEnum];

// ===== Common =====

export interface CommonResponse<T> {
  isSuccess: boolean;
  statusCode: number;
  message?: string;
  data?: T;
  listErrors?: { field: string; detail: string }[];
}

export interface PaginationResponse<T> {
  items: T[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
}

// ===== Chat DTOs =====

export interface TicketChatMentionDTO {
  id: string;
  chatId: string;
  mentionedUserId: string;
  mentionedUserRole: ActorRoleEnum;
  mentionedDisplayName: string;
  isAcknowledged: boolean;
  acknowledgedAt?: string; // ISO 8601
  createdAt: string;
}

export interface TicketChatReactionDTO {
  emoji: string;
  userId: string;
  displayName: string;
  reactedAt: string;
}

export interface TicketChatDTO {
  id: string;
  ticketId: string;
  authorUserId: string;
  authorRole: ActorRoleEnum;
  authorDisplayName?: string;
  body: string;
  bodyHtml?: string;
  bodyFormat: ChatBodyFormatEnum;
  isInternal: boolean;
  isRedacted: boolean; // GDPR #569
  attachmentFileIds: string[];
  editedAt?: string;
  editCount: number;
  parentChatId?: string;
  threadRootId?: string;
  replyCount: number;
  isPinned: boolean;
  pinnedAt?: string;
  mentions: TicketChatMentionDTO[];
  reactions: TicketChatReactionDTO[];
  createdAt: string;
  updatedAt?: string;
}

// ===== KB Integration (#564) =====

export interface KbArticleSuggestDTO {
  id: string;
  code: string;
  title: string;
  /** DTO thực tế trả `symptoms` (triệu chứng) — không phải `category` (fix drift 2026-07-07). */
  symptoms: string;
  helpfulCount: number;
  viewCount: number;
  /**
   * true = bài nội bộ — KHÔNG gán được với referenceType ProvidedToCustomer (BE trả 422).
   * Suggest endpoints lọc sẵn bài nội bộ nên thường luôn false (2026-07-07).
   */
  isInternalOnly: boolean;
}

export interface ChatAttachKbReferencePayload {
  kbArticleId: string;
  referenceType: KbReferenceTypeEnum;
  note?: string;
}

export interface ChatConvertToKbDraftPayload {
  title?: string;
  category?: string;
}

// ===== GDPR (#569) =====

export interface EraseMyDataResponse {
  isSuccess: boolean;
  message: string; // "Đã xóa dữ liệu GDPR: N tin nhắn chat đã được ẩn danh hóa."
}

// ===== Notification Preferences (#570) =====

export interface NotificationPreferenceDTO {
  pushEnabled: boolean;
  emailEnabled: boolean;
  smsEnabled: boolean;
  inAppEnabled: boolean;
  quietHoursStart?: string; // "HH:mm"
  quietHoursEnd?: string;   // "HH:mm"
  timeZone: string;
  notifyOnChat: boolean;
  notifyOnMention: boolean;
  notifyOnReaction: boolean;
  digestWindowMinutes?: number; // null = immediate; 5/15/30 = digest
}

// ===== SignalR Payloads =====

export interface SignalRMentionPayload {
  chatId: string;
  ticketId: string;
  mentionedUserId: string;
  mentionedUserRole: ActorRoleEnum;
  actorUserId: string;
  isGroupMention: boolean;
}

export interface SignalRTypingPayload {
  userId: string;
  displayName: string;
  ticketId: string;
}

export interface SignalRReactionPayload {
  chatId: string;
  emoji: string;
  userId: string;
  displayName?: string;
}
