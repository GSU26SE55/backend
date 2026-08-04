// Chat Hub — TypeScript DTO Types
// Đối chiếu trực tiếp với DTO C# của TicketService. Lần rà gần nhất: 2026-08-02.
//
// ⚠️ Bản trước lệch nặng so với BE: KbReferenceTypeEnum sai toàn bộ giá trị, reactions sai kiểu,
// PaginationResponse sai tên field đếm, mention còn field đã gỡ, thiếu 4 event SignalR.
// Xem ghi chú "SỬA 2026-08-02" tại từng chỗ.

// ===== Enums =====

export const ActorRoleEnum = {
  Admin: "Admin",
  Manager: "Manager",
  Staff: "Staff",
  Customer: "Customer",
  /** SỬA 2026-08-02: BE có System = 5, bản cũ thiếu. */
  System: "System",
} as const;
export type ActorRoleEnum = (typeof ActorRoleEnum)[keyof typeof ActorRoleEnum];

export const ChatBodyFormatEnum = {
  PlainText: "PlainText",
  Markdown: "Markdown",
} as const;
export type ChatBodyFormatEnum = (typeof ChatBodyFormatEnum)[keyof typeof ChatBodyFormatEnum];

/**
 * SỬA 2026-08-02: bản cũ ghi Related/Resolved/Source — KHÔNG giá trị nào tồn tại trong BE.
 * Giá trị thật theo KbReferenceTypeEnum (TicketService.Domain/Enums).
 */
export const KbReferenceTypeEnum = {
  ConsultedDuringResolve: "ConsultedDuringResolve",
  ProvidedToCustomer: "ProvidedToCustomer",
  GeneratedAfterResolve: "GeneratedAfterResolve",
} as const;
export type KbReferenceTypeEnum = (typeof KbReferenceTypeEnum)[keyof typeof KbReferenceTypeEnum];

export const ReactionTypeEnum = {
  ThumbsUp: "ThumbsUp",
  Acknowledged: "Acknowledged",
  Resolved: "Resolved",
  NeedMoreInfo: "NeedMoreInfo",
  Disagree: "Disagree",
} as const;
export type ReactionTypeEnum = (typeof ReactionTypeEnum)[keyof typeof ReactionTypeEnum];

export const VoiceTranscriptionStatusEnum = {
  Pending: "Pending",
  Processing: "Processing",
  Completed: "Completed",
  Failed: "Failed",
} as const;
export type VoiceTranscriptionStatusEnum =
  (typeof VoiceTranscriptionStatusEnum)[keyof typeof VoiceTranscriptionStatusEnum];

// ===== Common =====

export interface CommonResponse<T> {
  isSuccess: boolean;
  statusCode: number;
  message?: string;
  data?: T;
  /** BE serialize list rỗng thành null (ErrorsListJsonConverter) — không bao giờ trả []. */
  listErrors?: { field: string | null; detail: string | null }[] | null;
}

/** SỬA 2026-08-02: field đếm tên là `totalItems`, KHÔNG phải `totalCount`. */
export interface PaginationResponse<T> {
  items: T[];
  totalItems: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
}

export interface CursorPaginationResponse<T> {
  items: T[];
  nextCursor?: string | null;
  hasMore: boolean;
}

// ===== Chat DTOs =====

/**
 * SỬA 2026-08-02: bỏ `isAcknowledged`/`acknowledgedAt` — cơ chế ACK mention đã gỡ (GH-866),
 * endpoint PATCH /api/chats/mentions/{id}/acknowledge không còn tồn tại.
 * Thêm `ticketId` + `isInternal` theo DTO thật.
 */
export interface TicketChatMentionDTO {
  id: string;
  chatId: string;
  ticketId?: string;
  mentionedUserId: string;
  mentionedUserRole: ActorRoleEnum;
  mentionedDisplayName?: string;
  /** Mention nằm trong chat nội bộ — chỉ để chọn view/hiển thị, KHÔNG phải authz check. */
  isInternal: boolean;
  createdAt: string;
}

export interface ChatReactionUserDTO {
  userId: string;
  role: ActorRoleEnum;
}

export interface ChatReactionGroupDTO {
  count: number;
  users: ChatReactionUserDTO[];
}

/**
 * SỬA 2026-08-02: reactions KHÔNG phải mảng `{emoji,userId,...}`.
 * BE trả object gộp sẵn theo đúng 5 loại của ReactionTypeEnum.
 */
export interface TicketChatReactionsAggregateDTO {
  thumbsUp: ChatReactionGroupDTO;
  acknowledged: ChatReactionGroupDTO;
  resolved: ChatReactionGroupDTO;
  needMoreInfo: ChatReactionGroupDTO;
  disagree: ChatReactionGroupDTO;
}

export interface TicketAttachmentDTO {
  id: string;
  ticketId: string;
  chatId?: string;
  uploadedByUserId: string;
  fileId: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  /** AttachmentSourceEnum: CustomerSubmission | StaffWork | MaintenanceLog */
  source: string;
  thumbnailUrl?: string;
  url?: string;
  isInline: boolean;
  downloadCount: number;
  /** VirusScanStatusEnum: Pending | Clean | Infected | Failed — quyết định 200/202/451 khi download. */
  virusScanStatus: string;
  createdAt: string;
}

export interface ChatTranslateDTO {
  translatedBody: string;
  targetLanguage: string;
  originalLanguage?: string;
  provider: string;
  fromCache: boolean;
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
  attachmentFileIds: string[];
  editedAt?: string;
  editCount: number;
  lastEditedByUserId?: string;
  parentChatId?: string;
  threadRootId?: string;
  replyCount: number;
  isPinned: boolean;
  pinnedAt?: string;
  pinnedByUserId?: string;
  /** Chỉ có khi GetById; GetList trả null — dùng attachmentFileIds. */
  attachments?: TicketAttachmentDTO[] | null;
  mentions: TicketChatMentionDTO[];
  reactions: TicketChatReactionsAggregateDTO;
  /** Bản dịch user hiện tại đã yêu cầu — null nếu chưa dịch. */
  activeTranslation?: ChatTranslateDTO | null;
  /** SỬA 2026-08-02: BE trả `isDeleted`; KHÔNG có field `isRedacted`. */
  isDeleted: boolean;
  /** Chỉ có giá trị với chat tạo từ POST .../chats/voice. */
  voiceTranscriptionStatus?: VoiceTranscriptionStatusEnum | null;
  voiceTranscriptionError?: string | null;
  transcribedAt?: string | null;
  createdAt: string;
}

export interface ChatReaderDTO {
  chatId: string;
  userId: string;
  /** Resolve từ CustomerAccounts/StaffAccounts theo role; fallback về userId nếu không tìm thấy. */
  displayName: string;
  role: ActorRoleEnum;
  readAt: string;
}

export interface ChatEditHistoryDTO {
  id: string;
  chatId: string;
  oldBody: string;
  newBody: string;
  editedAt: string;
  editedByUserId: string;
  editedByRole: ActorRoleEnum;
  editReason?: string;
}

export interface ChatBulkDeleteResultDTO {
  deleted: number;
  skipped: number;
  skippedIds: string[];
}

// ===== Request payload =====

export interface ChatAttachmentInput {
  fileId: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  url?: string;
}

export interface ChatMentionInput {
  userId: string;
  displayName: string;
}

/** Whitelist cứng — sai giá trị BE trả 400. */
export interface GroupMentionInput {
  groupType: "role" | "team";
  /** role: manager|staff|admin|customer — team: tier1-staff|tier2-staff|tier3-staff */
  groupIdentifier: string;
}

export interface ChatAddPayload {
  /** Tối đa 10 000 ký tự; không được chỉ chứa khoảng trắng hoặc emoji. */
  body: string;
  isInternal?: boolean;
  bodyFormat?: ChatBodyFormatEnum;
  attachments?: ChatAttachmentInput[];
  mentions?: ChatMentionInput[];
  groupMentions?: GroupMentionInput[];
  requestCustomerInfo?: boolean;
}

// ===== KB Integration (#564) =====

/** SỬA 2026-08-02: DTO thật trả `content` (không phải `symptoms`) và KHÔNG có `isInternalOnly`. */
export interface KbArticleSuggestDTO {
  id: string;
  code: string;
  title: string;
  content: string;
  helpfulCount: number;
  viewCount: number;
}

export interface ChatAttachKbReferencePayload {
  kbArticleId: string;
  referenceType: KbReferenceTypeEnum;
  note?: string;
}

export interface ChatConvertToKbDraftPayload {
  title?: string;
  /** TicketCategoryEnum dạng chuỗi: Charging|Overheat|NoPower|Performance|Other|Repair */
  category?: string;
}

// ===== GDPR (#569) =====

/** `data` luôn null — số lượng đã xoá chỉ nằm trong `message`. */
export type EraseMyDataResponse = CommonResponse<null>;

// ===== Notification Preferences (#570) — thuộc NotificationService =====

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
  digestWindowMinutes?: number; // null = immediate
}

// ===== SignalR — hub /hubs/ticket-chats =====
//
// SỬA 2026-08-02: hub phát ĐÚNG 6 event dưới đây (nguồn: SignalRTicketChatNotifier + TicketChatHub).
// Bản cũ khai SignalRMentionPayload/SignalRReactionPayload với shape tự chế — không khớp BE.
//
//   ChatAdded(chat: TicketChatDTO)                    → group public|internal theo chat.isInternal
//   ChatEdited(chat: TicketChatDTO)                   → group public|internal
//   ChatDeleted(payload: SignalRChatDeletedPayload)   → group public|internal
//   ReactionChanged(payload: SignalRReactionChangedPayload) → group public|internal
//   MentionReceived(chat: TicketChatDTO)              → gửi RIÊNG cho user được mention (Clients.User)
//   UserTyping(ticketId, userId, displayName)         → chỉ group PUBLIC, trừ chính người gõ

export interface SignalRChatDeletedPayload {
  chatId: string;
  byUserDisplayName: string;
}

export interface SignalRReactionChangedPayload {
  chatId: string;
  reactions: TicketChatReactionsAggregateDTO;
}

/** UserTyping gửi 3 tham số rời, không phải 1 object — handler nhận (ticketId, userId, displayName). */
export type SignalRUserTypingArgs = [ticketId: string, userId: string, displayName: string];
