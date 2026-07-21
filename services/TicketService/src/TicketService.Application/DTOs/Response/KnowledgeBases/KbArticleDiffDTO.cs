namespace TicketService.Application.DTOs.Response.KnowledgeBases;

public class KbArticleDiffDTO
{
    /// <summary>
    /// From version.
    /// </summary>
    public string FromVersion { get; set; } = string.Empty;
    public string ToVersion { get; set; } = string.Empty;
    public DiffSection TitleDiff { get; set; } = new();
    public DiffSection ContentDiff { get; set; } = new();
    public DiffSection TagsDiff { get; set; } = new();
}

public class DiffSection
{
    /// <summary>
    /// Old value.
    /// </summary>
    public string OldValue { get; set; } = string.Empty;
    public string NewValue { get; set; } = string.Empty;
    public bool IsChanged { get; set; }
}
