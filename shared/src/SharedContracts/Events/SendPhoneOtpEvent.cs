using SharedContracts.Events.Root;

namespace SharedContracts.Events;

public record SendPhoneOtpEvent(string PhoneNumber, string Otp) : IntegrationEvent;
