using SharedContracts.Events.Root;

namespace SharedContracts.Events;

public record SendOtpRegisterEvent(string ToEmail, string Otp) : IntegrationEvent;
