using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.Implements.Services;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Services;

/// <summary>
/// Bộ test cho luật "ai được báo khi có chat mới".
///
/// <para>Trọng tâm là nhánh <c>isInternal</c>: danh sách người nhận phải TRÙNG KHÍT với danh sách
/// người đọc được ghi chú nội bộ theo <c>TicketQueryHelper.CanViewInternalChats</c>. Lệch về một
/// phía là bỏ sót người có quyền; lệch về phía kia là hé nội dung nội bộ ra ngoài — mà nội dung đó
/// đi thẳng vào tiêu đề/nội dung thông báo đẩy nên hiện nguyên văn trên màn hình khoá.</para>
/// </summary>
public class ChatRecipientResolverTests
{
    private static Ticket MakeTicket(Guid customerId) => new()
    {
        Id = Guid.NewGuid(),
        Code = "T-001",
        CustomerId = customerId,
        Title = "Test",
        Description = "desc",
        Category = TicketCategoryEnum.Other,
        Status = TicketStatusEnum.InProgress,
        Origin = TicketOriginEnum.ManualByCustomer,
        CreatedAt = DateTime.UtcNow,
    };

    private static TicketParticipant MakeParticipant(
        Ticket ticket, Guid userId, ActorRoleEnum role, bool canViewInternal, DateTime? removedAt = null) => new()
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            Ticket = ticket,
            UserId = userId,
            UserRole = role,
            ParticipantType = ParticipantTypeEnum.Owner,
            CanPost = true,
            CanViewInternal = canViewInternal,
            AddedByUserId = Guid.NewGuid(),
            AddedAt = DateTime.UtcNow,
            RemovedAt = removedAt,
        };

    private static TicketAssignment MakeAssignment(Ticket ticket, Guid staffId, AssignmentRoleEnum role) => new()
    {
        Id = Guid.NewGuid(),
        TicketId = ticket.Id,
        StaffId = staffId,
        Role = role,
    };

    private static TicketChat MakeChat(Ticket ticket, Guid authorId, ActorRoleEnum authorRole) => new()
    {
        Id = Guid.NewGuid(),
        TicketId = ticket.Id,
        Ticket = ticket,
        AuthorUserId = authorId,
        AuthorRole = authorRole,
        AuthorDisplayName = "Sender",
        Body = "content",
        CreatedAt = DateTime.UtcNow,
    };

    private static ChatRecipientResolver CreateSut(
        IEnumerable<TicketParticipant>? participants = null,
        IEnumerable<TicketAssignment>? assignments = null,
        IEnumerable<TicketChat>? chats = null)
    {
        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            chatSeed: chats,
            participantSeed: participants,
            assignmentSeed: assignments);

        return new ChatRecipientResolver(uow.Object);
    }

    // ════════════════════════ Chat công khai ════════════════════════

    [Fact]
    public async Task Public_GomChuTicketVaNguoiDuocPhanCong()
    {
        var customerId = Guid.NewGuid();
        var ticket = MakeTicket(customerId);
        var primaryId = Guid.NewGuid();
        var supporterId = Guid.NewGuid();

        var sut = CreateSut(assignments:
        [
            MakeAssignment(ticket, primaryId, AssignmentRoleEnum.PrimaryHandler),
            MakeAssignment(ticket, supporterId, AssignmentRoleEnum.Supporter),
        ]);

        var result = await sut.ResolveAsync(ticket.Id, customerId, Guid.NewGuid(), isInternal: false);

        result.Should().BeEquivalentTo(new[] { customerId, primaryId, supporterId });
    }

    [Fact]
    public async Task Public_BoQuaPreviousPrimaryHandler_DaBanGiaoThiThoi()
    {
        var customerId = Guid.NewGuid();
        var ticket = MakeTicket(customerId);
        var previousId = Guid.NewGuid();

        var sut = CreateSut(assignments:
        [
            MakeAssignment(ticket, previousId, AssignmentRoleEnum.PreviousPrimaryHandler),
        ]);

        var result = await sut.ResolveAsync(ticket.Id, customerId, Guid.NewGuid(), isInternal: false);

        result.Should().NotContain(previousId);
    }

    [Fact]
    public async Task Public_GomCaNguoiTungNhanTrenTicket()
    {
        // Manager nhảy vào trả lời một lần rồi thôi: không có dòng assignment, không có dòng
        // participant. Chỉ dựa vào hai nguồn đó thì họ không bao giờ biết có người trả lời lại.
        var customerId = Guid.NewGuid();
        var ticket = MakeTicket(customerId);
        var managerId = Guid.NewGuid();

        var sut = CreateSut(chats: [MakeChat(ticket, managerId, ActorRoleEnum.Manager)]);

        var result = await sut.ResolveAsync(ticket.Id, customerId, Guid.NewGuid(), isInternal: false);

        result.Should().Contain(managerId);
    }

    [Fact]
    public async Task Public_BoQuaParticipantDaBiGoKhoiTicket()
    {
        var customerId = Guid.NewGuid();
        var ticket = MakeTicket(customerId);
        var removedId = Guid.NewGuid();

        var sut = CreateSut(participants:
        [
            MakeParticipant(ticket, removedId, ActorRoleEnum.Staff, canViewInternal: true, removedAt: DateTime.UtcNow),
        ]);

        var result = await sut.ResolveAsync(ticket.Id, customerId, Guid.NewGuid(), isInternal: false);

        result.Should().NotContain(removedId);
    }

    // ════════════════════════ Ghi chú nội bộ ════════════════════════

    [Fact]
    public async Task Internal_KhongBaoChoChuTicketThongThuong()
    {
        // Customer mặc định có CanViewInternal=false (TicketCreateCommandHandler đặt vậy) nên
        // không đọc được ghi chú nội bộ, và vì thế không được nhận thông báo về nó.
        var customerId = Guid.NewGuid();
        var ticket = MakeTicket(customerId);
        var staffId = Guid.NewGuid();

        var sut = CreateSut(assignments: [MakeAssignment(ticket, staffId, AssignmentRoleEnum.PrimaryHandler)]);

        var result = await sut.ResolveAsync(ticket.Id, customerId, Guid.NewGuid(), isInternal: true);

        result.Should().NotContain(customerId);
        result.Should().Contain(staffId);
    }

    [Fact]
    public async Task Internal_KhongBaoChoCustomerDuTungNhanCongKhai()
    {
        // Customer đã nhắn công khai trên ticket vẫn không được thấy ghi chú nội bộ.
        var customerId = Guid.NewGuid();
        var ticket = MakeTicket(customerId);

        var sut = CreateSut(chats: [MakeChat(ticket, customerId, ActorRoleEnum.Customer)]);

        var result = await sut.ResolveAsync(ticket.Id, customerId, Guid.NewGuid(), isInternal: true);

        result.Should().NotContain(customerId);
    }

    [Fact]
    public async Task Internal_BaoChoStaffParticipantDuCanViewInternalLaFalse()
    {
        // Đây là ca mà luật cũ làm SAI: nó đòi cả "không phải Customer" LẪN CanViewInternal=true.
        // Luật thật (TicketQueryHelper) cho Staff/Manager/Admin xem nội bộ nhờ VAI TRÒ, không phụ
        // thuộc cờ trên dòng participant — nên Staff này đọc được mà lại không được báo.
        var customerId = Guid.NewGuid();
        var ticket = MakeTicket(customerId);
        var staffId = Guid.NewGuid();

        var sut = CreateSut(participants:
        [
            MakeParticipant(ticket, staffId, ActorRoleEnum.Staff, canViewInternal: false),
        ]);

        var result = await sut.ResolveAsync(ticket.Id, customerId, Guid.NewGuid(), isInternal: true);

        result.Should().Contain(staffId);
    }

    [Fact]
    public async Task Internal_BaoChoCustomerDuocCapQuyenXemNoiBo()
    {
        // #522 — participant bất kỳ được cấp CanViewInternal thì ĐỌC ĐƯỢC ghi chú nội bộ
        // (TicketChatsQueryHandler / ChatAuthorizationService đều theo luật này). Đã đọc được thì
        // phải được báo, nếu không họ chỉ thấy tin khi tự mở ticket ra xem.
        var customerId = Guid.NewGuid();
        var ticket = MakeTicket(customerId);

        var sut = CreateSut(participants:
        [
            MakeParticipant(ticket, customerId, ActorRoleEnum.Customer, canViewInternal: true),
        ]);

        var result = await sut.ResolveAsync(ticket.Id, customerId, Guid.NewGuid(), isInternal: true);

        result.Should().Contain(customerId);
    }

    [Fact]
    public async Task Internal_KhongBaoChoActorHeThong()
    {
        // ActorRoleEnum.System không nằm trong Admin/Manager/Staff nên không đọc được nội bộ.
        var customerId = Guid.NewGuid();
        var ticket = MakeTicket(customerId);
        var systemActorId = Guid.NewGuid();

        var sut = CreateSut(chats: [MakeChat(ticket, systemActorId, ActorRoleEnum.System)]);

        var result = await sut.ResolveAsync(ticket.Id, customerId, Guid.NewGuid(), isInternal: true);

        result.Should().NotContain(systemActorId);
    }

    [Fact]
    public async Task Internal_VaiTroDuocGopTuNhieuNguon()
    {
        // Cùng một người vừa có dòng participant ghi Customer, vừa từng nhắn với vai trò Staff.
        // Gộp vai trò lại thì họ đọc được nội bộ nhờ vai trò Staff — ghi đè thay vì gộp sẽ làm
        // kết quả phụ thuộc vào thứ tự duyệt nguồn.
        var customerId = Guid.NewGuid();
        var ticket = MakeTicket(customerId);
        var userId = Guid.NewGuid();

        var sut = CreateSut(
            participants: [MakeParticipant(ticket, userId, ActorRoleEnum.Customer, canViewInternal: false)],
            chats: [MakeChat(ticket, userId, ActorRoleEnum.Staff)]);

        var result = await sut.ResolveAsync(ticket.Id, customerId, Guid.NewGuid(), isInternal: true);

        result.Should().Contain(userId);
    }

    // ════════════════════════ Quy tắc chung ════════════════════════

    [Fact]
    public async Task LoaiTacGiaRaKhoiDanhSach()
    {
        var customerId = Guid.NewGuid();
        var ticket = MakeTicket(customerId);
        var authorId = Guid.NewGuid();

        var sut = CreateSut(assignments: [MakeAssignment(ticket, authorId, AssignmentRoleEnum.PrimaryHandler)]);

        var result = await sut.ResolveAsync(ticket.Id, customerId, authorId, isInternal: false);

        result.Should().NotContain(authorId);
    }

    [Fact]
    public async Task LoaiGuidEmpty_KhongPhaiMotNguoiNhanDuoc()
    {
        var ticket = MakeTicket(Guid.Empty);

        var sut = CreateSut(chats: [MakeChat(ticket, Guid.Empty, ActorRoleEnum.System)]);

        var result = await sut.ResolveAsync(ticket.Id, Guid.Empty, Guid.NewGuid(), isInternal: false);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task KhongTraVeTrungLap_DuMotNguoiXuatHienONhieuNguon()
    {
        var customerId = Guid.NewGuid();
        var ticket = MakeTicket(customerId);
        var staffId = Guid.NewGuid();

        var sut = CreateSut(
            participants: [MakeParticipant(ticket, staffId, ActorRoleEnum.Staff, canViewInternal: true)],
            assignments: [MakeAssignment(ticket, staffId, AssignmentRoleEnum.PrimaryHandler)],
            chats: [MakeChat(ticket, staffId, ActorRoleEnum.Staff)]);

        var result = await sut.ResolveAsync(ticket.Id, customerId, Guid.NewGuid(), isInternal: false);

        result.Should().OnlyHaveUniqueItems();
        result.Count(x => x == staffId).Should().Be(1);
    }

    [Fact]
    public async Task TicketChuaCoAiThamGia_ChiConChuTicket()
    {
        var customerId = Guid.NewGuid();
        var ticket = MakeTicket(customerId);

        var sut = CreateSut();

        var result = await sut.ResolveAsync(ticket.Id, customerId, Guid.NewGuid(), isInternal: false);

        result.Should().BeEquivalentTo(new[] { customerId });
    }
}
