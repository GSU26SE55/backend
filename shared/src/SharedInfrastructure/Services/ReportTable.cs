namespace SharedInfrastructure.Services;

/// <summary>
/// Sprint 7 #114 (§5.3) — bảng phẳng để export. <see cref="Rows"/> mỗi phần tử là 1 hàng,
/// cùng số cột với <see cref="Headers"/>.
/// </summary>
public sealed class ReportTable
{
    public string Name { get; }
    public IReadOnlyList<string> Headers { get; }
    public IReadOnlyList<IReadOnlyList<object?>> Rows { get; }

    public ReportTable(string name, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<object?>> rows)
    {
        Name = string.IsNullOrWhiteSpace(name) ? "report" : name;
        Headers = headers;
        Rows = rows;
    }
}
