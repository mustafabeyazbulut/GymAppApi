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

## Immediate follow-up requested by the user, not yet built (see the plan doc once written)

Same message also asked for: a GymAdmin assigning **BranchManager** to their own branches (no endpoint exists for this role yet - `AddStaffMemberCommand`'s validator only allows `Member`/`Trainer`), a GymAdmin inviting **additional GymAdmins** to their own already-existing company (today only SuperAdmin can create a GymAdmin, and only bundled with creating the company itself), a fix to the "already assigned in this company" check in `AddStaffMemberCommandHandler` so a Trainer/BranchManager can hold assignments in **multiple branches of the same company** (today `AnyAsync(a => a.UserId == user.Id && a.CompanyId == branch.CompanyId && a.IsActive)` blocks this for ANY role, which is wrong for staff even though it's probably right for Member), and explicit confirmation that a Trainer already CAN work across multiple different companies (the data model already supports it, the check above just needs narrowing to not block same-company/different-branch). User also said: hold off on Member-adding entirely until the Package/Membership module exists (Package/PackageAssignment/MembershipFreeze — designed in `2026-09-09-gym-yonetim-sistemi-design.md`'s "Paket Modeli" section but zero backend support today) — plan this as its own piece of work, don't implement ad hoc.
