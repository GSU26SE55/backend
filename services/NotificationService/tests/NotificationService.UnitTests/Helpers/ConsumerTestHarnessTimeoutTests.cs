using MassTransit.Testing;
using NotificationService.Application.Consumers;

namespace NotificationService.UnitTests.Helpers;

/// <summary>
/// Chặn hồi quy cho bản sửa flaky ngày 31/07/2026.
///
/// <para><b>Chuyện đã xảy ra:</b> một lần <c>make ci-full</c> đỏ 6 test consumer, tất cả cùng một
/// thông điệp <c>Expected (harness.Consumed.Any&lt;T&gt;()) to be true, but found False</c>. Chạy
/// riêng assembly thì 107 test xong trong ~370ms và pass 5/5 lần; chạy cả solution song song thì
/// thỉnh thoảng đỏ.</para>
///
/// <para><b>Vì sao:</b> <c>Consumed.Any&lt;T&gt;()</c> ngừng chờ khi bus "im" quá
/// <c>TestInactivityTimeout</c> rồi trả <c>false</c>. Nghĩa là <i>hết giờ</i> và <i>thật sự hỏng</i>
/// cho ra cùng một kết quả, không phân biệt được. Máy nghẽn một nhịp là test đỏ dù code đúng.</para>
///
/// <para><b>Đo thật (MassTransit 8.5.9, 31/07/2026)</b> — dựng harness rỗng rồi chờ một message
/// không bao giờ tới:</para>
/// <code>
/// harness mặc định            → Consumed.Any&lt;T&gt;() bỏ cuộc sau 1,20s
/// SetTestTimeouts(…, 4s)      → bỏ cuộc sau 4,00s
/// </code>
/// <para>1,20s khớp đúng thời lượng 3 trong 6 test đỏ hôm đó (1,25s · 1,55s · 1,60s) — và phép đo
/// thứ hai chứng minh <c>SetTestTimeouts</c> thật sự có hiệu lực, không phải cấu hình trang trí.</para>
///
/// <para><b>Vì sao cần test này:</b> bản sửa chỉ là vài dòng cấu hình, rất dễ bị xoá khi ai đó dọn
/// dẹp hoặc copy khuôn cũ sang consumer mới. Không có gì canh thì flake quay lại, và lần sau lại
/// mất một buổi để truy — vì lỗi không tái hiện được theo yêu cầu.</para>
/// </summary>
public class ConsumerTestHarnessTimeoutTests
{
    [Fact]
    public async Task Harness_ApDungTimeoutTuongMinh_KhongDungMacDinh1Giay()
    {
        var (harness, _, _) = await ConsumerTestHarness.StartAsync<SmsFailedConsumer>();

        try
        {
            harness.Should().BeAssignableTo<ITestHarness>();

            // Đây là con số thật sự quyết định test đỏ hay xanh dưới tải.
            harness.InactivityToken.Should().NotBe(default);

            // Ngưỡng 5s chứ không phải "hơn 1,2s một chút": đo được mặc định là 1,20s, nên một giá
            // trị sát mép vẫn đỏ khi máy nghẽn. Phải cách xa mặc định thì mới thật sự hết flaky.
            ConsumerTestHarness.InactivityTimeout.Should().BeGreaterThanOrEqualTo(
                TimeSpan.FromSeconds(5),
                "mặc định đo được là 1,20s — chính là nguyên nhân 6 test đỏ ngày 31/07/2026");

            ConsumerTestHarness.TestTimeout.Should().BeGreaterThanOrEqualTo(
                ConsumerTestHarness.InactivityTimeout,
                "trần chờ tổng không được nhỏ hơn ngưỡng im lặng, nếu không nó mới là thứ chặn trước");
        }
        finally
        {
            await harness.Stop();
        }
    }
}
