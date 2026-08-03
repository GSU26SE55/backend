using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedContracts.Events;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Infrastructure.Consumers;

/// <summary>
/// Verify async khi TicketCreatedEvent: đợi ticket commit (race ~16ms) rồi gọi TicketVerifyRunner
/// (logic verify dùng chung với re-verify thủ công). Human-in-the-loop: chỉ gắn nhãn, KHÔNG chặn.
/// </summary>
public class TicketVerifyOnCreatedConsumer : IConsumer<TicketCreatedEvent>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly ITicketVerifyRunner _runner;
    private readonly ILogger<TicketVerifyOnCreatedConsumer> _logger;

    public TicketVerifyOnCreatedConsumer(
        ITicketUnitOfWork uow,
        ITicketVerifyRunner runner,
        ILogger<TicketVerifyOnCreatedConsumer> logger)
    {
        _uow = uow;
        _runner = runner;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TicketCreatedEvent> context)
    {
        var ct = context.CancellationToken;
        var ticketId = context.Message.TicketId;

        // Race: event có thể tới TRƯỚC khi transaction tạo ticket commit (đo được ~16ms).
        // Đợi ticket xuất hiện với backoff nhỏ trước khi verify — tránh skip vĩnh viễn.
        var exists = await _uow.Tickets.GetAllAsync()
            .AnyAsync(t => t.Id == ticketId && !t.IsDeleted, ct);

        for (var attempt = 1; !exists && attempt <= 5; attempt++)
        {
            await Task.Delay(300 * attempt, ct);
            exists = await _uow.Tickets.GetAllAsync()
                .AnyAsync(t => t.Id == ticketId && !t.IsDeleted, ct);
        }

        if (!exists)
        {
            _logger.LogWarning(
                "TicketVerify: ticket {TicketId} không thấy sau 5 lần thử — bỏ qua.", ticketId);
            return;
        }

        await _runner.RunAsync(ticketId, ct);
    }
}
