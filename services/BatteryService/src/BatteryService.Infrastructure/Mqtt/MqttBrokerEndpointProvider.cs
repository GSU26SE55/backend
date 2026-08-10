using BatteryService.Application.Interfaces;
using Microsoft.Extensions.Options;

namespace BatteryService.Infrastructure.Mqtt;

/// <summary>
/// GH-784 — dựng điểm kết nối broker từ <see cref="MqttOptions"/>.
/// </summary>
public class MqttBrokerEndpointProvider : IMqttBrokerEndpointProvider
{
    private readonly MqttOptions _options;

    public MqttBrokerEndpointProvider(IOptions<MqttOptions> options) => _options = options.Value;

    public MqttBrokerEndpoint Resolve(string deviceCode)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.Host))
            return MqttBrokerEndpoint.Disabled;

        var publicHost = string.IsNullOrWhiteSpace(_options.PublicHost)
            ? _options.Host
            : _options.PublicHost.Trim();
        var publicPort = _options.PublicPort is > 0 and <= 65535
            ? _options.PublicPort.Value
            : _options.Port;

        return new MqttBrokerEndpoint(
            publicHost,
            publicPort,
            _options.UseTls,
            TopicPrefixFor(deviceCode));
    }

    /// <summary>
    /// GH-784 — tiền tố topic CHUẨN HOÁ CHỮ THƯỜNG.
    /// </summary>
    /// <remarks>
    /// ACL Mosquitto dùng <c>pattern write solar/%u/+/telemetry</c>, trong đó <c>%u</c> là username
    /// — mà username được sinh bằng <c>deviceCode.ToLowerInvariant()</c>. Nếu thiết bị publish lên
    /// <c>solar/E2E-IOT-230605/...</c> (deviceCode nguyên bản, chữ hoa) thì topic KHÔNG khớp
    /// <c>solar/e2e-iot-230605/...</c> và broker từ chối — dù credential hoàn toàn đúng.
    /// <para>
    /// So khớp topic của MQTT phân biệt chữ hoa/thường và không có cách nào tắt, nên phía sinh
    /// credential phải nói thẳng cho thiết bị biết tiền tố nào dùng được, thay vì để mỗi bên tự suy
    /// từ deviceCode rồi lệch nhau.
    /// </para>
    /// </remarks>
    public static string TopicPrefixFor(string deviceCode)
        => $"solar/{(deviceCode ?? string.Empty).Trim().ToLowerInvariant()}";
}
