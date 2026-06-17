using MockQueryable.Moq;
using SharedKernels.Interfaces;
using SmsService.Application.CQRS.Handler.Sms;
using SmsService.Application.CQRS.Query.Sms;
using SmsService.Application.Interfaces.Repositories;
using SmsService.Domain.Entities;
using SmsService.Domain.Enums;

namespace SmsService.UnitTests.Handlers.Sms;

public class GetSmsByIdQueryHandlerTests
{
    private readonly Mock<ISmsUnitOfWork> _uow = new();
    private readonly Mock<IGenericRepository<SmsMessage>> _msgs = new();
    private readonly GetSmsByIdQueryHandler _sut;

    public GetSmsByIdQueryHandlerTests()
    {
        _uow.Setup(u => u.SmsMessages).Returns(_msgs.Object);
        _sut = new GetSmsByIdQueryHandler(_uow.Object);
    }

    [Fact]
    public async Task Handle_NotFound_Returns404()
    {
        _msgs.Setup(m => m.GetAllAsync()).Returns(new List<SmsMessage>().BuildMock());

        var resp = await _sut.Handle(new GetSmsByIdQuery { SmsId = Guid.NewGuid() }, CancellationToken.None);

        resp.StatusCode.Should().Be(404);
        resp.Data.Should().BeNull();
    }

    [Fact]
    public async Task Handle_Existing_ReturnsSms()
    {
        var sms = new SmsMessage
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "+84901234567",
            Message = "hello",
            Status = SmsStatus.Sent,
            SourceService = "auth",
            CorrelationId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        };
        _msgs.Setup(m => m.GetAllAsync()).Returns(new[] { sms }.BuildMock());

        var resp = await _sut.Handle(new GetSmsByIdQuery { SmsId = sms.Id }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.Data!.Id.Should().Be(sms.Id);
    }

    [Fact]
    public async Task Handle_SoftDeleted_Returns404()
    {
        var sms = new SmsMessage
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "+84901234567",
            Message = "hello",
            Status = SmsStatus.Sent,
            SourceService = "auth",
            CorrelationId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            IsDeleted = true
        };
        _msgs.Setup(m => m.GetAllAsync()).Returns(new[] { sms }.BuildMock());

        var resp = await _sut.Handle(new GetSmsByIdQuery { SmsId = sms.Id }, CancellationToken.None);

        resp.StatusCode.Should().Be(404);
    }
}
