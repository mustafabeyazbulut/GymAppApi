---
name: project-tenant-onboarding-progress
description: GymAppApi Tenant Onboarding (Backend) implementation progress against docs/superpowers/plans/2026-09-17-tenant-onboarding.md — read this first if resuming in a new console/account
metadata:
  type: project
---

# GymAppApi Tenant Onboarding (Backend) — Implementation Progress

**Plan being executed:** `docs/superpowers/plans/2026-09-17-tenant-onboarding.md`
**Predecessor plan:** [[project-backend-foundation-progress]] (done). This plan makes tenant isolation actually work (it was a no-op stub) and adds Company/Branch/staff-creation endpoints; see the plan's own "Goal" section.

## Task checklist

- [x] Task 1 — `ITenantResolutionService` contract
- [x] Task 2 — `TenantResolutionService` implementation
- [x] Task 3 — `AmbientTenantContext` made settable (commit "Make AmbientTenantContext a settable, dependency-free holder")
- [x] Task 4 — `TenantContextMiddleware` (commit "Add TenantContextMiddleware and authorize BranchesController - tenant isolation is now real") — tenant isolation is now real, not a stub
- [x] Task 5 — `SuperAdminOnly`/`StaffManagement` policies (commit a957694)
- [x] Task 6 — `CreateCompanyCommand` (commit 2440167, code review follow-up in 392b01e: transaction + email-uniqueness check)
- [x] Task 7 — `POST /api/companies`, SuperAdmin-only (commit 3ee9095)
- [x] Task 8 — `AddStaffMemberCommand` (commit a0477c2, code review follow-up in 5c1b926: same transaction + email-uniqueness pattern as Task 6 — **this follow-up commit was sitting uncommitted-but-verified when the session's PC froze; resumed 2026-09-17 by rebuilding/retesting the exact same diff, confirming it matched Task 6's already-committed pattern, then committing it**)
- [x] Task 9 — `POST /api/assignments/staff` (commit 24ef574). **Found during this task, not in the plan's original text:** `AddStaffMemberCommand.Role` is the first request body to expose an enum to clients — `"role":"Member"` failed model binding (400) with no `JsonStringEnumConverter` registered. Fixed by adding `.AddJsonOptions(...)` to `AddControllers()` in `Program.cs`, same commit.
- [ ] Task 10 — Manual end-to-end verification against real Postgres (not started; needs `docker start postgres` + `dotnet run`, see plan for the exact curl sequence)
- [ ] Task 11 — Retire self-service registration (`/api/auth/register/*`) — **do this LAST**, only after GymApp's mobile plan has shipped Add Company/Add Staff Member screens and removed its own Register screen. Until then, self-registration is the only way to create a test account on a fresh DB.

## Known permanent local state (not a bug, don't "fix")

`Presentation/GymAppApi.WebApi/Program.cs` always carries an **uncommitted, local-only** dev CORS block (`AddCors("DevClients", AllowAnyOrigin/Header/Method)` + `app.UseCors("DevClients")` inside the `IsDevelopment()` block) — used to let the mobile app hit this API from a LAN device during manual testing. It was accidentally committed once and reverted (commit 135b67a "Remove accidentally-committed dev-only CORS block from Program.cs"). When staging/committing any other Program.cs change, split the diff so this block stays uncommitted (edit it out, stage, commit, then paste it back in) rather than committing it or deleting it.

## If resuming from a new console/account

1. Read this file, then `docs/superpowers/plans/2026-09-17-tenant-onboarding.md` in full (its "Key facts" section has load-bearing context, e.g. why `ITenantResolutionService` needs `.IgnoreQueryFilters()`).
2. Run `git log --oneline` to confirm which commits actually landed — trust git over this file if they disagree.
3. Continue subagent-driven-development (or direct implementation) from the first unchecked task above — currently Task 10.
4. `git status`/`git diff` first — if there's an uncommitted change beyond the permanent CORS block described above, treat it as in-progress work to finish/verify, not something to discard.
