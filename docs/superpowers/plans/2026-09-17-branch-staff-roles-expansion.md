# Branch & Staff Roles Expansion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a GymAdmin invite peer GymAdmins to their own company and assign BranchManager to their branches, let staff (Trainer/BranchManager) hold assignments in more than one branch of the same company without being wrongly blocked, stop the AddStaffMember endpoint from accepting `Member` as a role until the Package/Membership module exists to actually back a membership, let a GymAdmin close/reopen and rename/re-address their own branches, and let any GymAdmin/BranchManager/Trainer assignment (and a peer GymAdmin) be removed again — with a company never being left with zero active GymAdmins unless SuperAdmin explicitly does it.

**Architecture:** All of this extends the existing invitation-confirmation security model from `docs/superpowers/plans/` 2026-09-17 work (`AssignmentInvitationService` + `PendingAssignmentInvitation` + the generic `POST /api/assignments/confirm`) — every new "attach a role to a person" flow issues a pending invitation and an SMS code exactly like `CreateCompanyCommand`/`AddStaffMemberCommand` already do; nothing here invents a new security model, it reuses that one. The one new command, `InviteGymAdminCommand`, is a company-scoped sibling of `AddStaffMemberCommand` (which is branch-scoped) that lets an *existing* GymAdmin (not just SuperAdmin) issue a GymAdmin invitation for their own company.

**Tech Stack:** ASP.NET Core 10 / .NET 10, MediatR 14, FluentValidation, EF Core 10 + Npgsql, xUnit + Moq (unit), xUnit + EF Core InMemory + `WebApplicationFactory<Program>` (integration) — all already in use, no new packages.

## PLAN STATUS (2026-09-18): ALL 10 TASKS DONE

Executed via superpowers:subagent-driven-development (fresh implementer subagent per task + spec-compliance review + code-quality review, with fix-and-reverify loops wherever a review found a real gap). Final commit per task/fix:

- Task 1 (dedup scoping fix): `5627ec9`
- Task 2 (remove Member, allow BranchManager): `3a4986c`
- Task 3 (restrict BranchManager assignment): `41e6e2d`
- Task 4 (`InviteGymAdminCommand`): `d5a65ce`
- Task 5 (`POST /api/assignments/gym-admin`): `d125fbe`
- Task 6 (`PATCH /api/branches/{id}/active`) + doc-comment fix: `0032695`, `53f10ee`
- Task 7 (`PATCH /api/branches/{id}`) + missing-test fix: `8063396`, `2bf9535`
- Task 8 (`GET /api/branches/{id}`): `432678a`
- Task 9 (`DELETE /api/assignments/{id}`, last-GymAdmin protection) + security-test-gap fix: `9c32157`, `f011e25`
- Task 10 (this memory update): see `.claude/memory/project-branch-ownership-flow.md`

Full suite at completion: **151 unit + 57 integration = 208 tests, all green.** No task was merged with an open Critical/Important review finding — every gap a reviewer found was fixed and re-verified before moving to the next task. Full incident/detail narrative lives in `.claude/memory/project-branch-ownership-flow.md`, not repeated here.

---

## Key facts (read before starting — verified by reading the actual current code)

- **The bug this plan fixes first:** `AddStaffMemberCommandHandler` and `ConfirmAssignmentInvitationCommandHandler` both currently block a new assignment with `UserAlreadyAssignedException` if the target user has **any** active `Assignment` in the same `CompanyId` at all — regardless of `BranchId` or `Role`. That means a Trainer who already works at Branch A of Company X can never be added to Branch B of the same Company X. The correct invariant is per **(CompanyId, BranchId, Role)**, not per CompanyId alone — a `GymAdmin` assignment always has `BranchId = null`, so scoping by all three still correctly prevents a duplicate GymAdmin invite for the same company while allowing a Trainer/BranchManager to hold distinct branch assignments.
- **`AssignmentRole` enum, exact order** (`Core/GymAppApi.Domain/Enums/AssignmentRole.cs`): `SuperAdmin, GymAdmin, BranchManager, Trainer, Member`.
- **`AddStaffMemberCommandValidator`** (`Core/GymAppApi.Application/Features/Assignments/Commands/AddStaffMember/AddStaffMemberCommandValidator.cs`) currently allows `Member` or `Trainer`. This plan changes it to `Trainer` or `BranchManager` — `Member` is removed because the product model requires a `Package`/`PackageAssignment` to back a real membership, and that module doesn't exist yet (out of scope for this plan, deliberately deferred by the user).
- **`AddStaffMemberCommandHandler`'s existing authorization check** (`Core/GymAppApi.Application/Features/Assignments/Commands/AddStaffMember/AddStaffMemberCommandHandler.cs`) lets a `BranchManager` of the target branch add staff too, alongside `GymAdmin`/`SuperAdmin`. That's still correct for `Trainer`, but **not** for `BranchManager` itself — a branch manager must not be able to create peer/other branch managers or promote someone to that role. Only `GymAdmin` (of that company) or `SuperAdmin` may assign `BranchManager`. This plan branches the authorization check on `request.Role`.
- **The generic confirm endpoint needs zero changes for the new `InviteGymAdminCommand` flow.** `POST /api/assignments/confirm` (`ConfirmAssignmentInvitationCommandHandler`) already looks up the caller's own live `PendingAssignmentInvitation` rows by `TargetUserId` alone, with no assumption about which command created them — it's role-agnostic already. Only the already-assigned scoping fix (bullet 1) needs to land there.
- **`AssignmentInvitationService.IssueAsync`** signature (`Core/GymAppApi.Application/Common/Invitations/AssignmentInvitationService.cs`): `IssueAsync(IUnitOfWork unitOfWork, int targetUserId, int companyId, int? branchId, AssignmentRole role, int requestedByUserId, CancellationToken cancellationToken)` — returns the plaintext code, does not save or send SMS itself.
- **`NotificationDispatcher.NotifyUserAsync`** signature (`Core/GymAppApi.Application/Common/Notifications/NotificationDispatcher.cs`): `NotifyUserAsync(IUnitOfWork unitOfWork, IPushNotificationSender pushNotificationSender, int userId, string title, string body, CancellationToken cancellationToken)`.
- **Policy `"GymAdminOrSuperAdmin"`** already exists (`Presentation/GymAppApi.WebApi/Program.cs`, defined in the `AddAuthorization` block) and is exactly the policy the new `POST /api/assignments/gym-admin` endpoint needs — no new policy required.
- **Common exceptions** (`Core/GymAppApi.Application/Common/Exceptions/`): `NotFoundException`(404), `ForbiddenException`(403), `ConflictException(string message)`(409, public constructor taking a message). **Assignments exceptions** (`Core/GymAppApi.Application/Features/Assignments/Exceptions/`): `UserAlreadyAssignedException`(409, no-arg constructor, extends `ConflictException`).
- **`Branch` is `ICompanyScoped, IDeactivatable`** (`Core/GymAppApi.Domain/Entities/Branch.cs`: `CompanyId`, `Name`, `Address`, `IsActive`). Its global query filter (`GymAppApiDbContext.SetCompanyScopedDeactivatableFilter`) is `_tenantContext.IsSuperAdmin || (_tenantContext.CompanyId != null && e.CompanyId == _tenantContext.CompanyId && e.IsActive)` — **a deactivated branch becomes completely invisible to every non-SuperAdmin caller, including its own GymAdmin.** This is the exact same behavior `Company` already has (`SetCompanySelfFilter`), it's not new or branch-specific. Practical consequence for Task 6: a GymAdmin can deactivate their own currently-visible (active) branch just fine, but **cannot see it again afterward to reactivate it** — that read is filtered too. Only SuperAdmin (who bypasses the filter entirely) can reactivate a previously-deactivated branch. This plan does not change that filter or add a bypass for it — it's consistent with how `Company` already behaves, and inventing a bypass (e.g. an `IgnoreQueryFilters()` call, the pattern `TenantResolutionService` uses for its own narrowly-justified case) is explicitly out of scope unless the user asks for self-service reactivation later.
- **Company's existing CRUD shape to mirror for Branch:** `GetCompaniesQuery` (list), `GetCompanyDetailQuery` (`GET /api/companies/{id}`, throws `NotFoundException` if missing), `UpdateCompanyNameCommand` (`PATCH /api/companies/{id}`, `{ Name }`), `SetCompanyActiveCommand` (`PATCH /api/companies/{id}/active`, `{ IsActive }`). These don't re-check caller scope in their handlers because `CompaniesController` is entirely `[Authorize(Policy = "SuperAdminOnly")]` at the class level — any caller who reaches the handler is already fully authorized. `BranchesController`'s `Create` action uses the wider `GymAdminOrSuperAdmin` policy instead, so **every new Branch action in this plan must re-check the caller's own company scope inside the handler**, exactly like `CreateBranchCommandHandler` already does.
- **Test conventions to copy exactly:** unit tests live in `Tests/GymAppApi.UnitTests/Features/<Feature>/`, use a private static `Wire(...)` helper returning `(Mock<IUnitOfWork> uow, ...)` tuples (see `AddStaffMemberCommandHandlerTests.cs` for the closest existing shape — copy it). Integration tests use `IClassFixture<CustomWebApplicationFactory>`, seed via `_factory.Services.CreateScope()` + a raw `GymAppApiDbContext`, mint JWTs via `IJwtTokenService.GenerateAccessToken(new AccessTokenClaims(userId, fullName, email, phone))`. **Any integration test class that seeds a `User` more than once across its own test methods must use a fresh random phone per `SeedAsync()` call** (`CustomWebApplicationFactory`'s DB is shared across every test method in the class; `IReadRepository.GetAsync` uses `FirstOrDefaultAsync`, so a stale duplicate phone from an earlier test silently matches the wrong user instead of throwing — see `AddStaffMemberAuthorizationTests.UniqueCandidatePhone()` for the exact pattern to copy). **Any integration test reading `Assignment` rows directly via its own `db` scope must first do `scope.ServiceProvider.GetRequiredService<AmbientTenantContext>().IsSuperAdmin = true;`** (namespace `GymAppApi.Infrastructure.Tenancy`) — `Assignment` is `ITenantScoped` and that scope's own tenant context was never populated by `TenantContextMiddleware` (that only runs for real HTTP requests), so it defaults fail-closed and silently hides every company-scoped row. Full detail on both gotchas: `.claude/memory/project-assignment-invitation-security.md`.

---

### Task 1: Fix the duplicate-assignment check to scope by (CompanyId, BranchId, Role)

**Files:**
- Modify: `Core/GymAppApi.Application/Features/Assignments/Commands/AddStaffMember/AddStaffMemberCommandHandler.cs`
- Modify: `Core/GymAppApi.Application/Features/Assignments/Commands/ConfirmAssignmentInvitation/ConfirmAssignmentInvitationCommandHandler.cs`
- Test: `Tests/GymAppApi.IntegrationTests/AddStaffMemberAuthorizationTests.cs`

- [ ] **Step 1: Write the failing integration test**

Add this test to `Tests/GymAppApi.IntegrationTests/AddStaffMemberAuthorizationTests.cs` (add it right after `AddStaff_DoesNotCreateTheAssignmentUntilTheCandidateConfirmsTheirOwnCode`):

```csharp
    [Fact]
    public async Task AddStaff_WhenCandidateAlreadyWorksAtAnotherBranchOfTheSameCompany_StillSucceeds()
    {
        var (branchId, candidateId, candidatePhone, branchManagerToken, _, newStaffCandidateToken) = await SeedAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();
        scope.ServiceProvider.GetRequiredService<AmbientTenantContext>().IsSuperAdmin = true;
        var company = db.Branches.Single(b => b.Id == branchId).CompanyId;
        var secondBranch = new Branch { CompanyId = company, Name = "İkinci Şube", Address = "..." };
        db.Branches.Add(secondBranch);
        db.Assignments.Add(new Assignment { UserId = candidateId, CompanyId = company, BranchId = branchId, Role = AssignmentRole.Trainer, IsActive = true });
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", branchManagerToken);
        var addResponse = await client.PostAsJsonAsync("/api/assignments/staff", new { phone = candidatePhone, role = "Trainer", branchId = secondBranch.Id });
        Assert.Equal(HttpStatusCode.Created, addResponse.StatusCode);

        var code = db.PendingAssignmentInvitations.Single(p => p.TargetUserId == candidateId && p.BranchId == secondBranch.Id).Code;

        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", newStaffCandidateToken);
        var confirmResponse = await client.PostAsJsonAsync("/api/assignments/confirm", new { code });

        Assert.Equal(HttpStatusCode.Created, confirmResponse.StatusCode);
        var assignments = db.Assignments.Where(a => a.UserId == candidateId).ToList();
        Assert.Equal(2, assignments.Count);
        Assert.Contains(assignments, a => a.BranchId == branchId);
        Assert.Contains(assignments, a => a.BranchId == secondBranch.Id);
    }
```

This test seeds the candidate as already-active Trainer at the branch `SeedAsync()` returns, then tries to add them as Trainer at a **second** branch of the **same** company via the normal invite+confirm flow, and expects it to succeed with two distinct `Assignment` rows.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --filter AddStaff_WhenCandidateAlreadyWorksAtAnotherBranchOfTheSameCompany_StillSucceeds`
Expected: FAIL — the `addResponse` assertion fails because today's `AddStaffMemberCommandHandler` throws `UserAlreadyAssignedException` (409, not 201) the moment it sees the candidate already has *any* active assignment in that company.

- [ ] **Step 3: Fix `AddStaffMemberCommandHandler`**

In `Core/GymAppApi.Application/Features/Assignments/Commands/AddStaffMember/AddStaffMemberCommandHandler.cs`, replace:

```csharp
        var alreadyAssignedInCompany = await _unitOfWork.GetReadRepository<Assignment>().AnyAsync(
            a => a.UserId == user.Id && a.CompanyId == branch.CompanyId && a.IsActive, cancellationToken);
        if (alreadyAssignedInCompany)
        {
            throw new UserAlreadyAssignedException();
        }
```

with:

```csharp
        // Scoped to (CompanyId, BranchId, Role), not just CompanyId - a
        // Trainer/BranchManager can hold assignments at more than one branch
        // of the same company (and, separately, at any number of other
        // companies - that was never blocked). Only an exact duplicate
        // (same person, same branch, same role) is rejected.
        var alreadyHoldsThisExactAssignment = await _unitOfWork.GetReadRepository<Assignment>().AnyAsync(
            a => a.UserId == user.Id && a.CompanyId == branch.CompanyId && a.BranchId == branch.Id &&
                 a.Role == request.Role && a.IsActive, cancellationToken);
        if (alreadyHoldsThisExactAssignment)
        {
            throw new UserAlreadyAssignedException();
        }
```

- [ ] **Step 4: Fix `ConfirmAssignmentInvitationCommandHandler`**

In `Core/GymAppApi.Application/Features/Assignments/Commands/ConfirmAssignmentInvitation/ConfirmAssignmentInvitationCommandHandler.cs`, replace:

```csharp
        var alreadyAssigned = await _unitOfWork.GetReadRepository<Assignment>().AnyAsync(
            a => a.UserId == matching.TargetUserId && a.CompanyId == matching.CompanyId && a.IsActive, cancellationToken);
```

with:

```csharp
        var alreadyAssigned = await _unitOfWork.GetReadRepository<Assignment>().AnyAsync(
            a => a.UserId == matching.TargetUserId && a.CompanyId == matching.CompanyId &&
                 a.BranchId == matching.BranchId && a.Role == matching.Role && a.IsActive, cancellationToken);
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test --filter AddStaff_WhenCandidateAlreadyWorksAtAnotherBranchOfTheSameCompany_StillSucceeds`
Expected: PASS.

- [ ] **Step 6: Run the full suite to check for regressions**

Run: `dotnet test`
Expected: PASS, all green (165 tests before this task — should now be 166).

- [ ] **Step 7: Commit**

```bash
git add Core/GymAppApi.Application/Features/Assignments/Commands/AddStaffMember/AddStaffMemberCommandHandler.cs Core/GymAppApi.Application/Features/Assignments/Commands/ConfirmAssignmentInvitation/ConfirmAssignmentInvitationCommandHandler.cs Tests/GymAppApi.IntegrationTests/AddStaffMemberAuthorizationTests.cs
git commit -m "Scope duplicate-assignment check by branch and role, not just company"
```

---

### Task 2: Allow `BranchManager` through AddStaffMember, remove `Member`

**Files:**
- Modify: `Core/GymAppApi.Application/Features/Assignments/Commands/AddStaffMember/AddStaffMemberCommandValidator.cs`
- Modify: `Tests/GymAppApi.IntegrationTests/AddStaffMemberAuthorizationTests.cs`

- [ ] **Step 1: Write the failing integration test**

Add to `Tests/GymAppApi.IntegrationTests/AddStaffMemberAuthorizationTests.cs`:

```csharp
    [Fact]
    public async Task AddStaff_WithRoleMember_Returns422()
    {
        var (branchId, _, candidatePhone, branchManagerToken, _, _) = await SeedAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", branchManagerToken);

        var response = await client.PostAsJsonAsync("/api/assignments/staff", new { phone = candidatePhone, role = "Member", branchId });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --filter AddStaff_WithRoleMember_Returns422`
Expected: FAIL — today `Member` is accepted by the validator, so this currently returns 201, not 422.

- [ ] **Step 3: Update the validator**

In `Core/GymAppApi.Application/Features/Assignments/Commands/AddStaffMember/AddStaffMemberCommandValidator.cs`, replace:

```csharp
        RuleFor(x => x.Role).Must(r => r is AssignmentRole.Member or AssignmentRole.Trainer)
            .WithMessage("Role must be Member or Trainer.");
```

with:

```csharp
        // Member is deliberately NOT allowed here yet - the product model
        // requires a Package/PackageAssignment to back a real membership,
        // and that module doesn't exist yet. Member-adding returns once it
        // does; don't add it back ad hoc.
        RuleFor(x => x.Role).Must(r => r is AssignmentRole.Trainer or AssignmentRole.BranchManager)
            .WithMessage("Role must be Trainer or BranchManager.");
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test --filter AddStaff_WithRoleMember_Returns422`
Expected: PASS.

- [ ] **Step 5: Fix the two existing tests that used `role = "Member"` as an arbitrary allowed role**

In `Tests/GymAppApi.IntegrationTests/AddStaffMemberAuthorizationTests.cs`, these three call sites use `role = "Member"` only because that used to be one of two valid choices — they're testing authorization/not-found behavior, not anything Member-specific, so switch them to `"Trainer"`:
- `AddStaff_WithBranchManagerToken_Returns201`
- `AddStaff_WhenPhoneIsNotARegisteredUser_Returns404`
- `AddStaff_WithPlainMemberToken_Returns403`

Also update `Assert.Equal(AssignmentRole.Member, assignment.Role);` to `Assert.Equal(AssignmentRole.Trainer, assignment.Role);` in `AddStaff_DoesNotCreateTheAssignmentUntilTheCandidateConfirmsTheirOwnCode` (it already sends `role = "Member"` in its `POST /api/assignments/staff` call — change that to `"Trainer"` too).

- [ ] **Step 6: Run the full suite**

Run: `dotnet test`
Expected: PASS, all green.

- [ ] **Step 7: Commit**

```bash
git add Core/GymAppApi.Application/Features/Assignments/Commands/AddStaffMember/AddStaffMemberCommandValidator.cs Tests/GymAppApi.IntegrationTests/AddStaffMemberAuthorizationTests.cs
git commit -m "Stop accepting Member in AddStaffMember until Package module exists, allow BranchManager"
```

---

### Task 3: Restrict who can assign `BranchManager`

**Files:**
- Modify: `Core/GymAppApi.Application/Features/Assignments/Commands/AddStaffMember/AddStaffMemberCommandHandler.cs`
- Test: `Tests/GymAppApi.UnitTests/Features/Assignments/AddStaffMemberCommandHandlerTests.cs`

- [ ] **Step 1: Write the failing unit tests**

Add these two tests to `Tests/GymAppApi.UnitTests/Features/Assignments/AddStaffMemberCommandHandlerTests.cs` (same file, same `Wire(...)` helper already defined there — just add new `[Fact]` methods):

```csharp
    [Fact]
    public async Task Handle_WhenAssigningBranchManagerAsAGymAdminOfTheCompany_Succeeds()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var existingUser = new User { Id = 7, FullName = "Existing", Phone = "+905550003333", PasswordHash = "x" };
        var (uow, _, assignmentWriteRepo, _) = Wire(callerAssignments, Branch1(), existingUser, alreadyAssigned: false);
        var command = ValidCommand();
        command.Role = AssignmentRole.BranchManager;
        var handler = new AddStaffMemberCommandHandler(uow.Object, Mock.Of<ISmsSender>(), Mock.Of<IPushNotificationSender>());

        await handler.Handle(command, CancellationToken.None);

        assignmentWriteRepo.Verify(r => r.AddAsync(It.IsAny<Assignment>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenAssigningBranchManagerAsAPeerBranchManager_ThrowsForbiddenException()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, BranchId = BranchIdInCompany1, Role = AssignmentRole.BranchManager, IsActive = true } };
        var existingUser = new User { Id = 7, FullName = "Existing", Phone = "+905550003333", PasswordHash = "x" };
        var (uow, _, assignmentWriteRepo, invitationWriteRepo) = Wire(callerAssignments, Branch1(), existingUser, alreadyAssigned: false);
        var command = ValidCommand();
        command.Role = AssignmentRole.BranchManager;
        var handler = new AddStaffMemberCommandHandler(uow.Object, Mock.Of<ISmsSender>(), Mock.Of<IPushNotificationSender>());

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(command, CancellationToken.None));

        assignmentWriteRepo.Verify(r => r.AddAsync(It.IsAny<Assignment>(), default), Times.Never);
        invitationWriteRepo.Verify(r => r.AddAsync(It.IsAny<PendingAssignmentInvitation>(), default), Times.Never);
    }
```

Note: the first test above asserts `assignmentWriteRepo` is never called (that's always true in this handler now — it only ever writes a `PendingAssignmentInvitation`, never an `Assignment` directly) purely as a smoke check that the call didn't throw; the real "did it succeed" signal is that no exception was thrown. This mirrors the existing `Handle_WhenCallerIsGymAdminOfTheBranchsCompany_AttachesTheExistingUserAsAssignment` test's shape in the same file.

- [ ] **Step 2: Run the tests to verify the second one fails**

Run: `dotnet test --filter AddStaffMemberCommandHandlerTests`
Expected: `Handle_WhenAssigningBranchManagerAsAGymAdminOfTheCompany_Succeeds` PASSES already (GymAdmin was always allowed by the existing check). `Handle_WhenAssigningBranchManagerAsAPeerBranchManager_ThrowsForbiddenException` FAILS — today a BranchManager of that exact branch is allowed to add any Trainer/BranchManager, so no exception is thrown.

- [ ] **Step 3: Update the authorization check**

In `Core/GymAppApi.Application/Features/Assignments/Commands/AddStaffMember/AddStaffMemberCommandHandler.cs`, replace:

```csharp
        var callerIsAuthorized = callerAssignments.Any(a =>
            a.Role == AssignmentRole.SuperAdmin ||
            (a.Role == AssignmentRole.GymAdmin && a.CompanyId == branch.CompanyId) ||
            (a.Role == AssignmentRole.BranchManager && a.BranchId == branch.Id));
        if (!callerIsAuthorized)
        {
            throw new ForbiddenException("Bu şubeye üye/antrenör ekleme yetkiniz yok.");
        }
```

with:

```csharp
        // A BranchManager may add Trainers to their own branch, but must
        // never be able to create peer/other BranchManagers - only GymAdmin
        // (of this company) or SuperAdmin can assign that role.
        var callerIsAuthorized = request.Role == AssignmentRole.BranchManager
            ? callerAssignments.Any(a =>
                a.Role == AssignmentRole.SuperAdmin ||
                (a.Role == AssignmentRole.GymAdmin && a.CompanyId == branch.CompanyId))
            : callerAssignments.Any(a =>
                a.Role == AssignmentRole.SuperAdmin ||
                (a.Role == AssignmentRole.GymAdmin && a.CompanyId == branch.CompanyId) ||
                (a.Role == AssignmentRole.BranchManager && a.BranchId == branch.Id));
        if (!callerIsAuthorized)
        {
            throw new ForbiddenException("Bu şubeye personel ekleme yetkiniz yok.");
        }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter AddStaffMemberCommandHandlerTests`
Expected: PASS, all of them (7 tests in this file now).

- [ ] **Step 5: Run the full suite**

Run: `dotnet test`
Expected: PASS, all green.

- [ ] **Step 6: Commit**

```bash
git add Core/GymAppApi.Application/Features/Assignments/Commands/AddStaffMember/AddStaffMemberCommandHandler.cs Tests/GymAppApi.UnitTests/Features/Assignments/AddStaffMemberCommandHandlerTests.cs
git commit -m "Restrict BranchManager assignment to GymAdmin/SuperAdmin, not peer BranchManagers"
```

---

### Task 4: `InviteGymAdminCommand` — a GymAdmin invites a peer GymAdmin to their own company

**Files:**
- Create: `Core/GymAppApi.Application/Features/Assignments/Commands/InviteGymAdmin/InviteGymAdminCommand.cs`
- Create: `Core/GymAppApi.Application/Features/Assignments/Commands/InviteGymAdmin/InviteGymAdminCommandValidator.cs`
- Create: `Core/GymAppApi.Application/Features/Assignments/Commands/InviteGymAdmin/InviteGymAdminCommandResult.cs`
- Create: `Core/GymAppApi.Application/Features/Assignments/Commands/InviteGymAdmin/InviteGymAdminCommandHandler.cs`
- Test: `Tests/GymAppApi.UnitTests/Features/Assignments/InviteGymAdminCommandHandlerTests.cs`

This is a company-scoped sibling of `AddStaffMemberCommand` (which is branch-scoped): same invitation-issuing tail (SMS + in-app notification), same "policy proves SOME role, handler re-checks scope" pattern, but the target role is always `GymAdmin` and there's no `BranchId`.

- [ ] **Step 1: Write the command, validator, and result**

```csharp
// InviteGymAdminCommand.cs
using MediatR;

namespace GymAppApi.Application.Features.Assignments.Commands.InviteGymAdmin;

public class InviteGymAdminCommand : IRequest<InviteGymAdminCommandResult>
{
    public int CompanyId { get; set; }

    // Looks up an already-registered user by phone and invites them to be a
    // GymAdmin of this company too - this never creates a new User, same
    // rule as CreateCompanyCommand/AddStaffMemberCommand.
    public string Phone { get; set; } = null!;

    // Set by the controller from the caller's own JWT sub claim - never
    // trusted from the body. The [Authorize(Policy = "GymAdminOrSuperAdmin")]
    // policy only proves the caller holds SOME such role somewhere; the
    // handler re-checks a GymAdmin caller is scoped to THIS CompanyId.
    public int RequestedByUserId { get; set; }
}
```

```csharp
// InviteGymAdminCommandValidator.cs
using FluentValidation;

namespace GymAppApi.Application.Features.Assignments.Commands.InviteGymAdmin;

public class InviteGymAdminCommandValidator : AbstractValidator<InviteGymAdminCommand>
{
    public InviteGymAdminCommandValidator()
    {
        RuleFor(x => x.CompanyId).GreaterThan(0);
        RuleFor(x => x.Phone).NotEmpty().Matches(@"^\+[1-9]\d{7,14}$");
    }
}
```

```csharp
// InviteGymAdminCommandResult.cs
namespace GymAppApi.Application.Features.Assignments.Commands.InviteGymAdmin;

public class InviteGymAdminCommandResult
{
    public int UserId { get; set; }
    public int CompanyId { get; set; }
}
```

- [ ] **Step 2: Write the failing tests**

```csharp
// Tests/GymAppApi.UnitTests/Features/Assignments/InviteGymAdminCommandHandlerTests.cs
using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Assignments.Commands.InviteGymAdmin;
using GymAppApi.Application.Features.Assignments.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Assignments;

public class InviteGymAdminCommandHandlerTests
{
    private const int CallerId = 42;

    private static (Mock<IUnitOfWork> uow, Mock<IWriteRepository<PendingAssignmentInvitation>> invitationWriteRepo) Wire(
        bool companyExists, IReadOnlyList<Assignment> callerAssignments, User? existingUser, bool alreadyGymAdmin)
    {
        var companyReadRepo = new Mock<IReadRepository<Company>>();
        companyReadRepo.Setup(r => r.GetAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Company, bool>>>(), null, false, default))
            .ReturnsAsync(companyExists ? new Company { Id = 1, Name = "Test Co", IsActive = true } : null);

        var assignmentReadRepo = new Mock<IReadRepository<Assignment>>();
        assignmentReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Assignment, bool>>>(), null, null, false, default))
            .ReturnsAsync(callerAssignments);
        assignmentReadRepo.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Assignment, bool>>>(), default))
            .ReturnsAsync(alreadyGymAdmin);

        var userReadRepo = new Mock<IReadRepository<User>>();
        userReadRepo.Setup(r => r.GetAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), null, false, default))
            .ReturnsAsync(existingUser);

        var invitationReadRepo = new Mock<IReadRepository<PendingAssignmentInvitation>>();
        invitationReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<PendingAssignmentInvitation, bool>>>(), null, null, false, default))
            .ReturnsAsync(new List<PendingAssignmentInvitation>());
        var invitationWriteRepo = new Mock<IWriteRepository<PendingAssignmentInvitation>>();

        var notificationWriteRepo = new Mock<IWriteRepository<Notification>>();
        var deviceTokenReadRepo = new Mock<IReadRepository<DeviceToken>>();
        deviceTokenReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<DeviceToken, bool>>>(), null, null, false, default))
            .ReturnsAsync(new List<DeviceToken>());

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Company>()).Returns(companyReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(assignmentReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<User>()).Returns(userReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<PendingAssignmentInvitation>()).Returns(invitationReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<PendingAssignmentInvitation>()).Returns(invitationWriteRepo.Object);
        uow.Setup(u => u.GetWriteRepository<Notification>()).Returns(notificationWriteRepo.Object);
        uow.Setup(u => u.GetReadRepository<DeviceToken>()).Returns(deviceTokenReadRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        return (uow, invitationWriteRepo);
    }

    private static InviteGymAdminCommand ValidCommand() => new()
    {
        CompanyId = 1,
        Phone = "+905550003333",
        RequestedByUserId = CallerId,
    };

    [Fact]
    public async Task Handle_WhenCallerIsGymAdminOfThisCompanyAndPhoneBelongsToAnExistingUser_IssuesAPendingInvitation()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var existingUser = new User { Id = 7, FullName = "Existing", Phone = "+905550003333", PasswordHash = "x" };
        var (uow, invitationWriteRepo) = Wire(companyExists: true, callerAssignments, existingUser, alreadyGymAdmin: false);
        var smsSender = new Mock<ISmsSender>();
        var handler = new InviteGymAdminCommandHandler(uow.Object, smsSender.Object, Mock.Of<IPushNotificationSender>());

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.Equal(7, result.UserId);
        Assert.Equal(1, result.CompanyId);
        invitationWriteRepo.Verify(r => r.AddAsync(It.Is<PendingAssignmentInvitation>(p =>
            p.TargetUserId == 7 && p.CompanyId == 1 && p.BranchId == null && p.Role == AssignmentRole.GymAdmin), default), Times.Once);
        smsSender.Verify(s => s.SendAsync("+905550003333", It.IsAny<string>(), default), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCompanyDoesNotExist_ThrowsNotFoundException()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = null, Role = AssignmentRole.SuperAdmin, IsActive = true } };
        var (uow, _) = Wire(companyExists: false, callerAssignments, existingUser: null, alreadyGymAdmin: false);
        var handler = new InviteGymAdminCommandHandler(uow.Object, Mock.Of<ISmsSender>(), Mock.Of<IPushNotificationSender>());

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(ValidCommand(), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenCallerIsGymAdminOfADifferentCompany_ThrowsForbiddenException()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 999, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, invitationWriteRepo) = Wire(companyExists: true, callerAssignments, existingUser: null, alreadyGymAdmin: false);
        var handler = new InviteGymAdminCommandHandler(uow.Object, Mock.Of<ISmsSender>(), Mock.Of<IPushNotificationSender>());

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(ValidCommand(), CancellationToken.None));
        invitationWriteRepo.Verify(r => r.AddAsync(It.IsAny<PendingAssignmentInvitation>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenPhoneDoesNotBelongToAnyRegisteredUser_ThrowsNotFoundException()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, invitationWriteRepo) = Wire(companyExists: true, callerAssignments, existingUser: null, alreadyGymAdmin: false);
        var handler = new InviteGymAdminCommandHandler(uow.Object, Mock.Of<ISmsSender>(), Mock.Of<IPushNotificationSender>());

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(ValidCommand(), CancellationToken.None));
        invitationWriteRepo.Verify(r => r.AddAsync(It.IsAny<PendingAssignmentInvitation>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenAlreadyGymAdminOfThisCompany_ThrowsUserAlreadyAssignedException()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var existingUser = new User { Id = 7, FullName = "Existing", Phone = "+905550003333", PasswordHash = "x" };
        var (uow, invitationWriteRepo) = Wire(companyExists: true, callerAssignments, existingUser, alreadyGymAdmin: true);
        var handler = new InviteGymAdminCommandHandler(uow.Object, Mock.Of<ISmsSender>(), Mock.Of<IPushNotificationSender>());

        await Assert.ThrowsAsync<UserAlreadyAssignedException>(() => handler.Handle(ValidCommand(), CancellationToken.None));
        invitationWriteRepo.Verify(r => r.AddAsync(It.IsAny<PendingAssignmentInvitation>(), default), Times.Never);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test --filter InviteGymAdminCommandHandlerTests`
Expected: FAIL — `InviteGymAdminCommandHandler` does not exist yet (compile error).

- [ ] **Step 4: Write the handler**

```csharp
// InviteGymAdminCommandHandler.cs
using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.Invitations;
using GymAppApi.Application.Common.Notifications;
using GymAppApi.Application.Features.Assignments.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.Assignments.Commands.InviteGymAdmin;

public class InviteGymAdminCommandHandler : IRequestHandler<InviteGymAdminCommand, InviteGymAdminCommandResult>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISmsSender _smsSender;
    private readonly IPushNotificationSender _pushNotificationSender;

    public InviteGymAdminCommandHandler(IUnitOfWork unitOfWork, ISmsSender smsSender, IPushNotificationSender pushNotificationSender)
    {
        _unitOfWork = unitOfWork;
        _smsSender = smsSender;
        _pushNotificationSender = pushNotificationSender;
    }

    public async Task<InviteGymAdminCommandResult> Handle(InviteGymAdminCommand request, CancellationToken cancellationToken)
    {
        var company = await _unitOfWork.GetReadRepository<Company>()
            .GetAsync(c => c.Id == request.CompanyId, cancellationToken: cancellationToken);
        if (company is null)
        {
            throw new NotFoundException($"Firma {request.CompanyId} bulunamadı.");
        }

        // Same pattern as every other mutation here: the [Authorize] policy
        // only proves the caller holds SOME GymAdmin/SuperAdmin assignment
        // somewhere - re-check it's scoped to THIS company.
        var callerAssignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
            a => a.UserId == request.RequestedByUserId && a.IsActive, cancellationToken: cancellationToken);
        var callerIsAuthorized = callerAssignments.Any(a =>
            a.Role == AssignmentRole.SuperAdmin ||
            (a.Role == AssignmentRole.GymAdmin && a.CompanyId == request.CompanyId));
        if (!callerIsAuthorized)
        {
            throw new ForbiddenException("Bu firma için Gym Admin daveti gönderme yetkiniz yok.");
        }

        // Never creates a new User - same rule as CreateCompanyCommand. See
        // .claude/memory/feedback-never-remove-registration-pointer.md.
        var invitedUser = await _unitOfWork.GetReadRepository<User>()
            .GetAsync(u => u.Phone == request.Phone, cancellationToken: cancellationToken);
        if (invitedUser is null)
        {
            throw new NotFoundException($"'{request.Phone}' numaralı kayıtlı bir kullanıcı bulunamadı.");
        }

        var alreadyGymAdminOfThisCompany = await _unitOfWork.GetReadRepository<Assignment>().AnyAsync(
            a => a.UserId == invitedUser.Id && a.CompanyId == request.CompanyId && a.BranchId == null &&
                 a.Role == AssignmentRole.GymAdmin && a.IsActive, cancellationToken);
        if (alreadyGymAdminOfThisCompany)
        {
            throw new UserAlreadyAssignedException();
        }

        // Security requirement: knowing this phone number is never enough by
        // itself - the Assignment only comes into existence once the
        // invitee confirms this code themselves (POST /api/assignments/confirm).
        var code = await AssignmentInvitationService.IssueAsync(
            _unitOfWork, invitedUser.Id, request.CompanyId, null, AssignmentRole.GymAdmin, request.RequestedByUserId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _smsSender.SendAsync(
            request.Phone,
            $"GymApp'te '{company.Name}' firmasının Gym Admin'i olmak üzeresiniz. Onay kodu: {code} (10 dakika geçerli).",
            cancellationToken);
        await NotificationDispatcher.NotifyUserAsync(
            _unitOfWork, _pushNotificationSender, invitedUser.Id,
            "Yeni firma daveti",
            "Bir firmanın Gym Admin'i olmanız için davet gönderildi. Telefonunuza gelen kodla onaylayabilirsiniz.",
            cancellationToken);

        return new InviteGymAdminCommandResult
        {
            UserId = invitedUser.Id,
            CompanyId = request.CompanyId,
        };
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter InviteGymAdminCommandHandlerTests`
Expected: PASS, 5/5.

- [ ] **Step 6: Commit**

```bash
git add Core/GymAppApi.Application/Features/Assignments/Commands/InviteGymAdmin/ Tests/GymAppApi.UnitTests/Features/Assignments/InviteGymAdminCommandHandlerTests.cs
git commit -m "Add InviteGymAdminCommand - a GymAdmin can invite a peer GymAdmin to their own company"
```

---

### Task 5: `POST /api/assignments/gym-admin`

**Files:**
- Modify: `Presentation/GymAppApi.WebApi/Controllers/AssignmentsController.cs`
- Test: `Tests/GymAppApi.IntegrationTests/InviteGymAdminAuthorizationTests.cs`

- [ ] **Step 1: Write the failing integration tests**

```csharp
// Tests/GymAppApi.IntegrationTests/InviteGymAdminAuthorizationTests.cs
using System.Net;
using System.Net.Http.Json;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.Persistence.Context;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GymAppApi.IntegrationTests;

public class InviteGymAdminAuthorizationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public InviteGymAdminAuthorizationTests(CustomWebApplicationFactory factory) => _factory = factory;

    private static string UniquePhone() => $"+9055503{Random.Shared.Next(10000, 99999)}";

    private async Task<(int companyId, string gymAdminToken, string memberToken, string candidatePhone)> SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();

        var company = new Company { Name = "Test Co", IsActive = true };
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        var gymAdmin = new User { FullName = "Gym Admin", Phone = UniquePhone(), PasswordHash = "x" };
        var member = new User { FullName = "Plain Member", Phone = UniquePhone(), PasswordHash = "x" };
        var candidate = new User { FullName = "Future Peer Admin", Phone = UniquePhone(), PasswordHash = "x" };
        db.Users.AddRange(gymAdmin, member, candidate);
        await db.SaveChangesAsync();
        db.Assignments.Add(new Assignment { UserId = gymAdmin.Id, CompanyId = company.Id, Role = AssignmentRole.GymAdmin, IsActive = true });
        await db.SaveChangesAsync();

        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var gymAdminToken = jwtService.GenerateAccessToken(new AccessTokenClaims(gymAdmin.Id, gymAdmin.FullName, gymAdmin.Email, gymAdmin.Phone)).Token;
        var memberToken = jwtService.GenerateAccessToken(new AccessTokenClaims(member.Id, member.FullName, member.Email, member.Phone)).Token;

        return (company.Id, gymAdminToken, memberToken, candidate.Phone);
    }

    [Fact]
    public async Task Invite_WithoutToken_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/assignments/gym-admin", new { companyId = 1, phone = "+905550000001" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Invite_WithPlainMemberToken_Returns403()
    {
        var (companyId, _, memberToken, candidatePhone) = await SeedAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", memberToken);

        var response = await client.PostAsJsonAsync("/api/assignments/gym-admin", new { companyId, phone = candidatePhone });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Invite_ThenConfirm_AsGymAdminOfOwnCompany_CreatesAPeerGymAdminAssignment()
    {
        var (companyId, gymAdminToken, _, candidatePhone) = await SeedAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", gymAdminToken);
        var inviteResponse = await client.PostAsJsonAsync("/api/assignments/gym-admin", new { companyId, phone = candidatePhone });
        Assert.Equal(HttpStatusCode.Created, inviteResponse.StatusCode);

        var candidate = db.Users.Single(u => u.Phone == candidatePhone);
        var code = db.PendingAssignmentInvitations.Single(p => p.TargetUserId == candidate.Id).Code;
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var candidateToken = jwtService.GenerateAccessToken(new AccessTokenClaims(candidate.Id, candidate.FullName, candidate.Email, candidate.Phone)).Token;

        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", candidateToken);
        var confirmResponse = await client.PostAsJsonAsync("/api/assignments/confirm", new { code });

        Assert.Equal(HttpStatusCode.Created, confirmResponse.StatusCode);
        scope.ServiceProvider.GetRequiredService<GymAppApi.Infrastructure.Tenancy.AmbientTenantContext>().IsSuperAdmin = true;
        var assignment = db.Assignments.Single(a => a.UserId == candidate.Id);
        Assert.Equal(companyId, assignment.CompanyId);
        Assert.Equal(AssignmentRole.GymAdmin, assignment.Role);
        Assert.Null(assignment.BranchId);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter InviteGymAdminAuthorizationTests`
Expected: FAIL — `/api/assignments/gym-admin` route doesn't exist yet (404, not the asserted status codes).

- [ ] **Step 3: Add the controller action**

In `Presentation/GymAppApi.WebApi/Controllers/AssignmentsController.cs`, add the import:

```csharp
using GymAppApi.Application.Features.Assignments.Commands.InviteGymAdmin;
```

and add this action (anywhere inside the class, e.g. right after `AddStaffMember`):

```csharp
    [Authorize(Policy = "GymAdminOrSuperAdmin")]
    [HttpPost("gym-admin")]
    public async Task<IActionResult> InviteGymAdmin(InviteGymAdminCommand command, CancellationToken cancellationToken)
    {
        command.RequestedByUserId = CurrentUserId;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter InviteGymAdminAuthorizationTests`
Expected: PASS, 3/3.

- [ ] **Step 5: Run the full suite**

Run: `dotnet test`
Expected: PASS, all green.

- [ ] **Step 6: Commit**

```bash
git add Presentation/GymAppApi.WebApi/Controllers/AssignmentsController.cs Tests/GymAppApi.IntegrationTests/InviteGymAdminAuthorizationTests.cs
git commit -m "Add POST /api/assignments/gym-admin for GymAdmin/SuperAdmin"
```

---

### Task 6: `SetBranchActiveCommand` — GymAdmin closes/reopens their own branch

**Files:**
- Create: `Core/GymAppApi.Application/Features/Branches/Commands/SetBranchActive/SetBranchActiveCommand.cs`
- Create: `Core/GymAppApi.Application/Features/Branches/Commands/SetBranchActive/SetBranchActiveCommandHandler.cs`
- Modify: `Presentation/GymAppApi.WebApi/Controllers/BranchesController.cs`
- Test: `Tests/GymAppApi.UnitTests/Features/Branches/SetBranchActiveCommandHandlerTests.cs`
- Test: `Tests/GymAppApi.IntegrationTests/BranchesAuthorizationTests.cs`

- [ ] **Step 1: Write the command**

```csharp
// SetBranchActiveCommand.cs
using MediatR;

namespace GymAppApi.Application.Features.Branches.Commands.SetBranchActive;

public class SetBranchActiveCommand : IRequest
{
    // Set by the controller from the route segment, never trusted from the body.
    public int BranchId { get; set; }

    public bool IsActive { get; set; }

    // Set by the controller from the caller's own JWT sub claim - see
    // CreateBranchCommand for why this re-check is needed on this
    // controller (unlike CompaniesController's SuperAdminOnly actions).
    public int RequestedByUserId { get; set; }
}
```

- [ ] **Step 2: Write the failing unit tests**

```csharp
// Tests/GymAppApi.UnitTests/Features/Branches/SetBranchActiveCommandHandlerTests.cs
using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Branches.Commands.SetBranchActive;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;
using Xunit;

namespace GymAppApi.UnitTests.Features.Branches;

public class SetBranchActiveCommandHandlerTests
{
    private const int CallerId = 42;

    private static (Mock<IUnitOfWork> uow, Mock<IWriteRepository<Branch>> branchWriteRepo) Wire(
        Branch? branch, IReadOnlyList<Assignment> callerAssignments)
    {
        var branchReadRepo = new Mock<IReadRepository<Branch>>();
        branchReadRepo.Setup(r => r.GetAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Branch, bool>>>(), null, false, default))
            .ReturnsAsync(branch);
        var branchWriteRepo = new Mock<IWriteRepository<Branch>>();

        var assignmentReadRepo = new Mock<IReadRepository<Assignment>>();
        assignmentReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Assignment, bool>>>(), null, null, false, default))
            .ReturnsAsync(callerAssignments);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Branch>()).Returns(branchReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<Branch>()).Returns(branchWriteRepo.Object);
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(assignmentReadRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        return (uow, branchWriteRepo);
    }

    private static Branch ExistingBranch() => new() { Id = 5, CompanyId = 1, Name = "Merkez", Address = "...", IsActive = true };

    [Fact]
    public async Task Handle_WhenBranchDoesNotExist_ThrowsNotFoundException()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, _) = Wire(branch: null, callerAssignments);
        var handler = new SetBranchActiveCommandHandler(uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new SetBranchActiveCommand { BranchId = 5, IsActive = false, RequestedByUserId = CallerId }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenCallerIsGymAdminOfThisBranchsCompany_DeactivatesIt()
    {
        var branch = ExistingBranch();
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, branchWriteRepo) = Wire(branch, callerAssignments);
        var handler = new SetBranchActiveCommandHandler(uow.Object);

        await handler.Handle(new SetBranchActiveCommand { BranchId = 5, IsActive = false, RequestedByUserId = CallerId }, CancellationToken.None);

        Assert.False(branch.IsActive);
        branchWriteRepo.Verify(r => r.Update(branch), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCallerIsGymAdminOfADifferentCompany_ThrowsForbiddenException()
    {
        var branch = ExistingBranch();
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 999, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, branchWriteRepo) = Wire(branch, callerAssignments);
        var handler = new SetBranchActiveCommandHandler(uow.Object);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new SetBranchActiveCommand { BranchId = 5, IsActive = false, RequestedByUserId = CallerId }, CancellationToken.None));
        branchWriteRepo.Verify(r => r.Update(It.IsAny<Branch>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenCallerIsBranchManagerNotGymAdmin_ThrowsForbiddenException()
    {
        var branch = ExistingBranch();
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, BranchId = 5, Role = AssignmentRole.BranchManager, IsActive = true } };
        var (uow, branchWriteRepo) = Wire(branch, callerAssignments);
        var handler = new SetBranchActiveCommandHandler(uow.Object);

        // A branch's own manager decides day-to-day operations, not whether
        // the branch itself exists - opening/closing is a GymAdmin/SuperAdmin
        // decision, same principle as who may assign BranchManager (Task 3).
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new SetBranchActiveCommand { BranchId = 5, IsActive = false, RequestedByUserId = CallerId }, CancellationToken.None));
        branchWriteRepo.Verify(r => r.Update(It.IsAny<Branch>()), Times.Never);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test --filter SetBranchActiveCommandHandlerTests`
Expected: FAIL — `SetBranchActiveCommandHandler` does not exist yet.

- [ ] **Step 4: Write the handler**

```csharp
// SetBranchActiveCommandHandler.cs
using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.Branches.Commands.SetBranchActive;

public class SetBranchActiveCommandHandler : IRequestHandler<SetBranchActiveCommand>
{
    private readonly IUnitOfWork _unitOfWork;

    public SetBranchActiveCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task Handle(SetBranchActiveCommand request, CancellationToken cancellationToken)
    {
        var branch = await _unitOfWork.GetReadRepository<Branch>()
            .GetAsync(b => b.Id == request.BranchId, cancellationToken: cancellationToken);
        if (branch is null)
        {
            throw new NotFoundException($"Şube {request.BranchId} bulunamadı.");
        }

        // Same re-check pattern as CreateBranchCommandHandler - opening or
        // closing a branch is a GymAdmin(of this company)/SuperAdmin
        // decision, not the branch's own BranchManager's call.
        var callerAssignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
            a => a.UserId == request.RequestedByUserId && a.IsActive, cancellationToken: cancellationToken);
        var callerIsAuthorized = callerAssignments.Any(a =>
            a.Role == AssignmentRole.SuperAdmin ||
            (a.Role == AssignmentRole.GymAdmin && a.CompanyId == branch.CompanyId));
        if (!callerIsAuthorized)
        {
            throw new ForbiddenException("Bu şubeyi aktif/pasif yapma yetkiniz yok.");
        }

        branch.IsActive = request.IsActive;
        _unitOfWork.GetWriteRepository<Branch>().Update(branch);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter SetBranchActiveCommandHandlerTests`
Expected: PASS, 4/4.

- [ ] **Step 6: Add the controller action**

In `Presentation/GymAppApi.WebApi/Controllers/BranchesController.cs`, add the import `using GymAppApi.Application.Features.Branches.Commands.SetBranchActive;` and this action:

```csharp
    [Authorize(Policy = "GymAdminOrSuperAdmin")]
    [HttpPatch("{id}/active")]
    public async Task<IActionResult> SetActive(int id, SetBranchActiveCommand command, CancellationToken cancellationToken)
    {
        command.BranchId = id;
        command.RequestedByUserId = CurrentUserId;
        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }
```

- [ ] **Step 7: Add the failing integration test, then watch it pass**

Add to `Tests/GymAppApi.IntegrationTests/BranchesAuthorizationTests.cs`:

```csharp
    [Fact]
    public async Task SetActive_AsGymAdminOfOwnCompany_DeactivatesTheBranch()
    {
        var (companyA, gymAdminAToken, _, _, _) = await SeedAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", gymAdminAToken);
        var createResponse = await client.PostAsJsonAsync("/api/branches", Body(companyA.Id));
        var created = await createResponse.Content.ReadFromJsonAsync<CreateBranchResultDto>();

        var response = await client.PatchAsJsonAsync($"/api/branches/{created!.Id}/active", new { isActive = false });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();
        scope.ServiceProvider.GetRequiredService<GymAppApi.Infrastructure.Tenancy.AmbientTenantContext>().IsSuperAdmin = true;
        Assert.False(db.Branches.Single(b => b.Id == created.Id).IsActive);
    }

    private record CreateBranchResultDto(int Id, string Name);
```

Run: `dotnet test --filter BranchesAuthorizationTests`
Expected: PASS, all of them.

- [ ] **Step 8: Run the full suite**

Run: `dotnet test`
Expected: PASS, all green.

- [ ] **Step 9: Commit**

```bash
git add Core/GymAppApi.Application/Features/Branches/Commands/SetBranchActive/ Presentation/GymAppApi.WebApi/Controllers/BranchesController.cs Tests/GymAppApi.UnitTests/Features/Branches/SetBranchActiveCommandHandlerTests.cs Tests/GymAppApi.IntegrationTests/BranchesAuthorizationTests.cs
git commit -m "Add PATCH /api/branches/{id}/active - GymAdmin can close/reopen their own branch"
```

---

### Task 7: `UpdateBranchCommand` — rename or re-address a branch

**Files:**
- Create: `Core/GymAppApi.Application/Features/Branches/Commands/UpdateBranch/UpdateBranchCommand.cs`
- Create: `Core/GymAppApi.Application/Features/Branches/Commands/UpdateBranch/UpdateBranchCommandValidator.cs`
- Create: `Core/GymAppApi.Application/Features/Branches/Commands/UpdateBranch/UpdateBranchCommandHandler.cs`
- Modify: `Presentation/GymAppApi.WebApi/Controllers/BranchesController.cs`
- Test: `Tests/GymAppApi.UnitTests/Features/Branches/UpdateBranchCommandHandlerTests.cs`

- [ ] **Step 1: Write the command and validator**

```csharp
// UpdateBranchCommand.cs
using MediatR;

namespace GymAppApi.Application.Features.Branches.Commands.UpdateBranch;

public class UpdateBranchCommand : IRequest
{
    // Set by the controller from the route segment, never trusted from the body.
    public int BranchId { get; set; }

    public string Name { get; set; } = null!;
    public string Address { get; set; } = null!;

    // Set by the controller from the caller's own JWT sub claim.
    public int RequestedByUserId { get; set; }
}
```

```csharp
// UpdateBranchCommandValidator.cs
using FluentValidation;

namespace GymAppApi.Application.Features.Branches.Commands.UpdateBranch;

public class UpdateBranchCommandValidator : AbstractValidator<UpdateBranchCommand>
{
    public UpdateBranchCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Address).NotEmpty().MaximumLength(500);
    }
}
```

- [ ] **Step 2: Write the failing unit tests**

```csharp
// Tests/GymAppApi.UnitTests/Features/Branches/UpdateBranchCommandHandlerTests.cs
using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Branches.Commands.UpdateBranch;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;
using Xunit;

namespace GymAppApi.UnitTests.Features.Branches;

public class UpdateBranchCommandHandlerTests
{
    private const int CallerId = 42;

    private static (Mock<IUnitOfWork> uow, Mock<IWriteRepository<Branch>> branchWriteRepo) Wire(
        Branch? branch, IReadOnlyList<Assignment> callerAssignments)
    {
        var branchReadRepo = new Mock<IReadRepository<Branch>>();
        branchReadRepo.Setup(r => r.GetAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Branch, bool>>>(), null, false, default))
            .ReturnsAsync(branch);
        var branchWriteRepo = new Mock<IWriteRepository<Branch>>();

        var assignmentReadRepo = new Mock<IReadRepository<Assignment>>();
        assignmentReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Assignment, bool>>>(), null, null, false, default))
            .ReturnsAsync(callerAssignments);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Branch>()).Returns(branchReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<Branch>()).Returns(branchWriteRepo.Object);
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(assignmentReadRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        return (uow, branchWriteRepo);
    }

    private static Branch ExistingBranch() => new() { Id = 5, CompanyId = 1, Name = "Eski Ad", Address = "Eski Adres", IsActive = true };

    [Fact]
    public async Task Handle_WhenBranchDoesNotExist_ThrowsNotFoundException()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, _) = Wire(branch: null, callerAssignments);
        var handler = new UpdateBranchCommandHandler(uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new UpdateBranchCommand { BranchId = 5, Name = "Yeni Ad", Address = "Yeni Adres", RequestedByUserId = CallerId }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenCallerIsGymAdminOfThisBranchsCompany_UpdatesNameAndAddress()
    {
        var branch = ExistingBranch();
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, branchWriteRepo) = Wire(branch, callerAssignments);
        var handler = new UpdateBranchCommandHandler(uow.Object);

        await handler.Handle(new UpdateBranchCommand { BranchId = 5, Name = "Yeni Ad", Address = "Yeni Adres", RequestedByUserId = CallerId }, CancellationToken.None);

        Assert.Equal("Yeni Ad", branch.Name);
        Assert.Equal("Yeni Adres", branch.Address);
        branchWriteRepo.Verify(r => r.Update(branch), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCallerIsGymAdminOfADifferentCompany_ThrowsForbiddenException()
    {
        var branch = ExistingBranch();
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 999, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, branchWriteRepo) = Wire(branch, callerAssignments);
        var handler = new UpdateBranchCommandHandler(uow.Object);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new UpdateBranchCommand { BranchId = 5, Name = "Yeni Ad", Address = "Yeni Adres", RequestedByUserId = CallerId }, CancellationToken.None));
        branchWriteRepo.Verify(r => r.Update(It.IsAny<Branch>()), Times.Never);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test --filter UpdateBranchCommandHandlerTests`
Expected: FAIL — `UpdateBranchCommandHandler` does not exist yet.

- [ ] **Step 4: Write the handler**

```csharp
// UpdateBranchCommandHandler.cs
using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.Branches.Commands.UpdateBranch;

public class UpdateBranchCommandHandler : IRequestHandler<UpdateBranchCommand>
{
    private readonly IUnitOfWork _unitOfWork;

    public UpdateBranchCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task Handle(UpdateBranchCommand request, CancellationToken cancellationToken)
    {
        var branch = await _unitOfWork.GetReadRepository<Branch>()
            .GetAsync(b => b.Id == request.BranchId, cancellationToken: cancellationToken);
        if (branch is null)
        {
            throw new NotFoundException($"Şube {request.BranchId} bulunamadı.");
        }

        var callerAssignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
            a => a.UserId == request.RequestedByUserId && a.IsActive, cancellationToken: cancellationToken);
        var callerIsAuthorized = callerAssignments.Any(a =>
            a.Role == AssignmentRole.SuperAdmin ||
            (a.Role == AssignmentRole.GymAdmin && a.CompanyId == branch.CompanyId));
        if (!callerIsAuthorized)
        {
            throw new ForbiddenException("Bu şubeyi güncelleme yetkiniz yok.");
        }

        branch.Name = request.Name;
        branch.Address = request.Address;
        _unitOfWork.GetWriteRepository<Branch>().Update(branch);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter UpdateBranchCommandHandlerTests`
Expected: PASS, 3/3.

- [ ] **Step 6: Add the controller action**

In `Presentation/GymAppApi.WebApi/Controllers/BranchesController.cs`, add the import `using GymAppApi.Application.Features.Branches.Commands.UpdateBranch;` and this action:

```csharp
    [Authorize(Policy = "GymAdminOrSuperAdmin")]
    [HttpPatch("{id}")]
    public async Task<IActionResult> Update(int id, UpdateBranchCommand command, CancellationToken cancellationToken)
    {
        command.BranchId = id;
        command.RequestedByUserId = CurrentUserId;
        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }
```

- [ ] **Step 7: Run the full suite**

Run: `dotnet test`
Expected: PASS, all green.

- [ ] **Step 8: Commit**

```bash
git add Core/GymAppApi.Application/Features/Branches/Commands/UpdateBranch/ Presentation/GymAppApi.WebApi/Controllers/BranchesController.cs Tests/GymAppApi.UnitTests/Features/Branches/UpdateBranchCommandHandlerTests.cs
git commit -m "Add PATCH /api/branches/{id} - rename or re-address a branch"
```

---

### Task 8: `GET /api/branches/{id}` — branch detail (supports an edit screen)

**Files:**
- Create: `Core/GymAppApi.Application/Features/Branches/Queries/GetBranchDetail/GetBranchDetailQuery.cs`
- Create: `Core/GymAppApi.Application/Features/Branches/Queries/GetBranchDetail/GetBranchDetailQueryHandler.cs`
- Modify: `Presentation/GymAppApi.WebApi/Controllers/BranchesController.cs`
- Test: `Tests/GymAppApi.UnitTests/Features/Branches/GetBranchDetailQueryHandlerTests.cs`

Reuses `BranchListItemDto` (`Core/GymAppApi.Application/Features/Branches/Queries/GetBranches/BranchListItemDto.cs`, already has `Id/CompanyId/Name/Address/IsActive`) - no new DTO needed, a single branch has no nested collections to add (unlike `CompanyDetailDto`, which nests its `Branches`).

- [ ] **Step 1: Write the query**

```csharp
// GetBranchDetailQuery.cs
using GymAppApi.Application.Features.Branches.Queries.GetBranches;
using MediatR;

namespace GymAppApi.Application.Features.Branches.Queries.GetBranchDetail;

public class GetBranchDetailQuery : IRequest<BranchListItemDto>
{
    public GetBranchDetailQuery(int branchId) => BranchId = branchId;

    public int BranchId { get; }
}
```

- [ ] **Step 2: Write the failing unit test**

```csharp
// Tests/GymAppApi.UnitTests/Features/Branches/GetBranchDetailQueryHandlerTests.cs
using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Branches.Queries.GetBranchDetail;
using GymAppApi.Domain.Entities;
using Moq;
using Xunit;

namespace GymAppApi.UnitTests.Features.Branches;

public class GetBranchDetailQueryHandlerTests
{
    [Fact]
    public async Task Handle_WhenBranchExists_ReturnsItsDetails()
    {
        var branch = new Branch { Id = 5, CompanyId = 1, Name = "Merkez", Address = "Adres", IsActive = true };
        var readRepo = new Mock<IReadRepository<Branch>>();
        readRepo.Setup(r => r.GetAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Branch, bool>>>(), null, false, default))
            .ReturnsAsync(branch);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Branch>()).Returns(readRepo.Object);
        var handler = new GetBranchDetailQueryHandler(uow.Object);

        var result = await handler.Handle(new GetBranchDetailQuery(5), CancellationToken.None);

        Assert.Equal(5, result.Id);
        Assert.Equal("Merkez", result.Name);
    }

    [Fact]
    public async Task Handle_WhenBranchDoesNotExist_ThrowsNotFoundException()
    {
        var readRepo = new Mock<IReadRepository<Branch>>();
        readRepo.Setup(r => r.GetAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Branch, bool>>>(), null, false, default))
            .ReturnsAsync((Branch?)null);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Branch>()).Returns(readRepo.Object);
        var handler = new GetBranchDetailQueryHandler(uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(new GetBranchDetailQuery(5), CancellationToken.None));
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test --filter GetBranchDetailQueryHandlerTests`
Expected: FAIL — `GetBranchDetailQueryHandler` does not exist yet.

- [ ] **Step 4: Write the handler**

```csharp
// GetBranchDetailQueryHandler.cs
using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Branches.Queries.GetBranches;
using GymAppApi.Domain.Entities;
using MediatR;

namespace GymAppApi.Application.Features.Branches.Queries.GetBranchDetail;

public class GetBranchDetailQueryHandler : IRequestHandler<GetBranchDetailQuery, BranchListItemDto>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetBranchDetailQueryHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<BranchListItemDto> Handle(GetBranchDetailQuery request, CancellationToken cancellationToken)
    {
        var branch = await _unitOfWork.GetReadRepository<Branch>()
            .GetAsync(b => b.Id == request.BranchId, cancellationToken: cancellationToken);
        if (branch is null)
        {
            throw new NotFoundException($"Şube {request.BranchId} bulunamadı.");
        }

        return new BranchListItemDto
        {
            Id = branch.Id,
            CompanyId = branch.CompanyId,
            Name = branch.Name,
            Address = branch.Address,
            IsActive = branch.IsActive,
        };
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter GetBranchDetailQueryHandlerTests`
Expected: PASS, 2/2.

- [ ] **Step 6: Add the controller action**

In `Presentation/GymAppApi.WebApi/Controllers/BranchesController.cs`, add the import `using GymAppApi.Application.Features.Branches.Queries.GetBranchDetail;` and this action (plain `[Authorize]`, same as `GetAll` — any authenticated tenant-scoped caller can read their own branch's detail):

```csharp
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetBranchDetailQuery(id), cancellationToken));
```

- [ ] **Step 7: Run the full suite**

Run: `dotnet test`
Expected: PASS, all green.

- [ ] **Step 8: Commit**

```bash
git add Core/GymAppApi.Application/Features/Branches/Queries/GetBranchDetail/ Presentation/GymAppApi.WebApi/Controllers/BranchesController.cs Tests/GymAppApi.UnitTests/Features/Branches/GetBranchDetailQueryHandlerTests.cs
git commit -m "Add GET /api/branches/{id} - branch detail"
```

---

### Task 9: `RemoveAssignmentCommand` — remove a GymAdmin/BranchManager/Trainer

**Files:**
- Create: `Core/GymAppApi.Application/Features/Assignments/Commands/RemoveAssignment/RemoveAssignmentCommand.cs`
- Create: `Core/GymAppApi.Application/Features/Assignments/Commands/RemoveAssignment/RemoveAssignmentCommandHandler.cs`
- Create: `Core/GymAppApi.Application/Features/Assignments/Exceptions/LastGymAdminException.cs`
- Modify: `Presentation/GymAppApi.WebApi/Controllers/AssignmentsController.cs`
- Test: `Tests/GymAppApi.UnitTests/Features/Assignments/RemoveAssignmentCommandHandlerTests.cs`
- Test: `Tests/GymAppApi.IntegrationTests/RemoveAssignmentAuthorizationTests.cs`

This is one generic command for all three removable roles, mirroring how `ConfirmAssignmentInvitationCommand` is already role-agnostic. Removal never goes through the invitation/SMS-confirmation flow from `docs/superpowers/plans/2026-09-17-branch-staff-roles-expansion.md`'s sibling plan — that model exists to protect against being ADDED without consent; revoking someone's own access is an ordinary authorized management action, same as in any real organization, and needs no confirmation from the person being removed. `SuperAdmin`/`Member` assignments are not removable via this command (no case for them - falls through to `ForbiddenException`, `Member` doesn't exist as an addable role yet per Task 2 anyway).

- [ ] **Step 1: Write the exception**

```csharp
// LastGymAdminException.cs
using GymAppApi.Application.Common.Exceptions;

namespace GymAppApi.Application.Features.Assignments.Exceptions;

public class LastGymAdminException : ConflictException
{
    public LastGymAdminException() : base(
        "Bir firmanın en az bir Gym Admin'i olmalı. Son Gym Admin'i kaldırmak için Super Admin gerekir.") { }
}
```

- [ ] **Step 2: Write the command**

```csharp
// RemoveAssignmentCommand.cs
using MediatR;

namespace GymAppApi.Application.Features.Assignments.Commands.RemoveAssignment;

public class RemoveAssignmentCommand : IRequest
{
    // Set by the controller from the route segment, never trusted from the body.
    public int AssignmentId { get; set; }

    // Set by the controller from the caller's own JWT sub claim.
    public int RequestedByUserId { get; set; }
}
```

- [ ] **Step 3: Write the failing unit tests**

```csharp
// Tests/GymAppApi.UnitTests/Features/Assignments/RemoveAssignmentCommandHandlerTests.cs
using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Assignments.Commands.RemoveAssignment;
using GymAppApi.Application.Features.Assignments.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Assignments;

public class RemoveAssignmentCommandHandlerTests
{
    private const int CallerId = 42;

    private static (Mock<IUnitOfWork> uow, Mock<IWriteRepository<Assignment>> assignmentWriteRepo) Wire(
        Assignment? target, IReadOnlyList<Assignment> callerAssignments, bool otherActiveGymAdminExists = true)
    {
        var assignmentReadRepo = new Mock<IReadRepository<Assignment>>();
        assignmentReadRepo.Setup(r => r.GetAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Assignment, bool>>>(), null, false, default))
            .ReturnsAsync(target);
        assignmentReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Assignment, bool>>>(), null, null, false, default))
            .ReturnsAsync(callerAssignments);
        assignmentReadRepo.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Assignment, bool>>>(), default))
            .ReturnsAsync(otherActiveGymAdminExists);
        var assignmentWriteRepo = new Mock<IWriteRepository<Assignment>>();

        var notificationWriteRepo = new Mock<IWriteRepository<Notification>>();
        var deviceTokenReadRepo = new Mock<IReadRepository<DeviceToken>>();
        deviceTokenReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<DeviceToken, bool>>>(), null, null, false, default))
            .ReturnsAsync(new List<DeviceToken>());

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(assignmentReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<Assignment>()).Returns(assignmentWriteRepo.Object);
        uow.Setup(u => u.GetWriteRepository<Notification>()).Returns(notificationWriteRepo.Object);
        uow.Setup(u => u.GetReadRepository<DeviceToken>()).Returns(deviceTokenReadRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        return (uow, assignmentWriteRepo);
    }

    [Fact]
    public async Task Handle_WhenAssignmentDoesNotExist_ThrowsNotFoundException()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, _) = Wire(target: null, callerAssignments);
        var handler = new RemoveAssignmentCommandHandler(uow.Object, Mock.Of<IPushNotificationSender>());

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new RemoveAssignmentCommand { AssignmentId = 1, RequestedByUserId = CallerId }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenRemovingATrainerAsTheirBranchManager_Succeeds()
    {
        var target = new Assignment { Id = 1, UserId = 7, CompanyId = 1, BranchId = 10, Role = AssignmentRole.Trainer, IsActive = true };
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, BranchId = 10, Role = AssignmentRole.BranchManager, IsActive = true } };
        var (uow, assignmentWriteRepo) = Wire(target, callerAssignments);
        var handler = new RemoveAssignmentCommandHandler(uow.Object, Mock.Of<IPushNotificationSender>());

        await handler.Handle(new RemoveAssignmentCommand { AssignmentId = 1, RequestedByUserId = CallerId }, CancellationToken.None);

        Assert.False(target.IsActive);
        assignmentWriteRepo.Verify(r => r.Update(target), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenRemovingATrainerAsAnUnrelatedBranchManager_ThrowsForbiddenException()
    {
        var target = new Assignment { Id = 1, UserId = 7, CompanyId = 1, BranchId = 10, Role = AssignmentRole.Trainer, IsActive = true };
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, BranchId = 999, Role = AssignmentRole.BranchManager, IsActive = true } };
        var (uow, assignmentWriteRepo) = Wire(target, callerAssignments);
        var handler = new RemoveAssignmentCommandHandler(uow.Object, Mock.Of<IPushNotificationSender>());

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new RemoveAssignmentCommand { AssignmentId = 1, RequestedByUserId = CallerId }, CancellationToken.None));
        assignmentWriteRepo.Verify(r => r.Update(It.IsAny<Assignment>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenRemovingABranchManagerAsAPeerBranchManager_ThrowsForbiddenException()
    {
        var target = new Assignment { Id = 1, UserId = 7, CompanyId = 1, BranchId = 10, Role = AssignmentRole.BranchManager, IsActive = true };
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, BranchId = 10, Role = AssignmentRole.BranchManager, IsActive = true } };
        var (uow, assignmentWriteRepo) = Wire(target, callerAssignments);
        var handler = new RemoveAssignmentCommandHandler(uow.Object, Mock.Of<IPushNotificationSender>());

        // Only GymAdmin/SuperAdmin may remove a BranchManager - same
        // principle as who may assign one (Task 3).
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new RemoveAssignmentCommand { AssignmentId = 1, RequestedByUserId = CallerId }, CancellationToken.None));
        assignmentWriteRepo.Verify(r => r.Update(It.IsAny<Assignment>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenRemovingAPeerGymAdminAsAnotherGymAdminOfTheSameCompany_Succeeds()
    {
        var target = new Assignment { Id = 1, UserId = 7, CompanyId = 1, BranchId = null, Role = AssignmentRole.GymAdmin, IsActive = true };
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, assignmentWriteRepo) = Wire(target, callerAssignments, otherActiveGymAdminExists: true);
        var handler = new RemoveAssignmentCommandHandler(uow.Object, Mock.Of<IPushNotificationSender>());

        await handler.Handle(new RemoveAssignmentCommand { AssignmentId = 1, RequestedByUserId = CallerId }, CancellationToken.None);

        Assert.False(target.IsActive);
    }

    [Fact]
    public async Task Handle_WhenRemovingTheLastGymAdminAsAnotherGymAdmin_ThrowsLastGymAdminException()
    {
        var target = new Assignment { Id = 1, UserId = 7, CompanyId = 1, BranchId = null, Role = AssignmentRole.GymAdmin, IsActive = true };
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, assignmentWriteRepo) = Wire(target, callerAssignments, otherActiveGymAdminExists: false);
        var handler = new RemoveAssignmentCommandHandler(uow.Object, Mock.Of<IPushNotificationSender>());

        await Assert.ThrowsAsync<LastGymAdminException>(() =>
            handler.Handle(new RemoveAssignmentCommand { AssignmentId = 1, RequestedByUserId = CallerId }, CancellationToken.None));
        assignmentWriteRepo.Verify(r => r.Update(It.IsAny<Assignment>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenSuperAdminRemovesTheLastGymAdmin_Succeeds()
    {
        var target = new Assignment { Id = 1, UserId = 7, CompanyId = 1, BranchId = null, Role = AssignmentRole.GymAdmin, IsActive = true };
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = null, Role = AssignmentRole.SuperAdmin, IsActive = true } };
        var (uow, assignmentWriteRepo) = Wire(target, callerAssignments, otherActiveGymAdminExists: false);
        var handler = new RemoveAssignmentCommandHandler(uow.Object, Mock.Of<IPushNotificationSender>());

        await handler.Handle(new RemoveAssignmentCommand { AssignmentId = 1, RequestedByUserId = CallerId }, CancellationToken.None);

        Assert.False(target.IsActive);
        assignmentWriteRepo.Verify(r => r.Update(target), Times.Once);
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test --filter RemoveAssignmentCommandHandlerTests`
Expected: FAIL — `RemoveAssignmentCommandHandler` does not exist yet.

- [ ] **Step 5: Write the handler**

```csharp
// RemoveAssignmentCommandHandler.cs
using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.Notifications;
using GymAppApi.Application.Features.Assignments.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.Assignments.Commands.RemoveAssignment;

public class RemoveAssignmentCommandHandler : IRequestHandler<RemoveAssignmentCommand>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPushNotificationSender _pushNotificationSender;

    public RemoveAssignmentCommandHandler(IUnitOfWork unitOfWork, IPushNotificationSender pushNotificationSender)
    {
        _unitOfWork = unitOfWork;
        _pushNotificationSender = pushNotificationSender;
    }

    public async Task Handle(RemoveAssignmentCommand request, CancellationToken cancellationToken)
    {
        var assignmentReadRepo = _unitOfWork.GetReadRepository<Assignment>();
        var assignment = await assignmentReadRepo.GetAsync(
            a => a.Id == request.AssignmentId && a.IsActive, cancellationToken: cancellationToken);
        if (assignment is null)
        {
            throw new NotFoundException($"Atama {request.AssignmentId} bulunamadı.");
        }

        var callerAssignments = await assignmentReadRepo.GetAllAsync(
            a => a.UserId == request.RequestedByUserId && a.IsActive, cancellationToken: cancellationToken);

        // Mirrors exactly who may ADD each role (Tasks 2-3, 5): a
        // BranchManager may remove a Trainer from their own branch, but
        // never a peer BranchManager or a GymAdmin; only GymAdmin(of this
        // company)/SuperAdmin may remove a BranchManager or a peer GymAdmin.
        var callerIsAuthorized = assignment.Role switch
        {
            AssignmentRole.GymAdmin or AssignmentRole.BranchManager => callerAssignments.Any(a =>
                a.Role == AssignmentRole.SuperAdmin ||
                (a.Role == AssignmentRole.GymAdmin && a.CompanyId == assignment.CompanyId)),
            AssignmentRole.Trainer => callerAssignments.Any(a =>
                a.Role == AssignmentRole.SuperAdmin ||
                (a.Role == AssignmentRole.GymAdmin && a.CompanyId == assignment.CompanyId) ||
                (a.Role == AssignmentRole.BranchManager && a.BranchId == assignment.BranchId)),
            _ => false,
        };
        if (!callerIsAuthorized)
        {
            throw new ForbiddenException("Bu atamayı kaldırma yetkiniz yok.");
        }

        // A company must always keep at least one active GymAdmin - unless
        // SuperAdmin is the one removing it (the explicit platform-level
        // override the user asked for, e.g. to force a replacement later).
        if (assignment.Role == AssignmentRole.GymAdmin)
        {
            var callerIsSuperAdmin = callerAssignments.Any(a => a.Role == AssignmentRole.SuperAdmin);
            if (!callerIsSuperAdmin)
            {
                var otherActiveGymAdminExists = await assignmentReadRepo.AnyAsync(
                    a => a.CompanyId == assignment.CompanyId && a.BranchId == null &&
                         a.Role == AssignmentRole.GymAdmin && a.IsActive && a.Id != assignment.Id, cancellationToken);
                if (!otherActiveGymAdminExists)
                {
                    throw new LastGymAdminException();
                }
            }
        }

        assignment.IsActive = false;
        _unitOfWork.GetWriteRepository<Assignment>().Update(assignment);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await NotificationDispatcher.NotifyUserAsync(
            _unitOfWork, _pushNotificationSender, assignment.UserId,
            "Atama kaldırıldı",
            $"GymApp'teki {assignment.Role} atamanız kaldırıldı.",
            cancellationToken);
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test --filter RemoveAssignmentCommandHandlerTests`
Expected: PASS, 7/7.

- [ ] **Step 7: Add the controller action**

In `Presentation/GymAppApi.WebApi/Controllers/AssignmentsController.cs`, add the import `using GymAppApi.Application.Features.Assignments.Commands.RemoveAssignment;` and this action:

```csharp
    // Any authenticated user can call this - authorization is fully
    // role-dependent (see RemoveAssignmentCommandHandler) and can't be
    // expressed as one static policy the way Create/AddStaffMember can.
    [Authorize]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Remove(int id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new RemoveAssignmentCommand { AssignmentId = id, RequestedByUserId = CurrentUserId }, cancellationToken);
        return NoContent();
    }
```

- [ ] **Step 8: Write the failing integration tests**

```csharp
// Tests/GymAppApi.IntegrationTests/RemoveAssignmentAuthorizationTests.cs
using System.Net;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.Infrastructure.Tenancy;
using GymAppApi.Persistence.Context;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GymAppApi.IntegrationTests;

public class RemoveAssignmentAuthorizationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public RemoveAssignmentAuthorizationTests(CustomWebApplicationFactory factory) => _factory = factory;

    private static string UniquePhone() => $"+9055504{Random.Shared.Next(10000, 99999)}";

    [Fact]
    public async Task Remove_WithoutToken_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.DeleteAsync("/api/assignments/1");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Remove_LastGymAdminAsAnotherGymAdmin_Returns409()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();
        var company = new Company { Name = "Test Co", IsActive = true };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var onlyGymAdmin = new User { FullName = "Only Admin", Phone = UniquePhone(), PasswordHash = "x" };
        db.Users.Add(onlyGymAdmin);
        await db.SaveChangesAsync();
        var onlyGymAdminAssignment = new Assignment { UserId = onlyGymAdmin.Id, CompanyId = company.Id, Role = AssignmentRole.GymAdmin, IsActive = true };
        db.Assignments.Add(onlyGymAdminAssignment);
        await db.SaveChangesAsync();

        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var token = jwtService.GenerateAccessToken(new AccessTokenClaims(onlyGymAdmin.Id, onlyGymAdmin.FullName, onlyGymAdmin.Email, onlyGymAdmin.Phone)).Token;

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var response = await client.DeleteAsync($"/api/assignments/{onlyGymAdminAssignment.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Remove_SecondGymAdminAsFirstGymAdmin_Returns204()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();
        var company = new Company { Name = "Test Co", IsActive = true };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var firstAdmin = new User { FullName = "First Admin", Phone = UniquePhone(), PasswordHash = "x" };
        var secondAdmin = new User { FullName = "Second Admin", Phone = UniquePhone(), PasswordHash = "x" };
        db.Users.AddRange(firstAdmin, secondAdmin);
        await db.SaveChangesAsync();
        db.Assignments.Add(new Assignment { UserId = firstAdmin.Id, CompanyId = company.Id, Role = AssignmentRole.GymAdmin, IsActive = true });
        var secondAssignment = new Assignment { UserId = secondAdmin.Id, CompanyId = company.Id, Role = AssignmentRole.GymAdmin, IsActive = true };
        db.Assignments.Add(secondAssignment);
        await db.SaveChangesAsync();

        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var firstAdminToken = jwtService.GenerateAccessToken(new AccessTokenClaims(firstAdmin.Id, firstAdmin.FullName, firstAdmin.Email, firstAdmin.Phone)).Token;

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", firstAdminToken);
        var response = await client.DeleteAsync($"/api/assignments/{secondAssignment.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        scope.ServiceProvider.GetRequiredService<AmbientTenantContext>().IsSuperAdmin = true;
        Assert.False(db.Assignments.Single(a => a.Id == secondAssignment.Id).IsActive);
    }
}
```

- [ ] **Step 9: Run tests to verify they pass**

Run: `dotnet test --filter RemoveAssignmentAuthorizationTests`
Expected: PASS, 3/3.

- [ ] **Step 10: Run the full suite**

Run: `dotnet test`
Expected: PASS, all green.

- [ ] **Step 11: Commit**

```bash
git add Core/GymAppApi.Application/Features/Assignments/Commands/RemoveAssignment/ Core/GymAppApi.Application/Features/Assignments/Exceptions/LastGymAdminException.cs Presentation/GymAppApi.WebApi/Controllers/AssignmentsController.cs Tests/GymAppApi.UnitTests/Features/Assignments/RemoveAssignmentCommandHandlerTests.cs Tests/GymAppApi.IntegrationTests/RemoveAssignmentAuthorizationTests.cs
git commit -m "Add DELETE /api/assignments/{id} - remove GymAdmin/BranchManager/Trainer, protect the last GymAdmin"
```

---

### Task 10: Update memory

**Files:**
- Modify: `.claude/memory/project-branch-ownership-flow.md`
- Modify: `.claude/memory/MEMORY.md`

- [ ] **Step 1: Update `project-branch-ownership-flow.md`**

Replace its "Immediate follow-up requested by the user, not yet built" section with a note that this plan (`docs/superpowers/plans/2026-09-17-branch-staff-roles-expansion.md`) was executed and is done, briefly listing what landed: the (CompanyId, BranchId, Role) dedup fix, `BranchManager` assignable via `AddStaffMember` (GymAdmin/SuperAdmin only), `Member` removed from `AddStaffMember` until Package/Membership exists, `InviteGymAdminCommand` / `POST /api/assignments/gym-admin`, branch close/reopen (`PATCH /api/branches/{id}/active` — note the reactivation-visibility caveat from "Key facts"), branch rename/re-address (`PATCH /api/branches/{id}`), branch detail (`GET /api/branches/{id}`), and `DELETE /api/assignments/{id}` with the last-GymAdmin protection.

- [ ] **Step 2: Update `MEMORY.md`**

Update the "Branch ownership flow" line to say the follow-up plan is done rather than pending.

- [ ] **Step 3: Commit**

```bash
git add .claude/memory/project-branch-ownership-flow.md .claude/memory/MEMORY.md
git commit -m "Update memory: branch/staff roles expansion plan complete"
```

---

## Self-review notes (from writing this plan)

- **Spec coverage:** "gym sahibi admini yetkiyi aldıktan sonra kendi gym'i için şube oluşturabilir" → already done in the prior session (commit `7e9c91e`), not part of this plan. "isterse o şubelere şubeadmin atayabilir" → Tasks 2–3. "isterse kendine başka gymadmin de atayabilir" → Tasks 4–5. "şubelere antrenör atayabilir" → already existed. "bir antrenör bir sürü gym de atanabilir / bir sürü farklı gymde çalışıyor olabilir" → already worked (no per-company-only restriction ever existed across DIFFERENT companies; verified by Task 1's test only checking the same-company case, which was the actual bug). "Gym'in farklı şubelerinde çalışıyor olabilir" → Task 1. "Üye ekleme işlemini paket atayana kadar yapmayacağız" → Task 2 (removes `Member`). "Şube de oluşturabilmeli şube de kapatabilmeli" → create already done, close/reopen → Task 6. "herşeyi düşün gerçek hayatta olabilecek tüm senaryolar" (re: branches) → Tasks 6–8 add close/reopen, rename/re-address, and detail-by-id; hard delete was deliberately NOT added (no entity in this codebase is ever hard-deleted, all soft-delete via `IsActive`) and cascading a branch's deactivation onto its staff's `Assignment` rows was deliberately NOT added either (matches `Company`'s own deactivation, which doesn't cascade to its `Branch`es). "anagymadmin harici diğer gymadminler şubeden çıkarılabilsin" / "ya da superadmin isterse ileride gymadmini değiştirebilsin" / "şubeadmin de eklenebilsin çıkarılabilsin" / "traning de aynı şekilde" → Task 9 (`RemoveAssignmentCommand`, one generic command for all three roles, with the last-GymAdmin invariant and the SuperAdmin override in one).
- **Out of scope, deliberately:** the Package/Membership module itself (`Package`, `PackageAssignment`, `MembershipFreeze`) — the user explicitly said member-adding waits until that exists; building it is its own plan, not bolted onto this one. `CreateAssignmentCommand` (`POST /api/assignments`, the older direct-by-UserId endpoint) is untouched — still a known, separately-tracked gap (`.claude/memory/project-assignment-invitation-security.md`). Self-service branch reactivation after deactivation (would need a `.IgnoreQueryFilters()`-style bypass, see "Key facts") — flagged, not built, only SuperAdmin can reactivate for now. A "replace GymAdmin" single-step command — composed instead from existing `RemoveAssignmentCommand` (SuperAdmin override) + `InviteGymAdminCommand`, no new endpoint needed.
- **Type consistency check:** `InviteGymAdminCommand`/`InviteGymAdminCommandResult`/`InviteGymAdminCommandHandler` names match across Tasks 4–5. `SetBranchActiveCommand`/`UpdateBranchCommand`/`GetBranchDetailQuery`/`RemoveAssignmentCommand` names match between their own definition steps and controller-wiring steps within Tasks 6–9. `AssignmentInvitationService.IssueAsync` and `NotificationDispatcher.NotifyUserAsync` signatures used in Tasks 4 and 9 match their actual current definitions (verified by reading the source files before writing this plan, quoted in "Key facts"). `ConflictException(string message)`'s public constructor (used by the new `LastGymAdminException`) matches its actual current definition (also verified and quoted in "Key facts").
