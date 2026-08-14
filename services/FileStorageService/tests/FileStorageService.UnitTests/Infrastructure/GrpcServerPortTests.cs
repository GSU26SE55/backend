using FileStorageService.Infrastructure.Options;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FileStorageService.UnitTests.Infrastructure;

/// <summary>
/// GH-790 — luật quyết định FileStorageService có khởi động được hay không.
/// </summary>
/// <remarks>
/// Trước đây luật này nằm thẳng trong <c>Program.cs</c> (câu lệnh cấp cao nhất) nên không test nào
/// chạm tới được. Đó chính là lý do <c>env.prod.example</c> và Helm chart thiếu biến suốt một thời
/// gian dài mà không có gì báo — mọi thứ biên dịch, mọi test xanh, và service chỉ chết lúc chạy thật.
/// </remarks>
public class GrpcServerPortTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] entries)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(entries.ToDictionary(e => e.Key, e => e.Value))
            .Build();

    [Fact]
    public void MissingConfiguration_FailsFast_WithAMessageThatNamesTheVariable()
    {
        // Thông báo phải nêu ĐÚNG tên biến: người triển khai đọc log rồi sửa ngay, thay vì đi dò.
        var act = () => GrpcServerPort.Resolve(Config());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*FILE_STORAGE_SERVICE_GRPC_SERVER_PORT*Grpc:Port*");
    }

    [Fact]
    public void PrimaryVariable_IsUsed()
    {
        GrpcServerPort.Resolve(Config((GrpcServerPort.PrimaryKey, "8081"))).Should().Be(8081);
    }

    [Fact]
    public void FallbackKey_StillWorks_SoExistingDeploymentsDoNotBreak()
    {
        // Helm/compose có thể khai theo dạng section (Grpc:Port). Bỏ nhánh này là làm vỡ bản triển
        // khai đang chạy.
        GrpcServerPort.Resolve(Config((GrpcServerPort.FallbackKey, "9090"))).Should().Be(9090);
    }

    [Fact]
    public void PrimaryVariable_WinsOverFallback()
    {
        GrpcServerPort.Resolve(Config(
            (GrpcServerPort.PrimaryKey, "8081"),
            (GrpcServerPort.FallbackKey, "9090"))).Should().Be(8081);
    }

    [Fact]
    public void SameAsHttpPort_IsRejected()
    {
        // Trùng cổng HTTP thì Kestrel không bind được hai giao thức trên cùng cổng — service chết
        // với lỗi khó đọc hơn nhiều so với thông báo này.
        var act = () => GrpcServerPort.Resolve(Config((GrpcServerPort.PrimaryKey, "8080")));

        act.Should().Throw<InvalidOperationException>().WithMessage("*differ from HTTP port*");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("70000")]
    public void OutOfRangePort_IsRejected(string port)
    {
        // Cổng 0 nguy hiểm nhất: service VẪN LÊN (hệ điều hành tự chọn cổng), nhưng địa chỉ mà
        // TicketService gọi tới trỏ vào hư không. Hỏng im lặng còn tệ hơn không lên.
        var act = () => GrpcServerPort.Resolve(Config((GrpcServerPort.PrimaryKey, port)));

        act.Should().Throw<InvalidOperationException>();
    }
}
