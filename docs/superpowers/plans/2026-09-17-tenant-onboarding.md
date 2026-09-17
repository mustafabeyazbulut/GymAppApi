# Tenant Onboarding (Backend) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Company/Branch tenant isolation actually work (it is currently a no-op stub — every request sees every company's data), then add the two endpoints the product design's "Tenant Onboarding ve Bootstrap" and "Kullanıcı/Assignment oluşturma" sections describe: a Super-Admin-only "create a new Company + Branch + Gym Admin" endpoint, and a staff (Gym Admin / Branch Manager) "add a Member or Trainer" endpoint that creates a brand-new `User` when the phone doesn't exist yet — something no endpoint does today. Finish by retiring the open self-service registration endpoints, since new accounts are now staff-created.

**Architecture:** One half of a two-repo feature. The mobile half is planned separately at `C:\Users\MBEYAZBULUT\Documents\GitHub\GymApp\docs\superpowers\plans\2026-09-17-tenant-onboarding.md`. That plan's "Create Company" and "Add Staff Member" screens are hard-blocked on this plan's Tasks 7–10 (the two new endpoints); its "remove Register screen" task is hard-blocked on this plan's Task 11 only being done *after* the mobile side has already stopped calling `/api/auth/register/*`. Every new endpoint mirrors the existing `CreateAssignmentCommand` vertical slice's CQRS/MediatR/FluentValidation/repository/exception shape exactly (see `Core/GymAppApi.Application/Features/Assignments/Commands/CreateAssignment/`).

**Tech Stack:** ASP.NET Core 10 / .NET 10, MediatR 14, FluentValidation, EF Core 10 + Npgsql, xUnit + Moq (unit), xUnit + EF Core InMemory + `WebApplicationFactory<Program>` (integration) — all already in use, no new packages.

---

## Key facts (read before starting — do not re-derive these, they were verified by reading the actual current code)

- **Tenant isolation is currently completely broken, not hypothetical.** `AmbientTenantContext` (`Infrastructure/GymAppApi.Infrastructure/Tenancy/AmbientTenantContext.cs`) unconditionally returns `IsSuperAdmin => true`, which short-circuits every global query filter in `GymAppApiDbContext.ApplyGlobalQueryFilters` (`e.g. _tenantContext.IsSuperAdmin || (...)`) — **every authenticated request currently sees every company's and every branch's data.** This was a deliberate, explicitly-documented deferral in the Real Auth plan ("JWT carries no role/tenant claims... this plan deliberately does NOT [wire real tenant context]"). It stops being harmless the moment a second Company exists, which is this plan's whole point — Task 1–4 fix it first, before anything else.
- **The JWT carries zero role/company/branch claims, by design**, and this plan does not change that (`Infrastructure/GymAppApi.Infrastructure/Security/JwtTokenService.cs` issues only `sub`, `name`, `phone`, optional `email`). All role/tenant resolution happens by re-querying `Assignment` per request — both for authorization (`AssignmentRoleAuthorizationHandler`, already exists) and now for query-filter scoping (`ITenantResolutionService`, new in this plan). This avoids stale-claim bugs (a revoked role stays revoked immediately, not just after the access token expires).
- **`AmbientTenantContext` is registered `AddScoped<ITenantContext, AmbientTenantContext>()`** already (`Infrastructure/GymAppApi.Infrastructure/Registration.cs:16`) — i.e. already one-instance-per-HTTP-request. This plan does not change its lifetime, only makes its 3 properties settable and populates them from a new middleware instead of hardcoding them.
- **Why the resolution can't just reuse `IUnitOfWork`/`IReadRepository<Assignment>` directly:** `GymAppApiDbContext`'s constructor takes `ITenantContext` (confirmed: `GymAppApiDbContext(DbContextOptions<GymAppApiDbContext> options, ITenantContext tenantContext)`), and every `Assignment` query is itself tenant-filtered (`Assignment : ITenantScoped`). Querying "what are this user's own assignments" *through the normal filtered path, before the tenant context is known* is circular — with the context still at its unresolved defaults (`IsSuperAdmin=false, CompanyId=null`), the filter (`SetNullableTenantFilter`) collapses to "only rows where `CompanyId` is literally null", silently hiding every GymAdmin/Member/Trainer/BranchManager assignment (which always have `CompanyId` set) during the very lookup meant to discover it. Task 1's `ITenantResolutionService` sidesteps this with one explicit `.IgnoreQueryFilters()` call — the only place in the codebase that should ever do that, and it's exactly the same principle as `AssignmentRoleAuthorizationHandler`'s existing per-request re-query, just for a different consumer.
- **Repository interfaces, exact signatures** (`Core/GymAppApi.Application/Common/Interfaces/`):
  - `IUnitOfWork`: `GetReadRepository<T>()`, `GetWriteRepository<T>()`, `SaveChangesAsync(ct)`, `BeginTransactionAsync(ct)` → `IAsyncDisposable`, `CommitTransactionAsync(ct)`, `RollbackTransactionAsync(ct)`.
  - `IReadRepository<T>`: `GetAsync(predicate, include?, enableTracking=false, ct)`, `GetAllAsync(predicate?, include?, orderBy?, enableTracking=false, ct)`, `AnyAsync(predicate, ct)`, `CountAsync(predicate?, ct)`.
  - `IWriteRepository<T>`: `AddAsync(entity, ct)`, `Update(entity)`, `Remove(entity)`.
- **Domain entities already exist, no new entities needed:** `User` (`Phone` non-null, `Email?`, `PasswordHash`, `FullName`, `PhoneVerified`, `IsAccountFrozen`), `Assignment : ITenantScoped` (`UserId`, `CompanyId?`, `BranchId?`, `Role`, `IsActive`), `Company : IDeactivatable` (`Name`, `IsActive`, `Branches`), `Branch : ICompanyScoped, IDeactivatable` (`CompanyId`, `Name`, `Address`, `IsActive`). `AssignmentRole` enum, exact order: `SuperAdmin, GymAdmin, BranchManager, Trainer, Member`.
- **`IPasswordHasher.Hash(string)`** (`Core/GymAppApi.Application/Common/Interfaces/IPasswordHasher.cs`) is used to generate an unguessable placeholder password hash for a staff-created `User` — `_passwordHasher.Hash(Guid.NewGuid().ToString())`. That person sets their real password later via the **already fully-built, unmodified** `POST /api/auth/forgot-password` → `POST /api/auth/reset-password` flow, which looks a user up purely by phone/email with no precondition on `PhoneVerified` or how `PasswordHash` was originally set (confirmed by reading `ForgotPasswordCommandHandler`/`ResetPasswordCommandHandler` in full) — no new "first login" endpoint is needed anywhere in this plan.
- **`ISmsSender.SendAsync(phone, message, ct)`** (already used the same way in `FreezeAccountRequestOtpCommandHandler`) is reused to notify a newly-created Gym Admin / Member / Trainer that their account exists and how to set a password.
- **Authorization pattern to copy exactly** (from `CreateAssignmentCommandHandler` — the direct template for Tasks 7 and 9): the controller sets `command.RequestedByUserId = CurrentUserId` from the JWT `sub` claim (**never** trust a body-supplied caller id). The `[Authorize(Policy = ...)]` attribute only proves "the caller holds *some* allowed role somewhere" — the handler *always* does a second, explicit re-query of the caller's own active `Assignment`s and checks they're scoped to *this specific* company/branch before proceeding, throwing `ForbiddenException` (403) otherwise.
- **Common exceptions** (`Core/GymAppApi.Application/Common/Exceptions/`, all `BaseException` subclasses with `StatusCode`): `NotFoundException`(404), `ConflictException`(409), `ForbiddenException`(403), `UnauthorizedException`(401).
- **`BranchesController` currently has no `[Authorize]` at all** — it's fully anonymous today. Task 6 adds `[Authorize]`; combined with Task 1–4's fix, `GET /api/branches` then naturally returns only the caller's own company's branches (needed by the mobile "Add Staff Member" screen's branch picker) with zero new backend code beyond the attribute.
- **A first Super Admin already exists** — no bootstrap script needed. `Infrastructure/GymAppApi.Persistence/Configurations/SuperAdminSeedConfiguration.cs` / `SuperAdminAssignmentSeedConfiguration.cs` (migration `20260915121926_SeedSuperAdmin`) seed `User.Id=-1`, `Phone="+900000000000"`, plus an `Assignment.Id=-1` with `Role=SuperAdmin`, `CompanyId=null`. The seeded password is a placeholder documented once in `docs/superpowers/plans/2026-09-15-real-auth.md` Task 12 — use it to log in as this user for manual verification.
- **Test conventions to copy exactly:** unit tests live in `Tests/GymAppApi.UnitTests/Features/<Feature>/`, namespace `GymAppApi.UnitTests.Features.<Feature>`, a private static `Wire(...)` helper builds mocked `Mock<IUnitOfWork>` + repo mocks and returns them as a tuple (see `CreateAssignmentCommandHandlerTests.cs` — copy its exact shape). Integration tests live in `Tests/GymAppApi.IntegrationTests/`, use `IClassFixture<CustomWebApplicationFactory>`, seed data via a raw `GymAppApiDbContext` pulled from `_factory.Services.CreateScope()`, and mint real JWTs via `IJwtTokenService.GenerateAccessToken(new AccessTokenClaims(userId, fullName, email, phone))` (see `AssignmentsAuthorizationTests.cs` — copy its exact shape).

---

### Task 1: `ITenantResolutionService` — the Application-layer contract

**Files:**
- Create: `Core/GymAppApi.Application/Common/Interfaces/ITenantResolutionService.cs`

- [ ] **Step 1: Write the interface and its result record**

```csharp
namespace GymAppApi.Application.Common.Interfaces;

// Resolves what a request's ITenantContext should be, from the caller's own
// Assignment rows. Deliberately its own interface (not folded into
// ITenantContext itself) because the implementation needs to bypass the
// global query filter for one lookup (see this plan's "Key facts" section) -
// keeping that concern out of ITenantContext keeps that interface a plain,
// dependency-free settable bag of 3 properties.
public interface ITenantResolutionService
{
    Task<ResolvedTenant> ResolveForUserAsync(int userId, CancellationToken cancellationToken = default);
}

public record ResolvedTenant(bool IsSuperAdmin, int? CompanyId, int? BranchId);
```

- [ ] **Step 2: Build to confirm it compiles**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Core/GymAppApi.Application/Common/Interfaces/ITenantResolutionService.cs
git commit -m "Add ITenantResolutionService contract"
```

---

### Task 2: `TenantResolutionService` — the Persistence-layer implementation

**Files:**
- Create: `Infrastructure/GymAppApi.Persistence/Tenancy/TenantResolutionService.cs`
- Modify: `Infrastructure/GymAppApi.Persistence/Registration.cs`
- Test: `Tests/GymAppApi.UnitTests/Tenancy/TenantResolutionServiceTests.cs`

This needs `GymAppApiDbContext` directly (not `IUnitOfWork`) specifically to call `.IgnoreQueryFilters()`, which `IReadRepository<T>` does not expose. `TenantResolutionServiceTests` therefore uses the EF Core InMemory provider directly (like `TenantQueryFilterTests.cs` does), not Moq — there's no interface boundary to mock here, the thing under test *is* the EF Core query.

- [ ] **Step 1: Write the failing tests**

```csharp
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.Persistence.Context;
using GymAppApi.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GymAppApi.UnitTests.Tenancy;

public class TenantResolutionServiceTests
{
    // Bypasses the filter itself (IsSuperAdmin = true) purely to seed data -
    // this is not the code under test, see AmbientTenantContext's own tests
    // for that.
    private class SeedOnlyTenantContext : Application.Common.Interfaces.ITenantContext
    {
        public int? CompanyId => null;
        public int? BranchId => null;
        public bool IsSuperAdmin => true;
    }

    private static GymAppApiDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<GymAppApiDbContext>().UseInMemoryDatabase(dbName).Options;
        return new GymAppApiDbContext(options, new SeedOnlyTenantContext());
    }

    [Fact]
    public async Task ResolveForUserAsync_WhenUserHasNoAssignments_ReturnsFailClosedDefaults()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var context = CreateContext(dbName);
        var user = new User { FullName = "No Assignment", Phone = "+905550000010", PasswordHash = "x" };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var service = new TenantResolutionService(context);
        var resolved = await service.ResolveForUserAsync(user.Id);

        Assert.False(resolved.IsSuperAdmin);
        Assert.Null(resolved.CompanyId);
        Assert.Null(resolved.BranchId);
    }

    [Fact]
    public async Task ResolveForUserAsync_WhenUserIsSuperAdmin_ReturnsSuperAdminRegardlessOfOtherAssignments()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var context = CreateContext(dbName);
        var user = new User { FullName = "Super Admin", Phone = "+905550000011", PasswordHash = "x" };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        context.Assignments.Add(new Assignment { UserId = user.Id, CompanyId = null, Role = AssignmentRole.SuperAdmin, IsActive = true });
        await context.SaveChangesAsync();

        var service = new TenantResolutionService(context);
        var resolved = await service.ResolveForUserAsync(user.Id);

        Assert.True(resolved.IsSuperAdmin);
        Assert.Null(resolved.CompanyId);
    }

    [Fact]
    public async Task ResolveForUserAsync_WhenUserHasOneCompanyAssignment_ReturnsThatCompanyAndBranch()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var context = CreateContext(dbName);
        var user = new User { FullName = "Gym Admin", Phone = "+905550000012", PasswordHash = "x" };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        context.Assignments.Add(new Assignment { UserId = user.Id, CompanyId = 7, BranchId = 3, Role = AssignmentRole.BranchManager, IsActive = true });
        await context.SaveChangesAsync();

        var service = new TenantResolutionService(context);
        var resolved = await service.ResolveForUserAsync(user.Id);

        Assert.False(resolved.IsSuperAdmin);
        Assert.Equal(7, resolved.CompanyId);
        Assert.Equal(3, resolved.BranchId);
    }

    [Fact]
    public async Task ResolveForUserAsync_IgnoresInactiveAssignments()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var context = CreateContext(dbName);
        var user = new User { FullName = "Removed Staff", Phone = "+905550000013", PasswordHash = "x" };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        context.Assignments.Add(new Assignment { UserId = user.Id, CompanyId = 7, Role = AssignmentRole.GymAdmin, IsActive = false });
        await context.SaveChangesAsync();

        var service = new TenantResolutionService(context);
        var resolved = await service.ResolveForUserAsync(user.Id);

        Assert.False(resolved.IsSuperAdmin);
        Assert.Null(resolved.CompanyId);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter TenantResolutionServiceTests`
Expected: FAIL — `TenantResolutionService` does not exist yet (compile error).

- [ ] **Step 3: Write the implementation**

```csharp
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Enums;
using GymAppApi.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Persistence.Tenancy;

public class TenantResolutionService : ITenantResolutionService
{
    private readonly GymAppApiDbContext _dbContext;

    public TenantResolutionService(GymAppApiDbContext dbContext) => _dbContext = dbContext;

    public async Task<ResolvedTenant> ResolveForUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        // The only place in the codebase allowed to bypass the tenant query
        // filter — see this plan's "Key facts" for why (resolving a user's
        // OWN assignments must not itself already be tenant-filtered).
        var assignments = await _dbContext.Assignments
            .IgnoreQueryFilters()
            .Where(a => a.UserId == userId && a.IsActive)
            .ToListAsync(cancellationToken);

        if (assignments.Count == 0)
        {
            return new ResolvedTenant(false, null, null);
        }
        if (assignments.Any(a => a.Role == AssignmentRole.SuperAdmin))
        {
            return new ResolvedTenant(true, null, null);
        }

        // A user with more than one non-SuperAdmin Assignment (multi-company
        // staff/members) picks their FIRST one deterministically for now — a
        // proper "şirket/şube seç" context switcher (see the product design
        // doc's "Çoklu Şirkete Bağlılık ve Giriş Akışı") is future work, out
        // of scope here. Practically rare today since Tenant Onboarding is
        // what starts letting a user hold more than one Assignment at all.
        var primary = assignments.OrderBy(a => a.Id).First();
        return new ResolvedTenant(false, primary.CompanyId, primary.BranchId);
    }
}
```

- [ ] **Step 4: Register it**

In `Infrastructure/GymAppApi.Persistence/Registration.cs`, add the import and one line:

```csharp
using GymAppApi.Persistence.Tenancy;
```

```csharp
        services.AddScoped(typeof(IReadRepository<>), typeof(ReadRepository<>));
        services.AddScoped(typeof(IWriteRepository<>), typeof(WriteRepository<>));
        services.AddScoped<IUnitOfWork, GymAppApi.Persistence.UnitOfWork.UnitOfWork>();
        services.AddScoped<ITenantResolutionService, TenantResolutionService>();
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter TenantResolutionServiceTests`
Expected: PASS, 4/4.

- [ ] **Step 6: Commit**

```bash
git add Infrastructure/GymAppApi.Persistence/Tenancy/TenantResolutionService.cs Infrastructure/GymAppApi.Persistence/Registration.cs Tests/GymAppApi.UnitTests/Tenancy/TenantResolutionServiceTests.cs
git commit -m "Add TenantResolutionService, resolving a caller's tenant scope from their own Assignments"
```

---

### Task 3: Make `AmbientTenantContext` a settable, dependency-free holder

**Files:**
- Modify: `Infrastructure/GymAppApi.Infrastructure/Tenancy/AmbientTenantContext.cs`
- Modify: `Infrastructure/GymAppApi.Infrastructure/Registration.cs`

- [ ] **Step 1: Replace the stub**

```csharp
using GymAppApi.Application.Common.Interfaces;

namespace GymAppApi.Infrastructure.Tenancy;

// Populated once per request by TenantContextMiddleware (Presentation layer)
// right after authentication, from the caller's own Assignment rows (via
// ITenantResolutionService). A brand-new request that hasn't gone through
// that middleware yet (or an unauthenticated one) keeps these fail-closed
// defaults - IsSuperAdmin=false, CompanyId=null - which the existing global
// query filters already treat as "see nothing" for every tenant-scoped
// entity except a null-CompanyId row.
public class AmbientTenantContext : ITenantContext
{
    public int? CompanyId { get; set; }
    public int? BranchId { get; set; }
    public bool IsSuperAdmin { get; set; }
}
```

- [ ] **Step 2: Fix its DI registration so both the interface and the concrete type resolve to the SAME per-request instance**

`TenantContextMiddleware` (Task 4) needs to *write* to this service, so it must be injectable by its concrete type too, not only by `ITenantContext`. In `Infrastructure/GymAppApi.Infrastructure/Registration.cs`, replace:

```csharp
        services.AddScoped<ITenantContext, AmbientTenantContext>();
```

with:

```csharp
        services.AddScoped<AmbientTenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<AmbientTenantContext>());
```

- [ ] **Step 3: Build to confirm it compiles**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Run the full existing test suite to check for regressions**

Run: `dotnet test`
Expected: PASS, same count as before this task (no behavior changed yet — `AmbientTenantContext`'s properties still all default to `false`/`null`, same effective values as the old hardcoded stub returned... **except `IsSuperAdmin` now defaults to `false`, not `true`.** This is a deliberate, load-bearing behavior change that Task 4 depends on, but it means **any existing integration test that relies on the old always-`true` default without minting a SuperAdmin JWT will now fail** — if `dotnet test` reports failures here, read them: a failure in `TenantQueryFilterTests.cs` is expected-and-fine (it uses its own `FakeTenantContext`, unaffected); a failure anywhere else needs the seeded caller in that test to actually be a SuperAdmin or have a matching `CompanyId`, matching real-world usage. Do not proceed to Task 4 until this is either all-green or every red is understood and expected.

- [ ] **Step 5: Commit**

```bash
git add Infrastructure/GymAppApi.Infrastructure/Tenancy/AmbientTenantContext.cs Infrastructure/GymAppApi.Infrastructure/Registration.cs
git commit -m "Make AmbientTenantContext a settable, dependency-free holder"
```

---

### Task 4: `TenantContextMiddleware` — populate it once per request

**Files:**
- Create: `Presentation/GymAppApi.WebApi/Middleware/TenantContextMiddleware.cs`
- Modify: `Presentation/GymAppApi.WebApi/Program.cs`
- Test: `Tests/GymAppApi.IntegrationTests/TenantContextMiddlewareTests.cs`

- [ ] **Step 1: Write the failing integration test**

```csharp
using System.Net.Http.Json;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.Persistence.Context;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GymAppApi.IntegrationTests;

public class TenantContextMiddlewareTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public TenantContextMiddlewareTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task GetBranches_AsGymAdmin_OnlySeesOwnCompanyBranches()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();

        var companyA = new Company { Name = "Company A", IsActive = true };
        var companyB = new Company { Name = "Company B", IsActive = true };
        db.Companies.AddRange(companyA, companyB);
        await db.SaveChangesAsync();

        db.Branches.AddRange(
            new Branch { CompanyId = companyA.Id, Name = "A - Merkez", Address = "..." },
            new Branch { CompanyId = companyB.Id, Name = "B - Merkez", Address = "..." });
        await db.SaveChangesAsync();

        var gymAdmin = new User { FullName = "Gym Admin A", Phone = "+905550000020", PasswordHash = "x" };
        db.Users.Add(gymAdmin);
        await db.SaveChangesAsync();
        db.Assignments.Add(new Assignment { UserId = gymAdmin.Id, CompanyId = companyA.Id, Role = AssignmentRole.GymAdmin, IsActive = true });
        await db.SaveChangesAsync();

        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var token = jwtService.GenerateAccessToken(new AccessTokenClaims(gymAdmin.Id, gymAdmin.FullName, gymAdmin.Email, gymAdmin.Phone)).Token;

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/branches");
        response.EnsureSuccessStatusCode();
        var branches = await response.Content.ReadFromJsonAsync<List<BranchDto>>();

        Assert.NotNull(branches);
        Assert.Single(branches!);
        Assert.Equal("A - Merkez", branches![0].Name);
    }

    private record BranchDto(int Id, string Name, string Address, bool IsActive);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter TenantContextMiddlewareTests`
Expected: FAIL — `GET /api/branches` currently has no `[Authorize]`, and even once authenticated, `AmbientTenantContext` is not yet populated by anything, so this returns both companies' branches (2), not 1.

- [ ] **Step 3: Write the middleware**

```csharp
using System.IdentityModel.Tokens.Jwt;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Infrastructure.Tenancy;

namespace GymAppApi.WebApi.Middleware;

// Runs once per request, right after authentication and before
// authorization/endpoint execution, so every downstream query filter and
// every AssignmentRoleAuthorizationHandler check sees the caller's real
// tenant scope. See the plan's "Key facts" for why this can't just reuse
// IUnitOfWork/IReadRepository<Assignment> to look itself up.
public class TenantContextMiddleware
{
    private readonly RequestDelegate _next;

    public TenantContextMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, AmbientTenantContext tenantContext, ITenantResolutionService resolutionService)
    {
        var subClaim = context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (subClaim is not null && int.TryParse(subClaim, out var userId))
        {
            var resolved = await resolutionService.ResolveForUserAsync(userId, context.RequestAborted);
            tenantContext.IsSuperAdmin = resolved.IsSuperAdmin;
            tenantContext.CompanyId = resolved.CompanyId;
            tenantContext.BranchId = resolved.BranchId;
        }

        await _next(context);
    }
}
```

- [ ] **Step 4: Add `[Authorize]` to `BranchesController` and wire the middleware into `Program.cs`**

In `Presentation/GymAppApi.WebApi/Controllers/BranchesController.cs`, add the import and attribute:

```csharp
using Microsoft.AspNetCore.Authorization;
```

```csharp
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BranchesController : ControllerBase
```

In `Presentation/GymAppApi.WebApi/Program.cs`, add the import:

```csharp
using GymAppApi.WebApi.Middleware;
```

(already imported for `ExceptionMiddleware` — confirm it's there, don't duplicate) and insert the middleware between authentication and authorization:

```csharp
app.UseAuthentication();
app.UseMiddleware<TenantContextMiddleware>();
app.UseAuthorization();
app.UseRateLimiter();
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter TenantContextMiddlewareTests`
Expected: PASS.

- [ ] **Step 6: Run the FULL test suite**

Run: `dotnet test`
Expected: PASS, all green. This is the real regression gate for Tasks 1–4 together — if anything other than an already-understood, already-fixed test from Task 3 Step 4 is red, stop and fix it before moving on; Tasks 7–11 all build on this being solid.

- [ ] **Step 7: Commit**

```bash
git add Presentation/GymAppApi.WebApi/Middleware/TenantContextMiddleware.cs Presentation/GymAppApi.WebApi/Program.cs Presentation/GymAppApi.WebApi/Controllers/BranchesController.cs Tests/GymAppApi.IntegrationTests/TenantContextMiddlewareTests.cs
git commit -m "Add TenantContextMiddleware and authorize BranchesController - tenant isolation is now real"
```

---

### Task 5: `SuperAdminOnly` and `StaffManagement` authorization policies

**Files:**
- Modify: `Presentation/GymAppApi.WebApi/Program.cs`

- [ ] **Step 1: Add the two new policies**

In `Program.cs`, extend the existing `AddAuthorization` block:

```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("GymAdminOrSuperAdmin", policy => policy.Requirements.Add(
        new GymAppApi.WebApi.Authorization.AssignmentRoleRequirement(
            GymAppApi.Domain.Enums.AssignmentRole.GymAdmin, GymAppApi.Domain.Enums.AssignmentRole.SuperAdmin)));
    options.AddPolicy("SuperAdminOnly", policy => policy.Requirements.Add(
        new GymAppApi.WebApi.Authorization.AssignmentRoleRequirement(
            GymAppApi.Domain.Enums.AssignmentRole.SuperAdmin)));
    options.AddPolicy("StaffManagement", policy => policy.Requirements.Add(
        new GymAppApi.WebApi.Authorization.AssignmentRoleRequirement(
            GymAppApi.Domain.Enums.AssignmentRole.BranchManager,
            GymAppApi.Domain.Enums.AssignmentRole.GymAdmin,
            GymAppApi.Domain.Enums.AssignmentRole.SuperAdmin)));
});
```

- [ ] **Step 2: Build to confirm it compiles**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Presentation/GymAppApi.WebApi/Program.cs
git commit -m "Add SuperAdminOnly and StaffManagement authorization policies"
```

---

### Task 6: `CreateCompanyCommand` — Company + Branch + Gym Admin in one transaction

**Files:**
- Create: `Core/GymAppApi.Application/Features/Companies/Commands/CreateCompany/CreateCompanyCommand.cs`
- Create: `Core/GymAppApi.Application/Features/Companies/Commands/CreateCompany/CreateCompanyCommandValidator.cs`
- Create: `Core/GymAppApi.Application/Features/Companies/Commands/CreateCompany/CreateCompanyCommandResult.cs`
- Create: `Core/GymAppApi.Application/Features/Companies/Commands/CreateCompany/CreateCompanyCommandHandler.cs`
- Test: `Tests/GymAppApi.UnitTests/Features/Companies/CreateCompanyCommandHandlerTests.cs`

- [ ] **Step 1: Write the command, validator, and result**

```csharp
// CreateCompanyCommand.cs
using GymAppApi.Application.Features.Companies.Commands.CreateCompany;
using MediatR;

namespace GymAppApi.Application.Features.Companies.Commands.CreateCompany;

public class CreateCompanyCommand : IRequest<CreateCompanyCommandResult>
{
    public string CompanyName { get; set; } = null!;
    public string BranchName { get; set; } = null!;
    public string BranchAddress { get; set; } = null!;
    public string GymAdminFullName { get; set; } = null!;
    public string GymAdminPhone { get; set; } = null!;
    public string? GymAdminEmail { get; set; }
}
```

```csharp
// CreateCompanyCommandValidator.cs
using FluentValidation;

namespace GymAppApi.Application.Features.Companies.Commands.CreateCompany;

public class CreateCompanyCommandValidator : AbstractValidator<CreateCompanyCommand>
{
    public CreateCompanyCommandValidator()
    {
        RuleFor(x => x.CompanyName).NotEmpty();
        RuleFor(x => x.BranchName).NotEmpty();
        RuleFor(x => x.BranchAddress).NotEmpty();
        RuleFor(x => x.GymAdminFullName).NotEmpty();
        RuleFor(x => x.GymAdminPhone).NotEmpty();
    }
}
```

```csharp
// CreateCompanyCommandResult.cs
namespace GymAppApi.Application.Features.Companies.Commands.CreateCompany;

public class CreateCompanyCommandResult
{
    public int CompanyId { get; set; }
    public int BranchId { get; set; }
    public int GymAdminUserId { get; set; }
    public string GymAdminPhone { get; set; } = null!;
}
```

- [ ] **Step 2: Write the failing tests**

```csharp
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Companies.Commands.CreateCompany;
using GymAppApi.Domain.Entities;
using Moq;

namespace GymAppApi.UnitTests.Features.Companies;

public class CreateCompanyCommandHandlerTests
{
    private static (Mock<IUnitOfWork> uow, Mock<IReadRepository<User>> userReadRepo, Mock<IWriteRepository<User>> userWriteRepo,
        Mock<IWriteRepository<Company>> companyWriteRepo, Mock<IWriteRepository<Branch>> branchWriteRepo,
        Mock<IWriteRepository<Assignment>> assignmentWriteRepo) Wire(bool phoneAlreadyExists, int? existingUserId = null)
    {
        var userReadRepo = new Mock<IReadRepository<User>>();
        userReadRepo.Setup(r => r.GetAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), null, false, default))
            .ReturnsAsync(phoneAlreadyExists ? new User { Id = existingUserId!.Value, FullName = "Existing", Phone = "+905551112233", PasswordHash = "x" } : null);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<User>()).Returns(userReadRepo.Object);
        var userWriteRepo = new Mock<IWriteRepository<User>>();
        uow.Setup(u => u.GetWriteRepository<User>()).Returns(userWriteRepo.Object);
        var companyWriteRepo = new Mock<IWriteRepository<Company>>();
        uow.Setup(u => u.GetWriteRepository<Company>()).Returns(companyWriteRepo.Object);
        var branchWriteRepo = new Mock<IWriteRepository<Branch>>();
        uow.Setup(u => u.GetWriteRepository<Branch>()).Returns(branchWriteRepo.Object);
        var assignmentWriteRepo = new Mock<IWriteRepository<Assignment>>();
        uow.Setup(u => u.GetWriteRepository<Assignment>()).Returns(assignmentWriteRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        uow.Setup(u => u.BeginTransactionAsync(default)).ReturnsAsync(Mock.Of<IAsyncDisposable>());

        return (uow, userReadRepo, userWriteRepo, companyWriteRepo, branchWriteRepo, assignmentWriteRepo);
    }

    private static CreateCompanyCommand ValidCommand() => new()
    {
        CompanyName = "Test Gym",
        BranchName = "Merkez",
        BranchAddress = "Adres",
        GymAdminFullName = "Ada Admin",
        GymAdminPhone = "+905551112233",
        GymAdminEmail = null,
    };

    [Fact]
    public async Task Handle_WhenPhoneIsNew_CreatesCompanyBranchUserAndGymAdminAssignment()
    {
        var (uow, _, userWriteRepo, companyWriteRepo, branchWriteRepo, assignmentWriteRepo) = Wire(phoneAlreadyExists: false);
        var passwordHasher = new Mock<IPasswordHasher>();
        passwordHasher.Setup(p => p.Hash(It.IsAny<string>())).Returns("hashed");
        var smsSender = new Mock<ISmsSender>();
        var handler = new CreateCompanyCommandHandler(uow.Object, passwordHasher.Object, smsSender.Object);

        await handler.Handle(ValidCommand(), CancellationToken.None);

        companyWriteRepo.Verify(r => r.AddAsync(It.Is<Company>(c => c.Name == "Test Gym" && c.IsActive), default), Times.Once);
        branchWriteRepo.Verify(r => r.AddAsync(It.Is<Branch>(b => b.Name == "Merkez" && b.Address == "Adres"), default), Times.Once);
        userWriteRepo.Verify(r => r.AddAsync(It.Is<User>(u => u.Phone == "+905551112233" && u.FullName == "Ada Admin"), default), Times.Once);
        assignmentWriteRepo.Verify(r => r.AddAsync(It.Is<Assignment>(a => a.Role == GymAppApi.Domain.Enums.AssignmentRole.GymAdmin && a.BranchId == null), default), Times.Once);
        smsSender.Verify(s => s.SendAsync("+905551112233", It.IsAny<string>(), default), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenPhoneAlreadyBelongsToAUser_ReusesThatUserInsteadOfCreatingANewOne()
    {
        var (uow, _, userWriteRepo, _, _, assignmentWriteRepo) = Wire(phoneAlreadyExists: true, existingUserId: 55);
        var passwordHasher = new Mock<IPasswordHasher>();
        var smsSender = new Mock<ISmsSender>();
        var handler = new CreateCompanyCommandHandler(uow.Object, passwordHasher.Object, smsSender.Object);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.Equal(55, result.GymAdminUserId);
        userWriteRepo.Verify(r => r.AddAsync(It.IsAny<User>(), default), Times.Never);
        assignmentWriteRepo.Verify(r => r.AddAsync(It.Is<Assignment>(a => a.UserId == 55), default), Times.Once);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test --filter CreateCompanyCommandHandlerTests`
Expected: FAIL — `CreateCompanyCommandHandler` does not exist yet.

- [ ] **Step 4: Write the handler**

```csharp
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.Companies.Commands.CreateCompany;

public class CreateCompanyCommandHandler : IRequestHandler<CreateCompanyCommand, CreateCompanyCommandResult>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ISmsSender _smsSender;

    public CreateCompanyCommandHandler(IUnitOfWork unitOfWork, IPasswordHasher passwordHasher, ISmsSender smsSender)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _smsSender = smsSender;
    }

    public async Task<CreateCompanyCommandResult> Handle(CreateCompanyCommand request, CancellationToken cancellationToken)
    {
        // No [Authorize(Policy = "SuperAdminOnly")]-level re-check needed here
        // unlike CreateAssignmentCommandHandler's GymAdmin case - a SuperAdmin
        // has no per-company scope to violate, so the policy's own fresh
        // per-request Assignment re-query (AssignmentRoleAuthorizationHandler)
        // is already the complete check.
        var existingGymAdmin = await _unitOfWork.GetReadRepository<User>()
            .GetAsync(u => u.Phone == request.GymAdminPhone, cancellationToken: cancellationToken);

        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var company = new Company { Name = request.CompanyName, IsActive = true };
            await _unitOfWork.GetWriteRepository<Company>().AddAsync(company, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken); // need company.Id for the branch

            var branch = new Branch
            {
                CompanyId = company.Id,
                Name = request.BranchName,
                Address = request.BranchAddress,
                IsActive = true,
            };
            await _unitOfWork.GetWriteRepository<Branch>().AddAsync(branch, cancellationToken);

            var gymAdminUser = existingGymAdmin;
            if (gymAdminUser is null)
            {
                gymAdminUser = new User
                {
                    FullName = request.GymAdminFullName,
                    Phone = request.GymAdminPhone,
                    Email = request.GymAdminEmail,
                    PasswordHash = _passwordHasher.Hash(Guid.NewGuid().ToString()),
                    PhoneVerified = false,
                };
                await _unitOfWork.GetWriteRepository<User>().AddAsync(gymAdminUser, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken); // need gymAdminUser.Id for the assignment
            }

            // GymAdmin's BranchId is null by design (Assignment.cs's own
            // comment: a GymAdmin assignment has CompanyId set, BranchId
            // null, meaning "all branches of this company").
            await _unitOfWork.GetWriteRepository<Assignment>().AddAsync(new Assignment
            {
                UserId = gymAdminUser.Id,
                CompanyId = company.Id,
                BranchId = null,
                Role = AssignmentRole.GymAdmin,
                IsActive = true,
            }, cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            await _smsSender.SendAsync(
                request.GymAdminPhone,
                $"GymApp hesabınız oluşturuldu. Şifrenizi belirlemek için 'Şifremi Unuttum' akışını kullanın.",
                cancellationToken);

            return new CreateCompanyCommandResult
            {
                CompanyId = company.Id,
                BranchId = branch.Id,
                GymAdminUserId = gymAdminUser.Id,
                GymAdminPhone = gymAdminUser.Phone,
            };
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter CreateCompanyCommandHandlerTests`
Expected: PASS, 2/2.

- [ ] **Step 6: Commit**

```bash
git add Core/GymAppApi.Application/Features/Companies/ Tests/GymAppApi.UnitTests/Features/Companies/
git commit -m "Add CreateCompanyCommand - Company+Branch+GymAdmin in one transaction"
```

---

### Task 7: `POST /api/companies` — `CompaniesController`

**Files:**
- Create: `Presentation/GymAppApi.WebApi/Controllers/CompaniesController.cs`
- Test: `Tests/GymAppApi.IntegrationTests/CompaniesAuthorizationTests.cs`

- [ ] **Step 1: Write the failing integration tests**

```csharp
using System.Net;
using System.Net.Http.Json;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.Persistence.Context;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GymAppApi.IntegrationTests;

public class CompaniesAuthorizationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public CompaniesAuthorizationTests(CustomWebApplicationFactory factory) => _factory = factory;

    private static object ValidBody() => new
    {
        companyName = "New Gym",
        branchName = "Merkez",
        branchAddress = "Adres 1",
        gymAdminFullName = "Ada Admin",
        gymAdminPhone = "+905559998877",
        gymAdminEmail = (string?)null,
    };

    [Fact]
    public async Task Create_WithoutToken_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/companies", ValidBody());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithGymAdminToken_Returns403()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();
        var company = new Company { Name = "Existing Co", IsActive = true };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var gymAdmin = new User { FullName = "Gym Admin", Phone = "+905550001111", PasswordHash = "x" };
        db.Users.Add(gymAdmin);
        await db.SaveChangesAsync();
        db.Assignments.Add(new Assignment { UserId = gymAdmin.Id, CompanyId = company.Id, Role = AssignmentRole.GymAdmin, IsActive = true });
        await db.SaveChangesAsync();

        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var token = jwtService.GenerateAccessToken(new AccessTokenClaims(gymAdmin.Id, gymAdmin.FullName, gymAdmin.Email, gymAdmin.Phone)).Token;

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var response = await client.PostAsJsonAsync("/api/companies", ValidBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithSuperAdminToken_Returns201()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();
        var superAdmin = new User { FullName = "Super Admin", Phone = "+905550002222", PasswordHash = "x" };
        db.Users.Add(superAdmin);
        await db.SaveChangesAsync();
        db.Assignments.Add(new Assignment { UserId = superAdmin.Id, CompanyId = null, Role = AssignmentRole.SuperAdmin, IsActive = true });
        await db.SaveChangesAsync();

        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var token = jwtService.GenerateAccessToken(new AccessTokenClaims(superAdmin.Id, superAdmin.FullName, superAdmin.Email, superAdmin.Phone)).Token;

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var response = await client.PostAsJsonAsync("/api/companies", ValidBody());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter CompaniesAuthorizationTests`
Expected: FAIL — `/api/companies` route doesn't exist yet (404, not the asserted status codes).

- [ ] **Step 3: Write the controller**

```csharp
using GymAppApi.Application.Features.Companies.Commands.CreateCompany;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymAppApi.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "SuperAdminOnly")]
public class CompaniesController : ControllerBase
{
    private readonly IMediator _mediator;

    public CompaniesController(IMediator mediator) => _mediator = mediator;

    [HttpPost]
    public async Task<IActionResult> Create(CreateCompanyCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter CompaniesAuthorizationTests`
Expected: PASS, 3/3.

- [ ] **Step 5: Commit**

```bash
git add Presentation/GymAppApi.WebApi/Controllers/CompaniesController.cs Tests/GymAppApi.IntegrationTests/CompaniesAuthorizationTests.cs
git commit -m "Add POST /api/companies, SuperAdmin-only"
```

---

### Task 8: `AddStaffMemberCommand` — create-or-attach a Member/Trainer

**Files:**
- Create: `Core/GymAppApi.Application/Features/Assignments/Commands/AddStaffMember/AddStaffMemberCommand.cs`
- Create: `Core/GymAppApi.Application/Features/Assignments/Commands/AddStaffMember/AddStaffMemberCommandValidator.cs`
- Create: `Core/GymAppApi.Application/Features/Assignments/Commands/AddStaffMember/AddStaffMemberCommandResult.cs`
- Create: `Core/GymAppApi.Application/Features/Assignments/Commands/AddStaffMember/AddStaffMemberCommandHandler.cs`
- Test: `Tests/GymAppApi.UnitTests/Features/Assignments/AddStaffMemberCommandHandlerTests.cs`

This is the endpoint that fills the actual gap identified in this plan's "Key facts": `CreateAssignmentCommand` only attaches an assignment to an **existing** user and hardcodes `Role = Member`. This new command creates the `User` too when the phone is new, and accepts `Member` or `Trainer` as the role (never `BranchManager`/`GymAdmin`/`SuperAdmin` — matches the product design doc's own wording: staff add "yeni bir Üye veya Antrenör", nothing higher).

- [ ] **Step 1: Write the command, validator, and result**

```csharp
// AddStaffMemberCommand.cs
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.Assignments.Commands.AddStaffMember;

public class AddStaffMemberCommand : IRequest<AddStaffMemberCommandResult>
{
    public string FullName { get; set; } = null!;
    public string Phone { get; set; } = null!;
    public string? Email { get; set; }
    public AssignmentRole Role { get; set; }
    public int BranchId { get; set; }

    // Set by the controller from the caller's own JWT sub claim - see this
    // plan's "Key facts" on why the [Authorize(Policy = "StaffManagement")]
    // attribute alone is never enough.
    public int RequestedByUserId { get; set; }
}
```

```csharp
// AddStaffMemberCommandValidator.cs
using FluentValidation;
using GymAppApi.Domain.Enums;

namespace GymAppApi.Application.Features.Assignments.Commands.AddStaffMember;

public class AddStaffMemberCommandValidator : AbstractValidator<AddStaffMemberCommand>
{
    public AddStaffMemberCommandValidator()
    {
        RuleFor(x => x.FullName).NotEmpty();
        RuleFor(x => x.Phone).NotEmpty();
        RuleFor(x => x.BranchId).GreaterThan(0);
        RuleFor(x => x.Role).Must(r => r is AssignmentRole.Member or AssignmentRole.Trainer)
            .WithMessage("Role must be Member or Trainer.");
    }
}
```

```csharp
// AddStaffMemberCommandResult.cs
namespace GymAppApi.Application.Features.Assignments.Commands.AddStaffMember;

public class AddStaffMemberCommandResult
{
    public int AssignmentId { get; set; }
    public int UserId { get; set; }
    public int CompanyId { get; set; }
    public int BranchId { get; set; }
    public string Role { get; set; } = null!;
}
```

- [ ] **Step 2: Write the failing tests**

```csharp
using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Assignments.Commands.AddStaffMember;
using GymAppApi.Application.Features.Assignments.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Assignments;

public class AddStaffMemberCommandHandlerTests
{
    private const int CallerId = 42;
    private const int BranchIdInCompany1 = 10;

    private static (Mock<IUnitOfWork> uow, Mock<IWriteRepository<User>> userWriteRepo, Mock<IWriteRepository<Assignment>> assignmentWriteRepo) Wire(
        IReadOnlyList<Assignment> callerAssignments, Branch? branch, User? existingUser, bool alreadyAssigned)
    {
        var uow = new Mock<IUnitOfWork>();

        var assignmentReadRepo = new Mock<IReadRepository<Assignment>>();
        assignmentReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Assignment, bool>>>(), null, null, false, default))
            .ReturnsAsync(callerAssignments);
        assignmentReadRepo.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Assignment, bool>>>(), default))
            .ReturnsAsync(alreadyAssigned);
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(assignmentReadRepo.Object);
        var assignmentWriteRepo = new Mock<IWriteRepository<Assignment>>();
        uow.Setup(u => u.GetWriteRepository<Assignment>()).Returns(assignmentWriteRepo.Object);

        var branchReadRepo = new Mock<IReadRepository<Branch>>();
        branchReadRepo.Setup(r => r.GetAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Branch, bool>>>(), null, false, default))
            .ReturnsAsync(branch);
        uow.Setup(u => u.GetReadRepository<Branch>()).Returns(branchReadRepo.Object);

        var userReadRepo = new Mock<IReadRepository<User>>();
        userReadRepo.Setup(r => r.GetAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), null, false, default))
            .ReturnsAsync(existingUser);
        uow.Setup(u => u.GetReadRepository<User>()).Returns(userReadRepo.Object);
        var userWriteRepo = new Mock<IWriteRepository<User>>();
        uow.Setup(u => u.GetWriteRepository<User>()).Returns(userWriteRepo.Object);

        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        return (uow, userWriteRepo, assignmentWriteRepo);
    }

    private static Branch Branch1() => new() { Id = BranchIdInCompany1, CompanyId = 1, Name = "Merkez", Address = "..." };

    private static AddStaffMemberCommand ValidCommand() => new()
    {
        FullName = "New Trainer",
        Phone = "+905550003333",
        Role = AssignmentRole.Trainer,
        BranchId = BranchIdInCompany1,
        RequestedByUserId = CallerId,
    };

    [Fact]
    public async Task Handle_WhenCallerIsGymAdminOfTheBranchsCompany_CreatesNewUserAndAssignment()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, userWriteRepo, assignmentWriteRepo) = Wire(callerAssignments, Branch1(), existingUser: null, alreadyAssigned: false);
        var smsSender = new Mock<ISmsSender>();
        var passwordHasher = new Mock<IPasswordHasher>();
        passwordHasher.Setup(p => p.Hash(It.IsAny<string>())).Returns("hashed");
        var handler = new AddStaffMemberCommandHandler(uow.Object, passwordHasher.Object, smsSender.Object);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.Equal(1, result.CompanyId);
        userWriteRepo.Verify(r => r.AddAsync(It.Is<User>(u => u.Phone == "+905550003333"), default), Times.Once);
        assignmentWriteRepo.Verify(r => r.AddAsync(It.Is<Assignment>(a => a.Role == AssignmentRole.Trainer && a.BranchId == BranchIdInCompany1), default), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCallerIsBranchManagerOfADifferentBranch_ThrowsForbiddenException()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, BranchId = 999, Role = AssignmentRole.BranchManager, IsActive = true } };
        var (uow, _, assignmentWriteRepo) = Wire(callerAssignments, Branch1(), existingUser: null, alreadyAssigned: false);
        var handler = new AddStaffMemberCommandHandler(uow.Object, Mock.Of<IPasswordHasher>(), Mock.Of<ISmsSender>());

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(ValidCommand(), CancellationToken.None));
        assignmentWriteRepo.Verify(r => r.AddAsync(It.IsAny<Assignment>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenBranchDoesNotExist_ThrowsNotFoundException()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, _, _) = Wire(callerAssignments, branch: null, existingUser: null, alreadyAssigned: false);
        var handler = new AddStaffMemberCommandHandler(uow.Object, Mock.Of<IPasswordHasher>(), Mock.Of<ISmsSender>());

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(ValidCommand(), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenPhoneAlreadyHasAnActiveAssignmentInThisCompany_ThrowsUserAlreadyAssignedException()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var existingUser = new User { Id = 7, FullName = "Existing", Phone = "+905550003333", PasswordHash = "x" };
        var (uow, _, assignmentWriteRepo) = Wire(callerAssignments, Branch1(), existingUser, alreadyAssigned: true);
        var handler = new AddStaffMemberCommandHandler(uow.Object, Mock.Of<IPasswordHasher>(), Mock.Of<ISmsSender>());

        await Assert.ThrowsAsync<UserAlreadyAssignedException>(() => handler.Handle(ValidCommand(), CancellationToken.None));
        assignmentWriteRepo.Verify(r => r.AddAsync(It.IsAny<Assignment>(), default), Times.Never);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test --filter AddStaffMemberCommandHandlerTests`
Expected: FAIL — `AddStaffMemberCommandHandler` does not exist yet.

- [ ] **Step 4: Write the handler**

```csharp
using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Assignments.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.Assignments.Commands.AddStaffMember;

public class AddStaffMemberCommandHandler : IRequestHandler<AddStaffMemberCommand, AddStaffMemberCommandResult>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ISmsSender _smsSender;

    public AddStaffMemberCommandHandler(IUnitOfWork unitOfWork, IPasswordHasher passwordHasher, ISmsSender smsSender)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _smsSender = smsSender;
    }

    public async Task<AddStaffMemberCommandResult> Handle(AddStaffMemberCommand request, CancellationToken cancellationToken)
    {
        var branch = await _unitOfWork.GetReadRepository<Branch>()
            .GetAsync(b => b.Id == request.BranchId, cancellationToken: cancellationToken);
        if (branch is null)
        {
            throw new NotFoundException($"Şube {request.BranchId} bulunamadı.");
        }

        // Same pattern as CreateAssignmentCommandHandler: the [Authorize]
        // policy only proves the caller holds SOME staff role somewhere -
        // re-check it's scoped to THIS branch's company (GymAdmin) or THIS
        // exact branch (BranchManager). SuperAdmin bypasses both checks.
        var callerAssignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
            a => a.UserId == request.RequestedByUserId && a.IsActive, cancellationToken: cancellationToken);
        var callerIsAuthorized = callerAssignments.Any(a =>
            a.Role == AssignmentRole.SuperAdmin ||
            (a.Role == AssignmentRole.GymAdmin && a.CompanyId == branch.CompanyId) ||
            (a.Role == AssignmentRole.BranchManager && a.BranchId == branch.Id));
        if (!callerIsAuthorized)
        {
            throw new ForbiddenException("Bu şubeye üye/antrenör ekleme yetkiniz yok.");
        }

        var userReadRepo = _unitOfWork.GetReadRepository<User>();
        var existingUser = await userReadRepo.GetAsync(u => u.Phone == request.Phone, cancellationToken: cancellationToken);

        var alreadyAssignedInCompany = existingUser is not null && await _unitOfWork.GetReadRepository<Assignment>().AnyAsync(
            a => a.UserId == existingUser.Id && a.CompanyId == branch.CompanyId && a.IsActive, cancellationToken);
        if (alreadyAssignedInCompany)
        {
            throw new UserAlreadyAssignedException();
        }

        var user = existingUser;
        if (user is null)
        {
            user = new User
            {
                FullName = request.FullName,
                Phone = request.Phone,
                Email = request.Email,
                PasswordHash = _passwordHasher.Hash(Guid.NewGuid().ToString()),
                PhoneVerified = false,
            };
            await _unitOfWork.GetWriteRepository<User>().AddAsync(user, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken); // need user.Id for the assignment
        }

        var assignment = new Assignment
        {
            UserId = user.Id,
            CompanyId = branch.CompanyId,
            BranchId = branch.Id,
            Role = request.Role,
            IsActive = true,
        };
        await _unitOfWork.GetWriteRepository<Assignment>().AddAsync(assignment, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _smsSender.SendAsync(
            request.Phone,
            "GymApp hesabınız oluşturuldu. Şifrenizi belirlemek için 'Şifremi Unuttum' akışını kullanın.",
            cancellationToken);

        return new AddStaffMemberCommandResult
        {
            AssignmentId = assignment.Id,
            UserId = user.Id,
            CompanyId = branch.CompanyId,
            BranchId = branch.Id,
            Role = assignment.Role.ToString(),
        };
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter AddStaffMemberCommandHandlerTests`
Expected: PASS, 4/4.

- [x] **Step 6: Commit**

```bash
git add Core/GymAppApi.Application/Features/Assignments/Commands/AddStaffMember/ Tests/GymAppApi.UnitTests/Features/Assignments/AddStaffMemberCommandHandlerTests.cs
git commit -m "Add AddStaffMemberCommand - creates a new User when the phone doesn't exist yet"
```

**Follow-up (code review, same pattern as CreateCompanyCommand):** wrapped the create in a transaction and added the email-uniqueness check on the new-user path. Commit `5c1b926` "Address code review feedback on AddStaffMemberCommand".

---

### Task 9: `POST /api/assignments/staff` — extend `AssignmentsController`

**Files:**
- Modify: `Presentation/GymAppApi.WebApi/Controllers/AssignmentsController.cs`
- Test: `Tests/GymAppApi.IntegrationTests/AddStaffMemberAuthorizationTests.cs`

The controller currently has a single `[Authorize(Policy = "GymAdminOrSuperAdmin")]` at the class level. The new action needs the wider `"StaffManagement"` policy (adds `BranchManager`), so the existing policy moves from the class down to the existing `Create` action — this does not change that action's effective authorization at all (same policy, same callers allowed).

- [ ] **Step 1: Write the failing integration tests**

```csharp
using System.Net;
using System.Net.Http.Json;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.Persistence.Context;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GymAppApi.IntegrationTests;

public class AddStaffMemberAuthorizationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public AddStaffMemberAuthorizationTests(CustomWebApplicationFactory factory) => _factory = factory;

    private async Task<(int branchId, string branchManagerToken, string memberToken)> SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();

        var company = new Company { Name = "Test Co", IsActive = true };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var branch = new Branch { CompanyId = company.Id, Name = "Merkez", Address = "..." };
        db.Branches.Add(branch);
        await db.SaveChangesAsync();

        var branchManager = new User { FullName = "Branch Manager", Phone = "+905550004444", PasswordHash = "x" };
        var member = new User { FullName = "Plain Member", Phone = "+905550005555", PasswordHash = "x" };
        db.Users.AddRange(branchManager, member);
        await db.SaveChangesAsync();
        db.Assignments.Add(new Assignment { UserId = branchManager.Id, CompanyId = company.Id, BranchId = branch.Id, Role = AssignmentRole.BranchManager, IsActive = true });
        await db.SaveChangesAsync();

        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var branchManagerToken = jwtService.GenerateAccessToken(new AccessTokenClaims(branchManager.Id, branchManager.FullName, branchManager.Email, branchManager.Phone)).Token;
        var memberToken = jwtService.GenerateAccessToken(new AccessTokenClaims(member.Id, member.FullName, member.Email, member.Phone)).Token;

        return (branch.Id, branchManagerToken, memberToken);
    }

    [Fact]
    public async Task AddStaff_WithBranchManagerToken_Returns201()
    {
        var (branchId, branchManagerToken, _) = await SeedAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", branchManagerToken);

        var response = await client.PostAsJsonAsync("/api/assignments/staff", new
        {
            fullName = "New Member",
            phone = "+905550006666",
            role = "Member",
            branchId,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task AddStaff_WithPlainMemberToken_Returns403()
    {
        var (branchId, _, memberToken) = await SeedAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", memberToken);

        var response = await client.PostAsJsonAsync("/api/assignments/staff", new
        {
            fullName = "New Member",
            phone = "+905550006666",
            role = "Member",
            branchId,
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter AddStaffMemberAuthorizationTests`
Expected: FAIL — route doesn't exist yet.

- [ ] **Step 3: Update the controller**

```csharp
using System.IdentityModel.Tokens.Jwt;
using GymAppApi.Application.Features.Assignments.Commands.AddStaffMember;
using GymAppApi.Application.Features.Assignments.Commands.CreateAssignment;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymAppApi.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AssignmentsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AssignmentsController(IMediator mediator) => _mediator = mediator;

    private int CurrentUserId => int.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    [Authorize(Policy = "GymAdminOrSuperAdmin")]
    [HttpPost]
    public async Task<IActionResult> Create(CreateAssignmentCommand command, CancellationToken cancellationToken)
    {
        command.RequestedByUserId = CurrentUserId;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [Authorize(Policy = "StaffManagement")]
    [HttpPost("staff")]
    public async Task<IActionResult> AddStaffMember(AddStaffMemberCommand command, CancellationToken cancellationToken)
    {
        command.RequestedByUserId = CurrentUserId;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter AddStaffMemberAuthorizationTests`
Expected: PASS, 2/2.

- [ ] **Step 5: Run the full suite (regression check for the moved `[Authorize]`)**

Run: `dotnet test`
Expected: PASS — `AssignmentsAuthorizationTests` (Task-0 existing tests) must still be green; moving the attribute from class to action must not have changed `POST /api/assignments`'s effective authorization.

- [x] **Step 6: Commit**

```bash
git add Presentation/GymAppApi.WebApi/Controllers/AssignmentsController.cs Tests/GymAppApi.IntegrationTests/AddStaffMemberAuthorizationTests.cs
git commit -m "Add POST /api/assignments/staff for GymAdmin/BranchManager/SuperAdmin"
```

**Note (found while writing the test, not in the plan's original scope):** `"role":"Member"` in the JSON body failed model binding (400) because nothing registered a `JsonStringEnumConverter` — `AddStaffMemberCommand.Role` is the first request body to expose an enum to clients. Added `builder.Services.AddControllers().AddJsonOptions(...)` in `Program.cs` in the same commit (`24ef574`). Full suite: 99 unit + 19 integration, all green.

---

### Task 10: Manual end-to-end verification (both new endpoints, against a real Postgres)

**Files:** none — this task is manual verification, no code changes.

- [x] **Step 1: Start the local stack** — Docker Desktop + the `postgres` container (already had `gymapp_dev` from earlier sessions) + `dotnet run` against `http://localhost:5195`.

- [x] **Step 2: Log in as the seeded Super Admin** — 200, `accessToken` returned (identifier `+900000000000`).

- [x] **Step 3: Create a company** — 201: `{"companyId":4,"branchId":2,"gymAdminUserId":10,"gymAdminPhone":"+905551234567"}`. Console log confirmed `[FAKE SMS] To: +905551234567 | ...`.

- [x] **Step 4: Log in as the new Gym Admin via Forgot Password** — `forgot-password` → 200 → OTP read from console log → `reset-password` → 204 → `login` with the new password → 200 with a fresh token pair. Confirms the "staff-created account, member sets their own password via the existing Forgot Password flow" story end to end.

- [x] **Step 5: Add a staff member as this new Gym Admin** — 201: `{"assignmentId":6,"userId":11,"companyId":4,"branchId":2,"role":"Member"}`.

- [x] **Step 6: Confirm tenant isolation with `GET /api/branches`** — as the Gym Admin token, returned exactly `[{"id":2,"companyId":4,"name":"Merkez",...}]` — one branch, this company's own, despite other companies'/branches' rows already existing in `gymapp_dev` from earlier sessions. **Tenant isolation is confirmed real against a live Postgres, not just InMemory tests.**

**Verified 2026-09-17.** No code changes from this task. Background `dotnet run` process stopped afterward.

---

### Task 11: Retire self-service registration

**Files:**
- Modify: `Presentation/GymAppApi.WebApi/Controllers/AuthController.cs`
- Delete: `Core/GymAppApi.Application/Features/Auth/Commands/RegisterRequestOtp/` (whole folder)
- Delete: `Core/GymAppApi.Application/Features/Auth/Commands/RegisterComplete/` (whole folder)
- Delete: `Core/GymAppApi.Application/Features/Auth/Exceptions/PhoneAlreadyRegisteredException.cs` and `EmailAlreadyRegisteredException.cs` (confirm no other usage first — Step 1)
- Delete: `Tests/GymAppApi.UnitTests/Features/Auth/RegisterCompleteCommandHandlerTests.cs`, `RegisterRequestOtpCommandHandlerTests.cs`
- Delete: `Tests/GymAppApi.IntegrationTests/RegisterCompleteAttemptPersistenceTests.cs`, `RegisterCompleteMaxAttemptsPipelineTests.cs`

**Do this task LAST, only after the mobile plan has already shipped its "Add Company"/"Add Staff Member" screens and removed its own Register screen** (mobile plan's own last task) — until then, self-registration is still the only way anyone can create a test account on a fresh database, including for this very plan's own manual verification in Task 10.

**Status check (2026-09-17): still blocked.** `C:\Users\MBEYAZBULUT\Documents\GitHub\GymApp` has only added its plan file (commit `4805d08` "Add Tenant Onboarding (Mobile) implementation plan") — no screens implemented yet, no memory entry tracking its progress. Do not start this task until that repo's own progress memory (once it exists) or its git log shows the Add Company/Add Staff Member screens shipped and Register screen removed.

- [ ] **Step 1: Confirm nothing else references what's about to be deleted**

Run: `grep -rn "PhoneAlreadyRegisteredException\|EmailAlreadyRegisteredException\|RegisterRequestOtpCommand\|RegisterCompleteCommand" --include=*.cs .`
Expected: every hit is inside one of the files listed above to be deleted, or `AuthController.cs` (handled in Step 2). If anything else references them, stop and re-scope this step — do not delete something still in use.

- [ ] **Step 2: Remove the two routes from `AuthController`**

Remove these two actions (and their now-unused `using` lines for `RegisterComplete`/`RegisterRequestOtp`) from `Presentation/GymAppApi.WebApi/Controllers/AuthController.cs`:

```csharp
    [HttpPost("register/request-otp")]
    public async Task<IActionResult> RequestRegistrationOtp(RegisterRequestOtpCommand command, CancellationToken cancellationToken)
    {
        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }

    [HttpPost("register/complete")]
    public async Task<IActionResult> CompleteRegistration(RegisterCompleteCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }
```

- [ ] **Step 3: Delete the files listed above**

```bash
git rm -r Core/GymAppApi.Application/Features/Auth/Commands/RegisterRequestOtp
git rm -r Core/GymAppApi.Application/Features/Auth/Commands/RegisterComplete
git rm Core/GymAppApi.Application/Features/Auth/Exceptions/PhoneAlreadyRegisteredException.cs
git rm Core/GymAppApi.Application/Features/Auth/Exceptions/EmailAlreadyRegisteredException.cs
git rm Tests/GymAppApi.UnitTests/Features/Auth/RegisterCompleteCommandHandlerTests.cs
git rm Tests/GymAppApi.UnitTests/Features/Auth/RegisterRequestOtpCommandHandlerTests.cs
git rm Tests/GymAppApi.IntegrationTests/RegisterCompleteAttemptPersistenceTests.cs
git rm Tests/GymAppApi.IntegrationTests/RegisterCompleteMaxAttemptsPipelineTests.cs
```

- [ ] **Step 4: Build and run the full suite**

Run: `dotnet build && dotnet test`
Expected: build succeeds with 0 errors (confirms nothing else referenced the deleted types), full suite passes.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Retire self-service registration - accounts are now staff-created via CreateCompany/AddStaffMember"
```

---

## Self-review notes (from writing this plan)

- **Spec coverage:** "Yeni Company/Branch açma" → Tasks 6–7. "Kullanıcı/Assignment oluşturma" (staff adds Member/Trainer) → Tasks 8–9. "İlk Super Admin oluşturma (bootstrap)" → already done (documented in Key Facts, no new task needed). Real per-request tenant isolation (a precondition the design doc assumes but the Real Auth plan explicitly deferred) → Tasks 1–4. Self-servis kaydın kaldırılması → Task 11.
- **Explicitly out of scope, flagged rather than silently dropped:** the "birden fazla Assignment → Şirket/Şube Seç ekranı" context-switcher (product design doc, "Çoklu Şirkete Bağlılık ve Giriş Akışı") — `TenantResolutionService` picks the caller's first Assignment deterministically instead. Promoting a user to `BranchManager` itself isn't covered by any endpoint in this plan (only `Member`/`Trainer` via staff, and `GymAdmin` via Super Admin) — the design doc doesn't specify who creates a Branch Manager; raise this with the user before the next plan that needs it.
