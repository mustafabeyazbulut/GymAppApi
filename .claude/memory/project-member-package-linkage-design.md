---
name: project-member-package-linkage-design
description: Master roadmap for multi-role/multi-company support and the Package module - role-combination business rules, Package scope decisions, and the decomposed build order (Package core -> Payment -> Check-in -> mobile switcher -> video/content library). Read before touching any of these.
metadata:
  type: project
---

# Multi-role/multi-company + Package module — decisions and roadmap (2026-09-18)

Started from the user's clarification: "Üyeleri bir company'e atamayacağız ama üyelere paket verince otomatik o şirketin bilgilerini görebilecek" (Members won't be assigned to a company via the staff mechanism — giving them a Package is what links them to a company). This grew into a full multi-role/multi-company design session the same day. This file is the master index — **read it first** before starting work in any of the sub-areas below, then follow the `[[...]]` link to that sub-area's own detail file once one exists.

## Core model decision (settled, don't revisit)

- The Member↔Company relationship is **not** an `Assignment` row. It's established when a `PackageAssignment` is created. Giving a Member a package is what grants them visibility into that company — no separate "add member to company" step.
- Self-service registration stays fully open/company-independent (unrelated, never in question — [[feedback-never-remove-registration-pointer]]).
- `AddStaffMemberCommand` permanently excludes `Member` (only `Trainer`/`BranchManager`) — [[project-branch-ownership-flow]] Task 2. Not a gap to close later with the same mechanism.

## Real-world scope: a user can hold MANY simultaneous assignments

`Assignment.assignments` (mobile `MeResult`) already supports a list, and it needs to — a single user routinely holds 2+ roles across companies/branches in real life. Full enumerated catalog of realistic combinations (kept here so it isn't re-derived):

**Single role, single company (baseline):** plain registered Member with no package · Member with active package at Company A · Trainer at one branch · BranchManager of one branch · GymAdmin of a company · SuperAdmin (platform-wide, no company).

**Same company, multiple roles:** Trainer+Member same branch (staff who trains themselves) · BranchManager+Trainer same branch · GymAdmin+Member own company · GymAdmin+Trainer own company · BranchManager+Member same branch.

**Different companies, different roles:** Trainer(A)+Member(B) · GymAdmin(A)+Member(B) · GymAdmin(A)+Trainer(B) · GymAdmin(A)+GymAdmin(B) (owns multiple gym businesses) · BranchManager(A)+BranchManager/Trainer(B) · Trainer(A)+Trainer(B) (freelance, multiple studios) · Member(A)+Member(B) (memberships at two different gyms).

**Same company, multiple branches:** BranchManager of Branch1+Branch2 · Trainer of Branch1+Branch2 · Member with a branch-specific package at Branch1 and a separate one at Branch2 (depends on the Package-scope decision below).

## Business rules — DECIDED (2026-09-18)

- **GymAdmin + BranchManager in the SAME company, same user: BLOCKED.** GymAdmin already covers every branch; a redundant/conflicting BranchManager assignment for that same company must be rejected. Enforce at invitation-issue time (both directions: assigning BranchManager to an existing GymAdmin of that company, and inviting a GymAdmin who already holds a BranchManager assignment there), mirroring the existing `UserAlreadyAssignedException` pattern in `AddStaffMemberCommandHandler`/`InviteGymAdminCommandHandler`. Cross-company combos are unaffected (GymAdmin of A + BranchManager of B is fine).
- **SuperAdmin + any other role/company (including having their own Package somewhere): ALLOWED.** No extra restriction — the data model already permits it, not worth the complexity to block.
- **Package scope decided at the Package TEMPLATE level, not per-assignment.** A GymAdmin/BranchManager creating a Package chooses "tüm şubeler" (company-wide, `BranchId = null`) or "sadece şu şube" (branch-specific, `BranchId` set) when defining it. Every member who receives that package inherits the same scope. Not chosen per PackageAssignment.

## Package module — full real-world scenario catalog (for when the module is actually spec'd)

**Package types/structure:** duration-based membership (monthly/quarterly/yearly) · session-count based (10/20 seans, may also carry an expiry window) · trial package · single-day pass · PT (personal training, tied to one trainer) · group-class package (scoped to specific class types) · combined package (membership+PT+classes) · family/multi-person package (one purchase, several users) · corporate/bulk package · promo/referral package.
**Scope:** company-wide vs single-branch (decided above, chosen per template).
**Lifecycle:** active · expired (kept as history, not deleted) · frozen (MembershipFreeze — extends EndDate by frozen duration) · cancelled early · renewed (continuity tracking to the prior package).
**Content/entitlement gating (see below):** public/no-login · logged-in-but-no-package · any-active-package · specific-tier/specific-package · company-specific content · trainer-specific content (PT students only) · access auto-revoked when package expires/freezes.

## Decomposition and build order (agreed 2026-09-18)

This whole area is too large for one spec/plan. Agreed order, each its own spec → plan → implementation cycle:

1. **Business rules fix (small, backend-only) — DONE (2026-09-18, commit `16fd92e`).** GymAdmin+BranchManager same-company block enforced in `AddStaffMemberCommandHandler`, `InviteGymAdminCommandHandler` (issue-time, both directions) and `ConfirmAssignmentInvitationCommandHandler` (confirm-time, closes the race window between two concurrent invitations). New `ConflictingAssignmentRoleException` (409). Built TDD, 154 unit + 57 integration tests green.
2. **Package core module — DONE (2026-09-18, commits `413eb73`..`fda5896`, 18-task plan `docs/superpowers/plans/2026-09-18-package-core-module.md`).** `Package` (catalog/template: name, description, scope, duration/session-count, an extensible AccessTier-style field for future content-gating, IsActive) + `PackageAssignment` (Member ← Package, dates, status, freeze/cancel) + `PendingPackageAssignmentInvitation`. Assignment requires the **same SMS-confirmation flow as staff invitations** ([[project-assignment-invitation-security]]'s pattern), only **GymAdmin/BranchManager** may give a package (Trainer excluded). `POST /api/packages`, `GET /api/packages[/{id}]`, `PATCH /api/packages/{id}/active`, `POST /api/package-assignments[/confirm|/{id}/freeze|/{id}/unfreeze|/{id}/cancel]`. `GET /api/auth/me` now returns `packageAssignments` — the actual fulfillment of this whole roadmap's original goal. 187 unit + 59 integration tests, all green.
   - **Real bug caught by the full-flow integration test, now fixed:** `GetMeQueryHandler`'s query was itself tenant-filtered, but a plain Member has zero `Assignment` rows, so their own ambient `CompanyId` (resolved by `TenantResolutionService` purely from `Assignment`) is always `null` — the `ICompanyScoped` filter on `PackageAssignment`/`Company`/`Package` silently hid a Member's own package from themselves. Fixed with `.IgnoreQueryFilters()` on that query (safe: already pinned to `u.Id == request.UserId`, can't leak cross-tenant), same rationale `TenantResolutionService` already uses. **Reusable lesson:** any future query that reads a caller's own `ICompanyScoped`/`ITenantScoped` data needs this same treatment if the caller might have no `Assignment` (e.g. a plain Member) — don't assume ambient tenant context is populated just because the caller is authenticated.
   - **Also fixed along the way:** `PendingPackageAssignmentInvitation` originally had a required FK navigation to `Package` (which is tenant-filtered) while itself being intentionally unscoped — same EF "required end of a filtered relationship" class of bug as above, caught via an EF startup warning before it ever reached a test. Fixed by dropping the navigation, keeping a plain `PackageId` int (matches `PendingAssignmentInvitation`'s existing `CompanyId`/`BranchId`-as-plain-ints precedent). Also: `.gitignore`'s standard NuGet `**/[Pp]ackages/*` rule was silently swallowing the new `Features/Packages/` source and test folders — needed a targeted negation.
3. **Payment/installment tracking** — user chose the DETAILED option (installments/partial payments, payment history), not just amount+paid/unpaid. Depends on step 2's entities existing. Not yet built, not yet designed.
4. **Check-in/attendance system** — user chose to build real-time check-in now, not defer it: `RemainingSessions` on a session-based `PackageAssignment` is meant to decrement via actual check-ins (QR/manual), not stay a static info field. This is its own substantial subsystem (staff-facing check-in UX, QR or manual entry flow). Depends on step 2's `PackageAssignment` existing. Not yet built, not yet designed.
5. **Mobile: multi-assignment/multi-company awareness + context switcher** (the original "Parça 3") — `staffAssignment` getter currently returns only the FIRST GymAdmin/BranchManager found; needs to become a list-aware UI (drawer shows every company a user has a role in), and Home/Membership screens need a "which company/package am I viewing" context switcher instead of the current single `hasActiveMembership` bool. Depends on step 2 (real Package data) to be meaningfully testable end to end. Not yet built.
6. **Video/content library** — explicitly deferred to a fully separate future plan (own spec). The only thing carried into step 2 for forward-compatibility is the AccessTier-style field on `Package`; the actual content system (upload/storage/playback, admin content management, entitlement checks against tier) is out of scope until then.

**Current status (2026-09-18): steps 1 and 2 done and pushed to `origin/main`.** Steps 3-6 not started, not designed. Check `docs/superpowers/plans/` for a `*-package-*` plan file before assuming otherwise — if the plan for a given step exists and shows tasks checked off, trust that over this summary.

## If resuming

Read this file top to bottom first. The business rules section is final. The "build order" section is the current plan of record for what to build and in what sequence — follow it rather than re-deriving from scratch, but confirm with the user before starting a step past where the last session left off.
