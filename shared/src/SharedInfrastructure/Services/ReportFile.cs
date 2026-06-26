namespace SharedInfrastructure.Services;

/// <summary>Sprint 7 #114 (§5.3) — file export kết quả (bytes + content type + tên file).</summary>
public sealed record ReportFile(byte[] Content, string ContentType, string FileName);
