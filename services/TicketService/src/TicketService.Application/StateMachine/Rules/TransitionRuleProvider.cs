using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.StateMachine.Rules;

public class TransitionRuleProvider : ITransitionRuleProvider
{
    public Dictionary<TicketStatusEnum, Dictionary<TicketStatusEnum, Func<Ticket, ActorRoleEnum, Guid, TransitionResult>>> GetRules()
    {
        return new()
        {
            #region New Transitions
            {
                TicketStatusEnum.New,
                new Dictionary<TicketStatusEnum, Func<Ticket, ActorRoleEnum, Guid, TransitionResult>>
                {
                    {
                        TicketStatusEnum.Escalated,
                        (ticket, role, userId) => new TransitionResult
                        {
                            IsAllowed = role is ActorRoleEnum.Staff or ActorRoleEnum.Manager or ActorRoleEnum.Admin,
                            Reason = role is ActorRoleEnum.Staff or ActorRoleEnum.Manager or ActorRoleEnum.Admin
                                ? null
                                : "Only Staff, Managers or Admin can escalate."
                        }
                    },
                    {
                        TicketStatusEnum.Open,
                        (ticket, role, userId) => new TransitionResult
                        {
                            IsAllowed = role is ActorRoleEnum.Manager or ActorRoleEnum.Admin or ActorRoleEnum.System,
                            Reason = role is ActorRoleEnum.Manager or ActorRoleEnum.Admin or ActorRoleEnum.System
                                ? null
                                : "Only Managers, Admin or System can open a new ticket."
                        }
                    },
                    {
                        TicketStatusEnum.Approved,
                        (ticket, role, userId) => new TransitionResult
                        {
                            IsAllowed = role is ActorRoleEnum.Manager or ActorRoleEnum.Admin,
                            Reason = role is ActorRoleEnum.Manager or ActorRoleEnum.Admin
                                ? null
                                : "Only Managers can approve from New."
                        }
                    }
                }
            },
            #endregion

            //đã có test cho nó
            #region Open Transitions
            {
                TicketStatusEnum.Open,
                new Dictionary<TicketStatusEnum, Func<Ticket, ActorRoleEnum, Guid, TransitionResult>>
                {
                    {
                        TicketStatusEnum.Approved,
                        (ticket, role, userId) => new TransitionResult
                        {
                            IsAllowed = role is ActorRoleEnum.Manager or ActorRoleEnum.Admin,
                            Reason = role is ActorRoleEnum.Manager or ActorRoleEnum.Admin
                                ? null
                                : "Only Managers can approve."
                        }
                    },
                    {
                        TicketStatusEnum.Escalated,
                        (ticket, role, userId) => new TransitionResult
                        {
                            IsAllowed = role is ActorRoleEnum.Manager or ActorRoleEnum.Admin or ActorRoleEnum.System,
                            Reason = role is ActorRoleEnum.Manager or ActorRoleEnum.Admin or ActorRoleEnum.System
                                ? null
                                : "Only Managers, Admin or System can escalate."
                        }
                    },
                    {
                        TicketStatusEnum.ClosedRejected,
                        (ticket, role, userId) => new TransitionResult
                        {
                            IsAllowed = role is ActorRoleEnum.Manager or ActorRoleEnum.Admin,
                            Reason = role is ActorRoleEnum.Manager or ActorRoleEnum.Admin
                                ? null
                                : "Only Managers can reject tickets at triage."
                        }
                    }
                }
            },
            #endregion

            #region Approved Transitions
            {
                TicketStatusEnum.Approved,
                new Dictionary<TicketStatusEnum, Func<Ticket, ActorRoleEnum, Guid, TransitionResult>>
                {
                    {
                        TicketStatusEnum.Assigned,
                        (ticket, role, userId) => new TransitionResult
                        {
                            IsAllowed = role is ActorRoleEnum.Manager or ActorRoleEnum.Admin,
                            Reason = role is ActorRoleEnum.Manager or ActorRoleEnum.Admin
                                ? null
                                : "Only Managers can assign."
                        }
                    },
                    {
                        TicketStatusEnum.Escalated,
                        (ticket, role, userId) => new TransitionResult
                        {
                            IsAllowed = role is ActorRoleEnum.Manager or ActorRoleEnum.Admin,
                            Reason = role is ActorRoleEnum.Manager or ActorRoleEnum.Admin
                                ? null
                                : "Only Managers can escalate approved tickets."
                        }
                    }
                }
            },
            #endregion

            #region Assigned Transitions
            {
                TicketStatusEnum.Assigned,
                new Dictionary<TicketStatusEnum, Func<Ticket, ActorRoleEnum, Guid, TransitionResult>>
                {
                    {
                        TicketStatusEnum.Assigned,
                        (ticket, role, userId) => new TransitionResult
                        {
                            IsAllowed = role is ActorRoleEnum.Manager or ActorRoleEnum.Admin,
                            Reason = role is ActorRoleEnum.Manager or ActorRoleEnum.Admin
                                ? null
                                : "Only Managers can reassign."
                        }
                    },
                    {
                        TicketStatusEnum.InProgress,
                        (ticket, role, userId) => new TransitionResult
                        {
                            IsAllowed = (role is ActorRoleEnum.Staff && ticket.AssignedStaffId == userId) || role is ActorRoleEnum.Admin,
                            Reason = (role is ActorRoleEnum.Staff && ticket.AssignedStaffId == userId) || role is ActorRoleEnum.Admin
                                ? null
                                : "Only the Assigned Staff can start work."
                        }
                    },

                    {
                        TicketStatusEnum.Escalated,
                        (ticket, role, userId) => new TransitionResult
                        {
                            IsAllowed = role is ActorRoleEnum.System or ActorRoleEnum.Admin,
                            Reason = role is ActorRoleEnum.System or ActorRoleEnum.Admin
                                ? null
                                : "Only System can escalate due to SLA breach."
                        }
                    }
                }
            },
            #endregion

            #region InProgress Transitions
            {
                TicketStatusEnum.InProgress,
                new Dictionary<TicketStatusEnum, Func<Ticket, ActorRoleEnum, Guid, TransitionResult>>
                {
                    {
                        TicketStatusEnum.WaitingCustomer,
                        (ticket, role, userId) => new TransitionResult
                        {
                            IsAllowed = (role is ActorRoleEnum.Staff && ticket.AssignedStaffId == userId) || role is ActorRoleEnum.Admin,
                            Reason = (role is ActorRoleEnum.Staff && ticket.AssignedStaffId == userId) || role is ActorRoleEnum.Admin
                                ? null
                                : "Only the Assigned Staff can put on hold."
                        }
                    },

                    {
                        TicketStatusEnum.WaitingParts,
                        (ticket, role, userId) => new TransitionResult
                        {
                            IsAllowed = (role is ActorRoleEnum.Staff && ticket.AssignedStaffId == userId) || role is ActorRoleEnum.Admin,
                            Reason = (role is ActorRoleEnum.Staff && ticket.AssignedStaffId == userId) || role is ActorRoleEnum.Admin
                                ? null
                                : "Only the Assigned Staff can put on hold."
                        }
                    },

                    {
                        TicketStatusEnum.WaitingOnsiteSchedule,
                        (ticket, role, userId) => new TransitionResult
                        {
                            IsAllowed = (role is ActorRoleEnum.Staff && ticket.AssignedStaffId == userId) || role is ActorRoleEnum.Admin,
                            Reason = (role is ActorRoleEnum.Staff && ticket.AssignedStaffId == userId) || role is ActorRoleEnum.Admin
                                ? null
                                : "Only the Assigned Staff can put on hold."
                        }
                    },

                    {
                        TicketStatusEnum.Resolved,
                        (ticket, role, userId) => new TransitionResult
                        {
                            IsAllowed = (role is ActorRoleEnum.Staff && ticket.AssignedStaffId == userId) || role is ActorRoleEnum.Admin,
                            Reason = (role is ActorRoleEnum.Staff && ticket.AssignedStaffId == userId) || role is ActorRoleEnum.Admin
                                ? null
                                : "Only the Assigned Staff can resolve."
                        }
                    },

                    {
                        TicketStatusEnum.Escalated,
                        (ticket, role, userId) => new TransitionResult
                        {
                            IsAllowed = (role is ActorRoleEnum.Staff && ticket.AssignedStaffId == userId) || role is ActorRoleEnum.Admin || role is ActorRoleEnum.Manager,
                            Reason = (role is ActorRoleEnum.Staff && ticket.AssignedStaffId == userId) || role is ActorRoleEnum.Admin || role is ActorRoleEnum.Manager
                                ? null
                                : "Only the Assigned Staff, Manager or Admin can request escalation."
                        }
                    },
                }
            },
            #endregion

            #region WaitingCustomer Transitions
            {
                TicketStatusEnum.WaitingCustomer,
                new Dictionary<TicketStatusEnum, Func<Ticket, ActorRoleEnum, Guid, TransitionResult>>
                {
                    {
                        TicketStatusEnum.InProgress,
                        (ticket, role, userId) => new TransitionResult
                        {
                            IsAllowed = (role is ActorRoleEnum.Staff && ticket.AssignedStaffId == userId)
                                        || (role is ActorRoleEnum.System)
                                        || (role is ActorRoleEnum.Admin),
                            Reason = (role is ActorRoleEnum.Staff && ticket.AssignedStaffId == userId)
                                     || (role is ActorRoleEnum.System)
                                     || (role is ActorRoleEnum.Admin)
                                ? null
                                : "Only Assigned Staff or System can resume."
                        }
                    }
                }
            },
            #endregion

            #region WaitingParts Transitions
            {
                TicketStatusEnum.WaitingParts,
                new Dictionary<TicketStatusEnum, Func<Ticket, ActorRoleEnum, Guid, TransitionResult>>
                {
                    {
                        TicketStatusEnum.InProgress,
                        (ticket, role, userId) => new TransitionResult
                        {
                            IsAllowed = (role is ActorRoleEnum.Staff && ticket.AssignedStaffId == userId)
                                        || (role is ActorRoleEnum.System)
                                        || (role is ActorRoleEnum.Admin),
                            Reason = (role is ActorRoleEnum.Staff && ticket.AssignedStaffId == userId)
                                     || (role is ActorRoleEnum.System)
                                     || (role is ActorRoleEnum.Admin)
                                ? null
                                : "Only Assigned Staff or System can resume."
                        }
                    }
                }
            },
            #endregion

            #region WaitingOnsiteSchedule Transitions
            {
                TicketStatusEnum.WaitingOnsiteSchedule,
                new Dictionary<TicketStatusEnum, Func<Ticket, ActorRoleEnum, Guid, TransitionResult>>
                {
                    {
                        TicketStatusEnum.InProgress,
                        (ticket, role, userId) => new TransitionResult
                        {
                            IsAllowed = (role is ActorRoleEnum.Staff && ticket.AssignedStaffId == userId)
                                        || (role is ActorRoleEnum.System)
                                        || (role is ActorRoleEnum.Admin),
                            Reason = (role is ActorRoleEnum.Staff && ticket.AssignedStaffId == userId)
                                     || (role is ActorRoleEnum.System)
                                     || (role is ActorRoleEnum.Admin)
                                ? null
                                : "Only Assigned Staff or System can resume."
                        }
                    }
                }
            },
            #endregion

            #region Escalated Transitions
            {
                TicketStatusEnum.Escalated,
                new Dictionary<TicketStatusEnum, Func<Ticket, ActorRoleEnum, Guid, TransitionResult>>
                {
                    {
                        TicketStatusEnum.Assigned,
                        (ticket, role, userId) => new TransitionResult
                        {
                            IsAllowed = role is ActorRoleEnum.Manager or ActorRoleEnum.Admin,
                            Reason = role is ActorRoleEnum.Manager or ActorRoleEnum.Admin
                                ? null
                                : "Only Managers can reassign escalated tickets."
                        }
                    },
                    {
                        TicketStatusEnum.Incident,
                        (ticket, role, userId) => new TransitionResult
                        {
                            IsAllowed = role is ActorRoleEnum.Manager or ActorRoleEnum.Admin,
                            Reason = role is ActorRoleEnum.Manager or ActorRoleEnum.Admin
                                ? null
                                : "Only Managers can declare incidents."
                        }
                    },
                    {
                        TicketStatusEnum.ClosedRejected,
                        (ticket, role, userId) => new TransitionResult
                        {
                            IsAllowed = role is ActorRoleEnum.Manager or ActorRoleEnum.Admin,
                            Reason = role is ActorRoleEnum.Manager or ActorRoleEnum.Admin
                                ? null
                                : "Only Managers can reject escalated tickets."
                        }
                    }
                }
            },
            #endregion

            #region Incident Transitions
            {
                TicketStatusEnum.Incident,
                new Dictionary<TicketStatusEnum, Func<Ticket, ActorRoleEnum, Guid, TransitionResult>>
                {
                    {
                        TicketStatusEnum.Assigned,
                        (ticket, role, userId) => new TransitionResult
                        {
                            IsAllowed = role is ActorRoleEnum.Manager or ActorRoleEnum.Admin,
                            Reason = role is ActorRoleEnum.Manager or ActorRoleEnum.Admin
                                ? null
                                : "Only Managers can assign incident tickets."
                        }
                    }
                }
            },
            #endregion

            #region Resolved Transitions
            {
                TicketStatusEnum.Resolved,
                new Dictionary<TicketStatusEnum, Func<Ticket, ActorRoleEnum, Guid, TransitionResult>>
                {
                    {
                        TicketStatusEnum.ClosedPendingRate,
                        (ticket, role, userId) => new TransitionResult
                        {
                            IsAllowed = role is ActorRoleEnum.Manager or ActorRoleEnum.Admin,
                            Reason = role is ActorRoleEnum.Manager or ActorRoleEnum.Admin
                                ? null
                                : "Only Managers can approve resolved tickets."
                        }
                    },
                    {
                        TicketStatusEnum.InProgress,
                        (ticket, role, userId) => new TransitionResult
                        {
                            IsAllowed = role is ActorRoleEnum.Manager or ActorRoleEnum.Admin,
                            Reason = role is ActorRoleEnum.Manager or ActorRoleEnum.Admin
                                ? null
                                : "Only Managers can reject resolutions."
                        }
                    }
                }
            },
            #endregion

            #region ClosedPendingRate Transitions
            {
                TicketStatusEnum.ClosedPendingRate,
                new Dictionary<TicketStatusEnum, Func<Ticket, ActorRoleEnum, Guid, TransitionResult>>
                {
                    {
                        TicketStatusEnum.Closed,
                        (ticket, role, userId) => new TransitionResult
                        {
                            IsAllowed = (role is ActorRoleEnum.Customer && ticket.CustomerId == userId)
                                        || (role is ActorRoleEnum.System)
                                        || (role is ActorRoleEnum.Admin),
                            Reason = (role is ActorRoleEnum.Customer && ticket.CustomerId == userId)
                                     || (role is ActorRoleEnum.System)
                                     || (role is ActorRoleEnum.Admin)
                                    ? null
                                    : "Only Customer or System can close."
                        }
                    },
                    {
                        TicketStatusEnum.Open,
                        (ticket, role, userId) => {
                            // BR-06: Reopen within 7 days
                            var isAdmin = role is ActorRoleEnum.Admin;
                            var canReopen = isAdmin || ((role is ActorRoleEnum.Customer && ticket.CustomerId == userId) &&
                                            ticket.ApprovedAt.HasValue &&
                                            (DateTime.UtcNow - ticket.ApprovedAt.Value).TotalDays <= 7);

                            return new TransitionResult
                            {
                                IsAllowed = canReopen,
                                Reason = canReopen ? null : "Only Customer can reopen within 7 days of approval."
                            };
                        }
                    }
                }
            }
            #endregion
        };
    }
}
