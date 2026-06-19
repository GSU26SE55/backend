using SharedContracts.Common.Responses;

namespace TicketService.Application.DTOs.Response.KnowledgeBases;

public class KbArticleActionResponse : CommonResponse<KbArticleActionDTO> { }
public class KbArticleResponse : CommonResponse<KbArticleDTO> { }
public class KbArticlesResponse : CommonResponse<KbArticleListItemDTO> { }
public class KbArticleDiffResponse : CommonResponse<KbArticleDiffDTO> { }
public class KbArticleVersionResponse : CommonResponse<KbArticleVersionDTO> { }
public class KbArticleVersionsResponse : CommonResponse<List<KbArticleVersionDTO>> { }
public class KbArticleSuggestResponse : CommonResponse<KbArticleSuggestDTO> { }
public class KbArticleCreatedResponse : CommonResponse<KbArticleCreatedDTO> { }
public class KbArticleTemplateResponse : CommonResponse<KbArticleTemplateDTO> { }
