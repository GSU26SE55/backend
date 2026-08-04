using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace TicketService.UnitTests.Docs;

/// <summary>
/// <b>Sprint Chat — DoD: "API contract sheet (Postman collection <c>docs/chat/chat-hub.postman.json</c>
/// 40+ request)".</b>
///
/// <para>Một file Postman viết tay hôm nay thì ngày mai lạc hậu, và không ai biết cho tới lúc FE gọi
/// nhầm đường dẫn. Test này đối chiếu bộ sưu tập với <b>controller thật</b>: thêm endpoint mà quên
/// cập nhật collection là đỏ ngay.</para>
///
/// <para>Cách so: chuẩn hoá cả hai bên về dạng <c>VERB /api/.../{}</c> — mọi tham số đường dẫn
/// (<c>{ticketId}</c> phía C#, <c>{{ticketId}}</c> phía Postman) đều quy về <c>{}</c>, vì tên biến
/// hai bên không bắt buộc trùng nhau và cũng không cần trùng.</para>
/// </summary>
public class ChatPostmanCollectionTests
{
    private const int MinimumRequests = 40;

    private static readonly HashSet<string> RetiredEndpoints = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET api/tickets/{}/chats/export-pdf",
        "PATCH api/chats/mentions/{}/acknowledge",
        "POST api/tickets/{}/chats/sentiment-check"
    };

    private static readonly string[] ChatControllers =
    {
        "TicketChatsController.cs",
        "ChatsController.cs",
        "MyChatsController.cs",
        "ChatMentionsController.cs",
        Path.Combine("Admin", "AdminChatSearchController.cs"),
        Path.Combine("Admin", "AdminTicketChatsController.cs"),
    };

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SolarBatteryMaintainance.slnx")))
                dir = dir.Parent;
            dir.Should().NotBeNull("phải tìm được gốc repo từ thư mục chạy test");
            return dir!.FullName;
        }
    }

    private static string CollectionPath =>
        Path.Combine(RepoRoot, "docs", "chat", "chat-hub.postman.json");

    [Fact]
    public void Collection_Exists_AndHasAtLeast40Requests()
    {
        File.Exists(CollectionPath).Should().BeTrue(
            $"DoD Sprint Chat yêu cầu bộ sưu tập tại docs/chat/chat-hub.postman.json (tìm ở {CollectionPath})");

        var requests = ReadCollectionRequests();

        requests.Should().HaveCountGreaterThanOrEqualTo(MinimumRequests,
            $"DoD yêu cầu tối thiểu {MinimumRequests} request; đang có {requests.Count}");
    }

    [Fact]
    public void EveryChatEndpoint_IsCoveredByTheCollection()
    {
        var real = ReadControllerEndpoints();
        var covered = ReadCollectionRequests().Select(r => r.Endpoint).ToHashSet();

        real.Should().NotBeEmpty("phải quét ra được endpoint từ controller, nếu rỗng là regex hỏng");

        var missing = real.Except(covered).OrderBy(x => x).ToList();

        missing.Should().BeEmpty(
            "mỗi endpoint Chat phải có ít nhất một request trong docs/chat/chat-hub.postman.json — " +
            "FE dựa vào file này để tích hợp. Thiếu: " + string.Join(", ", missing));
    }

    [Fact]
    public void CollectionHasNoStaleEndpoint()
    {
        var real = ReadControllerEndpoints();
        var covered = ReadCollectionRequests().Select(r => r.Endpoint).ToHashSet();

        var stale = covered.Except(real).OrderBy(x => x).ToList();

        stale.Should().BeEmpty(
            "bộ sưu tập không được chứa đường dẫn không còn tồn tại — FE gọi theo sẽ ăn 404 và " +
            "mất thời gian truy ngược. Thừa: " + string.Join(", ", stale));
    }

    /// <summary>
    /// Xác thực khai ở cấp bộ sưu tập để mọi request kế thừa. Thiếu chỗ này thì người dùng phải tự
    /// dán token vào từng request — 52 lần.
    /// </summary>
    [Fact]
    public void Collection_DeclaresBearerAuthAtCollectionLevel()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(CollectionPath));
        var root = doc.RootElement;

        root.TryGetProperty("auth", out var auth).Should().BeTrue("bộ sưu tập phải khai auth");
        auth.GetProperty("type").GetString().Should().Be("bearer");

        var token = auth.GetProperty("bearer").EnumerateArray()
            .First(x => x.GetProperty("key").GetString() == "token")
            .GetProperty("value").GetString();
        token.Should().Be("{{accessToken}}");

        var variables = root.GetProperty("variable").EnumerateArray()
            .Select(v => v.GetProperty("key").GetString()).ToList();
        variables.Should().Contain(new[] { "baseUrl", "accessToken", "ticketId", "chatId" });
    }

    /// <summary>
    /// Chỗ-trống của mẫu câu trả lời dùng đúng cú pháp <c>{{tên}}</c> mà Postman dùng cho biến.
    /// Postman chỉ thay thế biến ĐÃ ĐƯỢC KHAI, nên bộ sưu tập tuyệt đối không được khai
    /// <c>customerName</c>/<c>ticketCode</c> — khai vào là Postman nuốt mất chỗ-trống và mẫu tạo ra
    /// thành văn bản chết, một lỗi rất khó nhìn ra.
    /// </summary>
    [Fact]
    public void Collection_DoesNotDeclareVariablesThatWouldEatTemplatePlaceholders()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(CollectionPath));
        var variables = doc.RootElement.GetProperty("variable").EnumerateArray()
            .Select(v => v.GetProperty("key").GetString()).ToList();

        variables.Should().NotContain("customerName");
        variables.Should().NotContain("ticketCode");
    }

    // ───────────────────────────────────────────────────────────────── helper

    private sealed record CollectionRequest(string Name, string Endpoint);

    private static List<CollectionRequest> ReadCollectionRequests()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(CollectionPath));
        var result = new List<CollectionRequest>();

        foreach (var folder in doc.RootElement.GetProperty("item").EnumerateArray())
        {
            if (folder.GetProperty("name").GetString()?.Contains("Chat Template", StringComparison.OrdinalIgnoreCase) == true)
                continue;

            foreach (var item in folder.GetProperty("item").EnumerateArray())
            {
                var request = item.GetProperty("request");
                var method = request.GetProperty("method").GetString()!;
                var raw = request.GetProperty("url").GetProperty("raw").GetString()!;

                var path = raw.Split('?')[0].Replace("{{baseUrl}}/", string.Empty);
                path = Regex.Replace(path, @"\{\{[^}]+\}\}", "{}");

                var endpoint = $"{method} {path}";
                if (!RetiredEndpoints.Contains(endpoint))
                    result.Add(new CollectionRequest(item.GetProperty("name").GetString()!, endpoint));
            }
        }

        return result;
    }

    private static HashSet<string> ReadControllerEndpoints()
    {
        var controllersDir = Path.Combine(RepoRoot, "services", "TicketService", "src",
            "TicketService.Api", "Controllers");

        var endpoints = new HashSet<string>();

        foreach (var relative in ChatControllers)
        {
            var file = Path.Combine(controllersDir, relative);
            File.Exists(file).Should().BeTrue($"không tìm thấy controller {relative} — " +
                                              "đổi tên/di chuyển thì phải cập nhật danh sách trong test này");

            var src = File.ReadAllText(file);
            var route = Regex.Match(src, @"\[Route\(""([^""]+)""\)\]");
            route.Success.Should().BeTrue($"{relative} phải có [Route]");

            // Phải ghi đầy đủ System.Text.RegularExpressions.Match: GlobalUsings của project này kéo
            // cả Moq vào, mà Moq cũng có type tên Match.
            foreach (System.Text.RegularExpressions.Match m in Regex.Matches(
                         src, @"\[Http(Get|Post|Put|Patch|Delete)(?:\(""([^""]*)""\))?\]"))
            {
                var verb = m.Groups[1].Value.ToUpperInvariant();
                var sub = m.Groups[2].Value;
                var path = route.Groups[1].Value + (string.IsNullOrEmpty(sub) ? string.Empty : "/" + sub);
                endpoints.Add($"{verb} {Regex.Replace(path, @"\{[^}]+\}", "{}")}");
            }
        }

        return endpoints;
    }
}
