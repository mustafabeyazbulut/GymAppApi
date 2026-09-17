---
name: project-branch-ownership-flow
description: CreateCompany no longer creates a Branch - the GymAdmin creates their own branches via POST /api/branches, which is now properly authorization-scoped. Read this before touching Company/Branch creation.
metadata:
  type: project
---

# Branch creation belongs to the GymAdmin, not SuperAdmin (2026-09-17)

User's correction: "tenant oluşturduk diyelim, süperadmin sonra onun sahibini atadı. o kişi kendine şube oluşturabilmeli. zaten önce şube oluşturacak. sonra isterse o şubeye atamasını yapar." (SuperAdmin creates the company and invites its owner; that owner should create their own branch — the branch comes first, then they staff it.)

Before this: `CreateCompanyCommand` created Company **and** its first Branch in one shot, both decided by SuperAdmin. The GymAdmin never got a say in their own branch's name/address, and there was no way to add a second branch that wasn't going through this same one-shot flow.

## What changed

- `CreateCompanyCommand`/`CreateCompanyCommandHandler`/`CreateCompanyCommandResult`: `BranchName`/`BranchAddress`/`BranchId` removed entirely. SuperAdmin now only creates the `Company` + issues the GymAdmin invitation (see [[project-assignment-invitation-security]]). No Branch exists until the GymAdmin makes one.
- `POST /api/branches` (`CreateBranchCommand`/`CreateBranchCommandHandler`/`BranchesController`) was, until now, a **leftover backend-foundation stub** — `[Authorize]` with no policy, and the handler trusted `request.CompanyId` from the body with zero re-check of the caller's own scope. Literally any authenticated user (even a plain Member) could create a Branch under any CompanyId. Fixed to the same pattern as every other mutation in this codebase: `[Authorize(Policy = "GymAdminOrSuperAdmin")]` + `RequestedByUserId` set server-side from the JWT `sub`, handler re-checks the caller is either SuperAdmin or a GymAdmin whose own `CompanyId` matches `request.CompanyId`.
- Quirk worth knowing: a GymAdmin hitting `POST /api/branches` with **another** company's id gets **404**, not 403 — `Company` itself is tenant-scoped (`GymAppApiDbContext.SetCompanySelfFilter`), so `BranchRules.CompanyMustExistAsync`'s own query can't even see a company outside the caller's tenant scope. This is consistent with how every other cross-tenant read in this API behaves (don't confirm another tenant's existence to someone unauthorized for it) - don't "fix" it to 403.

## New flow end to end

1. SuperAdmin: `POST /api/companies` `{ companyName, gymAdminPhone }` → Company created, GymAdmin invitation SMS'd.
2. Invitee: `POST /api/assignments/confirm` `{ code }` → GymAdmin Assignment now exists.
3. GymAdmin (now authenticated with that role): `POST /api/branches` `{ companyId, name, address }` → creates their own branch(es), as many as they want.
4. GymAdmin/BranchManager: `POST /api/assignments/staff` to staff each branch (existing flow, unchanged, itself gated by the invitation-confirmation flow in [[project-assignment-invitation-security]]).

## Follow-up plan executed and DONE (2026-09-17/18, same day)

The follow-up requested in the message above was written up as `docs/superpowers/plans/2026-09-17-branch-staff-roles-expansion.md` (10 tasks) and fully executed via subagent-driven-development (implementer + spec review + code-quality review per task, with fix-and-reverify loops where reviews found real gaps). Everything landed on `main`:

- **Task 1** (`5627ec9`): fixed the "already assigned in this company" bug — `AddStaffMemberCommandHandler`/`ConfirmAssignmentInvitationCommandHandler`'s duplicate check now scopes by `(CompanyId, BranchId, Role)` instead of `CompanyId` alone, so a Trainer/BranchManager can hold assignments at more than one branch of the same company. (Cross-company multi-assignment already worked, was never blocked — no fix needed there.)
- **Task 2** (`3a4986c`): `AddStaffMemberCommandValidator` now only accepts `Trainer`/`BranchManager` — `Member` removed until the Package/Membership module exists (per the user's explicit instruction, see below).
- **Task 3** (`41e6e2d`): `BranchManager` assignment restricted to `GymAdmin`(of company)/`SuperAdmin` only — a `BranchManager` can no longer assign a peer `BranchManager` (this briefly opened a window between Task 2 and Task 3 landing on `main`; closed same session).
- **Task 4+5** (`d5a65ce`, `d125fbe`): new `InviteGymAdminCommand` + `POST /api/assignments/gym-admin` — an existing GymAdmin can invite a peer GymAdmin to their own company (reuses the same invitation-confirmation security model, zero changes needed to the generic `POST /api/assignments/confirm`).
- **Task 6+fix** (`0032695`, `53f10ee`): `PATCH /api/branches/{id}/active` — GymAdmin(of company)/SuperAdmin can close a branch; `BranchManager` deliberately excluded (can't open/close their own branch). **Known limitation, not a bug:** once closed, only SuperAdmin can reopen it — `Branch`'s global query filter hides inactive branches from everyone else, including the GymAdmin who closed it (same as `Company` deactivation already behaves).
- **Task 7+fix** (`8063396`, `2bf9535`): `PATCH /api/branches/{id}` — rename/re-address a branch, same GymAdmin/SuperAdmin-only authorization as Task 6.
- **Task 8** (`432678a`): `GET /api/branches/{id}` — branch detail, read-only, relies purely on the existing tenant query filter (no manual re-check, matches `GetAll`'s existing pattern) — verified safe for every caller type including zero-assignment callers (fail-closed).
- **Task 9+fix** (`9c32157`, `f011e25`): `DELETE /api/assignments/{id}` — one generic command removing a GymAdmin/BranchManager/Trainer assignment (soft-delete via `IsActive=false`), authorization mirrors who-may-ADD each role exactly. New `LastGymAdminException` (409): a company can never be left with zero active GymAdmins via this endpoint **unless the caller is SuperAdmin** (the explicit override the user asked for — "SuperAdmin can force a GymAdmin change later"). Self-removal (a GymAdmin removing their own assignment) is intentionally allowed, still subject to the last-admin protection. Removal never goes through the SMS-confirmation flow — revoking access is a unilateral authorized action, doesn't need the target's consent (unlike being added).

**Deliberately still not done, per explicit user instruction:** Member-adding stays disabled until `Package`/`PackageAssignment`/`MembershipFreeze` exist (designed in `2026-09-09-gym-yonetim-sistemi-design.md`'s "Paket Modeli" section, zero backend support today) — don't re-enable it ad hoc, that's its own future plan.

**Full test count at plan completion: 151 unit + 57 integration = 208, all green.**
