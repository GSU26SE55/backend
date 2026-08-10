using SharedContracts.Events.Root;

namespace SharedInfrastructure.UnitTests.Contracts;

/// <summary>
/// GH-792 — ID message phải suy ra được từ dữ liệu nghiệp vụ, để lần phát lại của cùng một việc
/// mang đúng ID cũ và bị phía nhận nhận ra là trùng.
/// </summary>
public class DeterministicEventIdTests
{
    [Fact]
    public void SameInput_AlwaysGivesTheSameId()
    {
        // Đây là toàn bộ lý do tồn tại của lớp này: gửi lại phải "trông giống" lần gửi trước.
        var scope = Guid.NewGuid();

        DeterministicEventId.From(scope, "email")
            .Should().Be(DeterministicEventId.From(scope, "email"));
    }

    [Fact]
    public void IdIsStableAcrossProcessRestarts_NotJustWithinOneRun()
    {
        // Ghim giá trị cụ thể: nếu ai đó đổi thuật toán hoặc đổi namespace, mọi bản ghi chống trùng
        // đã lưu trở nên vô dụng và đợt gửi lại đầu tiên sau đó sẽ trùng hàng loạt. Test này biến
        // thay đổi âm thầm đó thành một lần đỏ rõ ràng.
        var scope = Guid.Parse("11111111-2222-3333-4444-555555555555");

        DeterministicEventId.From(scope, "email").ToString()
            .Should().Be("94c82568-8567-8c9c-b25e-bc1f5aaf8a6e");
    }

    [Fact]
    public void DifferentDiscriminator_GivesDifferentId()
    {
        // Một notification có thể sinh ra nhiều message khác nhau. Trùng ID nghĩa là message thứ hai
        // bị coi là bản sao rồi vứt đi — người dùng mất hẳn một kênh.
        var scope = Guid.NewGuid();

        DeterministicEventId.From(scope, "email")
            .Should().NotBe(DeterministicEventId.From(scope, "sms"));
    }

    [Fact]
    public void DifferentScope_GivesDifferentId()
    {
        DeterministicEventId.From(Guid.NewGuid(), "email")
            .Should().NotBe(DeterministicEventId.From(Guid.NewGuid(), "email"));
    }

    [Fact]
    public void GeneratedId_IsNotEmpty()
    {
        // Guid.Empty là giá trị "chưa gán" ở nhiều chỗ trong repo; trả về nó sẽ khiến mọi message
        // dùng chung một khoá chống trùng.
        DeterministicEventId.From(Guid.NewGuid(), "email").Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void GeneratedId_CarriesVersionAndVariantBits()
    {
        // Đánh dấu version 8 (custom, RFC 9562) để người điều tra sự cố nhìn ID là biết nó được sinh
        // theo quy tắc, không phải GUID ngẫu nhiên — khác biệt đó quyết định cách họ truy vết.
        var bytes = DeterministicEventId.From(Guid.NewGuid(), "email").ToByteArray();

        (bytes[7] & 0xF0).Should().Be(0x80, "version 8");
        (bytes[8] & 0xC0).Should().Be(0x80, "variant RFC 4122");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyName_IsRejected(string? name)
    {
        // Tên rỗng sẽ cho mọi lời gọi cùng một ID — hỏng âm thầm và rất khó lần ra.
        var act = () => DeterministicEventId.From(name!);

        act.Should().Throw<ArgumentException>();
    }
}
