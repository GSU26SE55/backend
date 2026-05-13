# ADR 0003: Account Extension Tables

Status: Accepted.

Decision: User profile extension data lives in `account_profiles`; staff-only assignment data lives in `staff_profiles` and `staff_skills`.

`Account` remains focused on identity, credentials, account status, and login metadata. Staff assignment capacity, department, availability, and skills do not get added directly to `Account`.
