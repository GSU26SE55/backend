using SharedContracts.Events.Root;

namespace SharedContracts.Events;

public record SendEmailChangeOtpEvent(string ToNewEmail, string Otp) : IntegrationEvent;
