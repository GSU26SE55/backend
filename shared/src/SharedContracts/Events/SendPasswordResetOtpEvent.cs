using SharedContracts.Events.Root;

namespace SharedContracts.Events;

public record SendPasswordResetOtpEvent(string ToEmail, string Otp) : IntegrationEvent;
