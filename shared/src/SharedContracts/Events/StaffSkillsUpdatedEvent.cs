using System.Collections.Generic;
using SharedContracts.Events.Root;

namespace SharedContracts.Events;

public record StaffSkillsUpdatedEvent(
    Guid AccountId,
    List<string> SkillCodes
) : IntegrationEvent;
