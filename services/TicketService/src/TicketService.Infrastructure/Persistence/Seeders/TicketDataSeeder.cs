using Microsoft.EntityFrameworkCore;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.Persistence;

namespace TicketService.Infrastructure.Persistence.Seeders;

public class TicketDataSeeder
{
    private readonly TicketDbContext _context;

    public TicketDataSeeder(TicketDbContext context)
    {
        _context = context;
    }

    public async Task SeedAsync()
    {
        await SeedTicketsAsync();
        await SeedKnowledgeBaseArticlesAsync();
    }

    private async Task SeedTicketsAsync()
    {
        if (await _context.Tickets.AnyAsync())
        {
            Console.WriteLine("[TicketService] Ticket data already exists. Seeding skipped.");
            return;
        }

        Console.WriteLine("[TicketService] Seeding ticket data...");

        var customerId1 = Guid.NewGuid();
        var customerId2 = Guid.NewGuid();
        var batteryAssetId1 = Guid.NewGuid();
        var batteryAssetId2 = Guid.NewGuid();
        var staffId1 = Guid.NewGuid();

        var tickets = new List<Ticket>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Code = "TKT-2602-0001",
                BatteryAssetId = batteryAssetId1,
                CustomerId = customerId1,
                AssignedStaffId = staffId1,
                Title = "Battery not charging",
                Description = "The battery unit is plugged in but not accumulating any charge. The indicator light is off.",
                Category = TicketCategoryEnum.Charging,
                Priority = TicketPriorityEnum.P1Critical,
                Status = TicketStatusEnum.Open,
                Origin = TicketOriginEnum.ManualByCustomer,
                IsIncident = true,
                CreatedAt = DateTime.UtcNow.AddDays(-5),
            },
            new()
            {
                Id = Guid.NewGuid(),
                Code = "TKT-2602-0002",
                BatteryAssetId = batteryAssetId2,
                CustomerId = customerId2,
                Title = "Unit overheating during use",
                Description = "The battery becomes unusually hot to the touch after about 30 minutes of operation.",
                Category = TicketCategoryEnum.Overheat,
                Priority = TicketPriorityEnum.P2High,
                Status = TicketStatusEnum.InProgress,
                Origin = TicketOriginEnum.ManualByCustomer,
                AssignedStaffId = staffId1,
                IsIncident = false,
                CreatedAt = DateTime.UtcNow.AddDays(-3),
            },
            new()
            {
                Id = Guid.NewGuid(),
                Code = "TKT-2602-0003",
                BatteryAssetId = batteryAssetId1,
                CustomerId = customerId1,
                Title = "Performance degradation",
                Description = "Battery life has significantly decreased. It only holds a charge for about half the advertised time.",
                Category = TicketCategoryEnum.Performance,
                Priority = TicketPriorityEnum.P3Normal,
                Status = TicketStatusEnum.WaitingCustomer,
                Origin = TicketOriginEnum.CreatedByStaff,
                IsIncident = false,
                CreatedAt = DateTime.UtcNow.AddDays(-10),
            },
            new()
            {
                Id = Guid.NewGuid(),
                Code = "TKT-2602-0004",
                BatteryAssetId = batteryAssetId2,
                CustomerId = customerId2,
                Title = "Automatic shutdown alert",
                Description = "System automatically generated an alert for an unexpected shutdown event.",
                Category = TicketCategoryEnum.NoPower,
                Priority = TicketPriorityEnum.P1Critical,
                Status = TicketStatusEnum.Resolved,
                Origin = TicketOriginEnum.AutoFromAlert,
                OriginAlertId = Guid.NewGuid(),
                IsIncident = true,
                CreatedAt = DateTime.UtcNow.AddDays(-1),
                ResolvedAt = DateTime.UtcNow,
                ResolutionSummary = "Firmware updated to version 1.2.3 which addresses the shutdown bug."
            },
            new()
            {
                Id = Guid.NewGuid(),
                Code = "TKT-2602-0005",
                BatteryAssetId = batteryAssetId1,
                CustomerId = customerId1,
                Title = "Request for on-site repair",
                Description = "Customer has requested an on-site technician for a repair.",
                Category = TicketCategoryEnum.Repair,
                Priority = TicketPriorityEnum.P2High,
                Status = TicketStatusEnum.Closed,
                Origin = TicketOriginEnum.ManualByCustomer,
                IsIncident = false,
                CreatedAt = DateTime.UtcNow.AddDays(-20),
                ResolvedAt = DateTime.UtcNow.AddDays(-18),
                ClosedAt = DateTime.UtcNow.AddDays(-17),
                ResolutionSummary = "Technician replaced the main board."
            }
        };

        await _context.Tickets.AddRangeAsync(tickets);
        await _context.SaveChangesAsync();

        Console.WriteLine($"[TicketService] Successfully seeded {tickets.Count} tickets.");
    }

    private async Task SeedKnowledgeBaseArticlesAsync()
    {
        if (await _context.KnowledgeBaseArticles.AnyAsync())
        {
            Console.WriteLine("[TicketService] KB articles already exist. Seeding skipped.");
            return;
        }

        Console.WriteLine("[TicketService] Seeding KB articles...");

        var managerUserId = Guid.NewGuid(); // Placeholder for manager

        var articles = new List<KnowledgeBaseArticle>
        {
            new KnowledgeBaseArticle
            {
                Id = Guid.NewGuid(),
                Code = "KB-2026-0001",
                Category = TicketCategoryEnum.Charging,
                Title = "Pin sạc chậm - Kiểm tra cáp và cổng",
                Symptoms = @"
## Triệu chứng quan sát:
- Đèn báo sạc nhấp nháy hoặc không sáng
- Thời gian sạc > 8 giờ (bình thường 4-6 giờ)
- Nhiệt độ pin tăng cao khi sạc (>45°C)
",
                DiagnosisSteps = @"
## Bước 1: Kiểm tra nguồn điện
- [ ] Đo điện áp đầu vào: 220V ±10%
- [ ] Kiểm tra ổn định nguồn: không dao động

## Bước 2: Kiểm tra cáp kết nối
- [ ] Cáp nguyên vẹn, không gãy, không cháy
- [ ] Đầu cắm chặt chẽ, không lỏng

## Bước 3: Kiểm tra cổng sạc
- [ ] Không có bụi, gỉ, oxy hóa
- [ ] Pin contact không bị cong hoặc hỏng
",
                SolutionSteps = @"
## Giải pháp:
1. Thay cáp sạc nếu hư hỏng (Mã linh kiện: CAB-USB-C-2M)
2. Làm sạch cổng sạc bằng khí nén (AIR-CLEAN-500ML)
3. Cập nhật firmware nếu phiên bản < 1.2.3

## Kiểm tra sau xử lý:
- Đèn báo sạc sáng liên tục màu xanh
- Thời gian sạc quay về 4-6 giờ
- Nhiệt độ khi sạc < 40°C

## Follow-up:
- Kiểm tra lại sau 24 giờ
",
                RecommendedParts = "{\"cable\":\"CAB-USB-C-2M\",\"cleaner\":\"AIR-CLEAN-500ML\"}",
                Tags = new List<string> { "charging", "cable", "port", "example", "template" },
                Status = KbArticleStatusEnum.Published,
                Version = 1,
                ViewCount = 0,
                HelpfulCount = 0,
                CreatedByUserId = managerUserId,
                CreatedAt = DateTime.UtcNow
            },
            new KnowledgeBaseArticle
            {
                Id = Guid.NewGuid(),
                Code = "KB-2026-0002",
                Category = TicketCategoryEnum.Overheat,
                Title = "Quá nhiệt khi vận hành - Quản lý nhiệt độ",
                Symptoms = "Pin nóng >45°C, quạt tản nhiệt không hoạt động, cảnh báo nhiệt độ trên màn hình",
                DiagnosisSteps = "1. Kiểm tra nhiệt độ môi trường\n2. Kiểm tra khe tản nhiệt\n3. Kiểm tra quạt làm mát",
                SolutionSteps = "1. Đảm bảo khoảng trống 10cm xung quanh\n2. Vệ sinh bụi trên khe tản nhiệt\n3. Thay quạt nếu hỏng",
                RecommendedParts = "{\"fan\":\"FAN-COOL-120MM\"}",
                Tags = new List<string> { "overheat", "thermal", "cooling", "fan", "example", "template" },
                Status = KbArticleStatusEnum.Published,
                Version = 1,
                ViewCount = 0,
                HelpfulCount = 0,
                CreatedByUserId = managerUserId,
                CreatedAt = DateTime.UtcNow
            },
            new KnowledgeBaseArticle
            {
                Id = Guid.NewGuid(),
                Code = "KB-2026-0003",
                Category = TicketCategoryEnum.NoPower,
                Title = "Mất nguồn đột ngột - Kiểm tra firmware và kết nối",
                Symptoms = "Pin tắt đột ngột, không khởi động lại, không phản hồi",
                DiagnosisSteps = "1. Kiểm tra nguồn điện đầu vào\n2. Kiểm tra cầu chì bảo vệ\n3. Kiểm tra log firmware",
                SolutionSteps = "1. Reset bằng nút reset phần cứng\n2. Cập nhật firmware\n3. Thay cầu chì nếu cần",
                RecommendedParts = "{\"fuse\":\"FUSE-10A\"}",
                Tags = new List<string> { "power", "firmware", "connection", "example", "template" },
                Status = KbArticleStatusEnum.Published,
                Version = 1,
                ViewCount = 0,
                HelpfulCount = 0,
                CreatedByUserId = managerUserId,
                CreatedAt = DateTime.UtcNow
            },
            new KnowledgeBaseArticle
            {
                Id = Guid.NewGuid(),
                Code = "KB-2026-0004",
                Category = TicketCategoryEnum.Performance,
                Title = "Suy giảm hiệu năng - Phân tích chu kỳ sạc",
                Symptoms = "Dung lượng pin giảm >20%, thời gian sử dụng ngắn hơn bình thường",
                DiagnosisSteps = "1. Kiểm tra số chu kỳ sạc\n2. Đo điện áp cell\n3. Kiểm tra SOH (State of Health)",
                SolutionSteps = "1. Cân bằng cell nếu chênh lệch >100mV\n2. Thay cell hỏng\n3. Khuyến nghị thay pin nếu SOH <70%",
                RecommendedParts = "{\"cell\":\"CELL-LI-ION-3.7V\"}",
                Tags = new List<string> { "performance", "capacity", "soh", "cycle", "example", "template" },
                Status = KbArticleStatusEnum.Published,
                Version = 1,
                ViewCount = 0,
                HelpfulCount = 0,
                CreatedByUserId = managerUserId,
                CreatedAt = DateTime.UtcNow
            },
            new KnowledgeBaseArticle
            {
                Id = Guid.NewGuid(),
                Code = "KB-2026-0005",
                Category = TicketCategoryEnum.Repair,
                Title = "Hư hỏng vật lý - Hướng dẫn thay thế linh kiện",
                Symptoms = "Vỏ pin bị biến dạng, rò rỉ chất lỏng, tiếng kêu bất thường",
                DiagnosisSteps = "1. Đánh giá mức độ hư hỏng\n2. Xác định linh kiện bị ảnh hưởng\n3. Kiểm tra BMS còn hoạt động không",
                SolutionSteps = "1. Cách ly pin khỏi hệ thống\n2. Thay thế linh kiện hư hỏng\n3. Kiểm tra toàn bộ trước khi đưa vào vận hành",
                RecommendedParts = "{\"case\":\"CASE-REPLACEMENT-MODEL-A\"}",
                Tags = new List<string> { "repair", "physical-damage", "replacement", "example", "template" },
                Status = KbArticleStatusEnum.Published,
                Version = 1,
                ViewCount = 0,
                HelpfulCount = 0,
                CreatedByUserId = managerUserId,
                CreatedAt = DateTime.UtcNow
            }
        };

        await _context.KnowledgeBaseArticles.AddRangeAsync(articles);
        await _context.SaveChangesAsync();

        Console.WriteLine($"[TicketService] Successfully seeded {articles.Count} KB articles.");
    }
}
