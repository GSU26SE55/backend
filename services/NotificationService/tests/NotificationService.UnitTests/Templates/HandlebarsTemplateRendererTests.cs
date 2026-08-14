using NotificationService.Application.Templates;

namespace NotificationService.UnitTests.Templates;

public class HandlebarsTemplateRendererTests
{
    private readonly ITemplateRenderer _renderer = new HandlebarsTemplateRenderer();

    [Fact]
    public void Render_EnvironmentalIncidentDetected_ContainsExpectedVariables()
    {
        var html = _renderer.Render("environmental-incident-detected", new
        {
            SiteName = "Site Hà Nội",
            IncidentType = "SmokeDetected",
            Severity = "Critical",
            Description = "Abnormal smoke detected",
            DetectedAt = "21/06/2026 14:30:00",
            IncidentId = "abc-123"
        });

        html.Should().Contain("<html");
        html.Should().Contain("Site Hà Nội");
        html.Should().Contain("SmokeDetected");
        html.Should().Contain("Critical");
        html.Should().Contain("Abnormal smoke detected");
    }

    [Fact]
    public void Render_SlaBreachTemplate_ContainsExpectedVariables()
    {
        var html = _renderer.Render("sla-breach", new
        {
            Code = "2026-001",
            Title = "Battery B0005 is Overheating",
            Priority = "P1 Critical",
            BreachedAt = "21/06/2026 10:00:00"
        });

        html.Should().Contain("<html");
        html.Should().Contain("2026-001");
        html.Should().Contain("21/06/2026 10:00:00");
        html.Should().Contain("SLA BREACH");
    }

    [Fact]
    public void Render_AlertTicketSagaFailed_ContainsExpectedVariables()
    {
        var html = _renderer.Render("alert-ticket-saga-failed", new
        {
            CorrelationId = "corr-999",
            AlertId = Guid.NewGuid(),
            TicketId = (string?)null,
            AssetSerialNumber = "SN-001",
            FailedAtStage = "TicketProvisioned",
            Reason = "Timeout",
            ErrorCode = (string?)null,
            FailedAt = "21/06/2026 09:00:00"
        });

        html.Should().Contain("<html");
        html.Should().Contain("SN-001");
        html.Should().Contain("TicketProvisioned");
        html.Should().Contain("Saga Failed");
    }

    [Fact]
    public void Render_PasswordReset_ContainsOtpCode()
    {
        var html = _renderer.Render("password-reset", new
        {
            FullName = "Nguyễn Văn A",
            OtpCode = "123456",
            ExpiredAt = "21/06/2026 15:00:00"
        });

        html.Should().Contain("<html");
        html.Should().Contain("123456");
        html.Should().Contain("Nguyễn Văn A");
        html.Should().Contain("Reset your password");
    }

    [Fact]
    public void Render_WelcomeCustomer_ContainsFullNameAndEmail()
    {
        var html = _renderer.Render("welcome-customer", new
        {
            FullName = "Trần Thị B",
            Email = "b@example.com"
        });

        html.Should().Contain("<html");
        html.Should().Contain("Trần Thị B");
        html.Should().Contain("b@example.com");
    }

    [Fact]
    public void Render_UnknownTemplate_ThrowsInvalidOperationException()
    {
        var act = () => _renderer.Render("non-existent-template", new { });

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*non-existent-template.html*");
    }

    [Fact]
    public void Render_CalledTwiceWithSameTemplate_UsesCachedCompilation()
    {
        // Second call should use cached compile — both should produce same output
        var data = new { Code = "CODE-01", Title = "Test Ticket", Priority = "P3", BreachedAt = "21/06/2026" };
        var first = _renderer.Render("sla-breach", data);
        var second = _renderer.Render("sla-breach", data);

        first.Should().Be(second);
    }
}
