namespace TicketService.Application.DTOs.Response.KnowledgeBase;

public class KbArticleDiffDto
{
    public string FromVersion { get; set; } = string.Empty;
    public string ToVersion { get; set; } = string.Empty;
    public DiffSection TitleDiff { get; set; } = new();
    public DiffSection SymptomsDiff { get; set; } = new();
    public DiffSection DiagnosisStepsDiff { get; set; } = new();
    public DiffSection SolutionStepsDiff { get; set; } = new();
    public DiffSection TagsDiff { get; set; } = new();
}

public class DiffSection
{
    public string OldValue { get; set; } = string.Empty;
    public string NewValue { get; set; } = string.Empty;
    public bool IsChanged { get; set; }
}
