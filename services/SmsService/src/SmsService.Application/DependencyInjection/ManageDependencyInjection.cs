using System.Reflection;

namespace SmsService.Application.DependencyInjection;

/// <summary>
/// Marker class expose <see cref="ApplicationAssembly"/> để MassTransit dùng khi
/// <c>AddMessageBus(... ManageDependencyInjection.ApplicationAssembly)</c>.
/// <para>
/// <strong>KHÔNG</strong> gọi <c>AddMediatR</c> ở đây — <c>AddSharedInfrastructure(..., "SmsService.Application")</c>
/// đã tự quét assembly theo tên. Đăng ký lại sẽ tạo "Multiple handlers registered" runtime exception
/// khi <c>mediator.Send</c>.
/// </para>
/// </summary>
public static class ManageDependencyInjection
{
    public static Assembly ApplicationAssembly => typeof(ManageDependencyInjection).Assembly;
}
