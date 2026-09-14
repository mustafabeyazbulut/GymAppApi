# Backend Foundation (Onion + CQRS/MediatR + Repository/UnitOfWork) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up the GymAppApi backend skeleton — Onion Architecture (Domain/Application/Infrastructure/Persistence/WebApi), CQRS via MediatR, generic Repository + UnitOfWork, PostgreSQL via EF Core with mandatory multi-tenant global query filters — and prove the whole stack end-to-end with one real vertical-slice feature (Branch management), plus seed the mobile-facing API documentation.

**Architecture:** Mirrors the reference project `EnvanteriX` (Core/Domain, Core/Application, Infrastructure/Persistence, Infrastructure/Infrastructure, Presentation/WebApi; feature-folder-by-technical-layer CQRS; `Registration.cs` DI-extension-per-layer; generic `IReadRepository<T>`/`IWriteRepository<T>`/`IUnitOfWork`). It deliberately **diverges** from EnvanteriX in the four places the architecture audit flagged as gaps for a multi-tenant product: (1) global query filters + `SaveChangesAsync` stamping for `CompanyId`/`BranchId` (EnvanteriX has none), (2) no ASP.NET Core Identity — plain `User`/`Assignment` entities since Identity's global role model can't express "same user, different role per company/branch", (3) a corrected exception→HTTP-status mapping (EnvanteriX maps all domain `NotFound` exceptions to 500 — a bug), (4) real xUnit test coverage (EnvanteriX has zero tests). AutoMapper's EnvanteriX wrapper (static mutable `MapperConfiguration` rebuilt per type pair) is **not** carried over — this plan uses plain manual mapping for the simple DTOs in scope.

**Tech Stack:** .NET 10, ASP.NET Core Web API, EF Core 10 + Npgsql (PostgreSQL), MediatR, FluentValidation, xUnit + Moq + EF Core InMemory provider.

**Out of scope for this plan (later plans):** Authentication/JWT/OTP, Tenant Onboarding (Super Admin creates Company/Branch/GymAdmin), Package/Membership/Freeze, Localization/Translation table, AuditLog read API, all Faz 2 modules. This plan only builds the foundation + a non-auth-gated Branch CRUD vertical slice to prove the stack compiles, migrates, and filters correctly.

---

## Architecture at a glance

```
                    ┌─────────────────────────┐
                    │   GymAppApi.WebApi      │  Controllers, Program.cs,
                    │   (Presentation)        │  Swagger, ExceptionMiddleware
                    └───────────┬─────────────┘
                                │ references
              ┌─────────────────┼─────────────────┐
              ▼                                   ▼
  ┌────────────────────────┐         ┌─────────────────────────┐
  │ GymAppApi.Infrastructure│         │  GymAppApi.Persistence  │
  │ (tenant context stub,   │         │  (DbContext, Repository,│
  │  Registration.cs)       │         │   UnitOfWork, Configs,   │
  └───────────┬─────────────┘         │   Migrations)           │
              │                       └────────────┬─────────────┘
              └───────────────┬───────────────────--┘
                              ▼
                ┌─────────────────────────┐
                │  GymAppApi.Application  │  MediatR Commands/Queries/
                │  (Core)                 │  Handlers/Validators/Rules,
                │                         │  Interfaces (IUnitOfWork,
                │                         │  IReadRepository, ITenantContext)
                └────────────┬────────────┘
                             ▼
                ┌─────────────────────────┐
                │   GymAppApi.Domain      │  Entities, Enums, Common
                │   (Core, no refs)       │  (EntityBase, ICompanyScoped...)
                └─────────────────────────┘
```

Request flow for the vertical slice (`POST /branches`):

```
Controller --Send(CreateBranchCommand)--> MediatR
   -> ValidationBehavior (FluentValidation: shape check)
   -> TransactionBehavior (only for ITransactionalRequest commands — not needed for Branch, shown for later reuse)
   -> CreateBranchCommandHandler
        -> BranchRules.CompanyMustExist / BranchNameMustBeUnique (DB-aware checks)
        -> IUnitOfWork.GetWriteRepository<Branch>().AddAsync(...)
        -> IUnitOfWork.SaveAsync()
              -> DbContext.SaveChangesAsync() override
                   -> stamps CreatedAt/UpdatedAt
                   -> stamps CompanyId from ambient ITenantContext where applicable
   <- CreateBranchCommandResult
Controller <- 201 Created
```

---

## Task 1: Solution & Project Skeleton

**Files:**
- Modify: `GymAppApi.slnx`
- Create: `Core/GymAppApi.Domain/GymAppApi.Domain.csproj`
- Create: `Core/GymAppApi.Application/GymAppApi.Application.csproj`
- Create: `Infrastructure/GymAppApi.Persistence/GymAppApi.Persistence.csproj`
- Create: `Infrastructure/GymAppApi.Infrastructure/GymAppApi.Infrastructure.csproj`
- Create: `Presentation/GymAppApi.WebApi/GymAppApi.WebApi.csproj`
- Create: `Tests/GymAppApi.UnitTests/GymAppApi.UnitTests.csproj`
- Create: `Tests/GymAppApi.IntegrationTests/GymAppApi.IntegrationTests.csproj`
- Create: `.gitignore`

- [ ] **Step 1: Add a standard .NET `.gitignore`**

Run:
```bash
cd "C:\Users\MBEYAZBULUT\Desktop\GymAppApi"
dotnet new gitignore
```
Expected: `.gitignore` created covering `bin/`, `obj/`, `.vs/`.

- [ ] **Step 2: Create the class library / web api / test projects**

Run:
```bash
dotnet new classlib -n GymAppApi.Domain -o Core/GymAppApi.Domain --framework net10.0
dotnet new classlib -n GymAppApi.Application -o Core/GymAppApi.Application --framework net10.0
dotnet new classlib -n GymAppApi.Persistence -o Infrastructure/GymAppApi.Persistence --framework net10.0
dotnet new classlib -n GymAppApi.Infrastructure -o Infrastructure/GymAppApi.Infrastructure --framework net10.0
dotnet new webapi -n GymAppApi.WebApi -o Presentation/GymAppApi.WebApi --framework net10.0 -controllers
dotnet new xunit -n GymAppApi.UnitTests -o Tests/GymAppApi.UnitTests --framework net10.0
dotnet new xunit -n GymAppApi.IntegrationTests -o Tests/GymAppApi.IntegrationTests --framework net10.0
```
Expected: seven project folders created, each with a default `Class1.cs`/`UnitTest1.cs`/`Program.cs` — delete the placeholder source files (`Class1.cs`, `UnitTest1.cs`) in every project except `WebApi` (keep its `Program.cs`, we edit it in Task 10).

- [ ] **Step 3: Wire project references (Onion dependency direction)**

Run:
```bash
dotnet add Core/GymAppApi.Application/GymAppApi.Application.csproj reference Core/GymAppApi.Domain/GymAppApi.Domain.csproj
dotnet add Infrastructure/GymAppApi.Persistence/GymAppApi.Persistence.csproj reference Core/GymAppApi.Application/GymAppApi.Application.csproj Core/GymAppApi.Domain/GymAppApi.Domain.csproj
dotnet add Infrastructure/GymAppApi.Infrastructure/GymAppApi.Infrastructure.csproj reference Core/GymAppApi.Application/GymAppApi.Application.csproj
dotnet add Presentation/GymAppApi.WebApi/GymAppApi.WebApi.csproj reference Infrastructure/GymAppApi.Persistence/GymAppApi.Persistence.csproj Infrastructure/GymAppApi.Infrastructure/GymAppApi.Infrastructure.csproj
dotnet add Tests/GymAppApi.UnitTests/GymAppApi.UnitTests.csproj reference Core/GymAppApi.Application/GymAppApi.Application.csproj Core/GymAppApi.Domain/GymAppApi.Domain.csproj
dotnet add Tests/GymAppApi.IntegrationTests/GymAppApi.IntegrationTests.csproj reference Infrastructure/GymAppApi.Persistence/GymAppApi.Persistence.csproj Core/GymAppApi.Application/GymAppApi.Application.csproj Core/GymAppApi.Domain/GymAppApi.Domain.csproj
```
Expected: no output errors. `GymAppApi.Domain` has **zero** project references (innermost layer).

- [ ] **Step 4: Add all projects to the solution**

Run:
```bash
dotnet sln GymAppApi.slnx add Core/GymAppApi.Domain/GymAppApi.Domain.csproj Core/GymAppApi.Application/GymAppApi.Application.csproj Infrastructure/GymAppApi.Persistence/GymAppApi.Persistence.csproj Infrastructure/GymAppApi.Infrastructure/GymAppApi.Infrastructure.csproj Presentation/GymAppApi.WebApi/GymAppApi.WebApi.csproj Tests/GymAppApi.UnitTests/GymAppApi.UnitTests.csproj Tests/GymAppApi.IntegrationTests/GymAppApi.IntegrationTests.csproj
dotnet build
```
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 5: Commit**

```bash
git add .gitignore GymAppApi.slnx Core Infrastructure Presentation Tests
git commit -m "Scaffold Onion Architecture solution skeleton"
```

---

## Task 2: Domain Layer — Common Abstractions & Enums

**Files:**
- Create: `Core/GymAppApi.Domain/Common/IEntityBase.cs`
- Create: `Core/GymAppApi.Domain/Common/EntityBase.cs`
- Create: `Core/GymAppApi.Domain/Common/ICompanyScoped.cs`
- Create: `Core/GymAppApi.Domain/Common/ITenantScoped.cs`
- Create: `Core/GymAppApi.Domain/Common/IDeactivatable.cs`
- Create: `Core/GymAppApi.Domain/Enums/AssignmentRole.cs`
- Create: `Core/GymAppApi.Domain/Enums/Gender.cs`
- Create: `Core/GymAppApi.Domain/Enums/OtpPurpose.cs`
- Create: `Core/GymAppApi.Domain/Enums/DevicePlatform.cs`
- Test: `Tests/GymAppApi.UnitTests/Domain/EntityBaseTests.cs`

- [ ] **Step 1: Write the failing test for `EntityBase` default values**

```csharp
using GymAppApi.Domain.Common;
using Xunit;

namespace GymAppApi.UnitTests.Domain;

public class EntityBaseTests
{
    private class TestEntity : EntityBase { }

    [Fact]
    public void NewEntity_HasZeroId_AndNoTimestampsSet()
    {
        var entity = new TestEntity();

        Assert.Equal(0, entity.Id);
        Assert.Equal(default, entity.CreatedAt);
        Assert.Null(entity.UpdatedAt);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Tests/GymAppApi.UnitTests --filter EntityBaseTests`
Expected: FAIL — `GymAppApi.Domain.Common` namespace / `EntityBase` type not found (compile error).

- [ ] **Step 3: Implement Common abstractions**

```csharp
// Core/GymAppApi.Domain/Common/IEntityBase.cs
namespace GymAppApi.Domain.Common;

public interface IEntityBase
{
    int Id { get; set; }
}
```

```csharp
// Core/GymAppApi.Domain/Common/EntityBase.cs
namespace GymAppApi.Domain.Common;

public abstract class EntityBase : IEntityBase
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
```

```csharp
// Core/GymAppApi.Domain/Common/ICompanyScoped.cs
// Entities that always belong to exactly one Company (non-nullable FK) — the
// global query filter applies "WHERE CompanyId = @ambient" unconditionally.
namespace GymAppApi.Domain.Common;

public interface ICompanyScoped
{
    int CompanyId { get; set; }
}
```

```csharp
// Core/GymAppApi.Domain/Common/ITenantScoped.cs
// Entities whose tenant scope can legitimately be null (Assignment: a Super
// Admin's assignment has no CompanyId/BranchId; AuditLog: a platform-level
// action may have no branch). The query filter treats null as "not scoped
// at that level" rather than "belongs to nobody".
namespace GymAppApi.Domain.Common;

public interface ITenantScoped
{
    int? CompanyId { get; set; }
    int? BranchId { get; set; }
}
```

```csharp
// Core/GymAppApi.Domain/Common/IDeactivatable.cs
namespace GymAppApi.Domain.Common;

public interface IDeactivatable
{
    bool IsActive { get; set; }
}
```

```csharp
// Core/GymAppApi.Domain/Enums/AssignmentRole.cs
namespace GymAppApi.Domain.Enums;

public enum AssignmentRole
{
    SuperAdmin,
    GymAdmin,
    BranchManager,
    Trainer,
    Member
}
```

```csharp
// Core/GymAppApi.Domain/Enums/Gender.cs
namespace GymAppApi.Domain.Enums;

public enum Gender
{
    Unspecified,
    Male,
    Female
}
```

```csharp
// Core/GymAppApi.Domain/Enums/OtpPurpose.cs
namespace GymAppApi.Domain.Enums;

public enum OtpPurpose
{
    Login2FA,
    PhoneVerification,
    PasswordReset
}
```

```csharp
// Core/GymAppApi.Domain/Enums/DevicePlatform.cs
namespace GymAppApi.Domain.Enums;

public enum DevicePlatform
{
    iOS,
    Android
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Tests/GymAppApi.UnitTests --filter EntityBaseTests`
Expected: PASS — 1 test passed.

- [ ] **Step 5: Commit**

```bash
git add Core/GymAppApi.Domain/Common Core/GymAppApi.Domain/Enums Tests/GymAppApi.UnitTests/Domain
git commit -m "Add Domain common abstractions and enums"
```

---

## Task 3: Domain Layer — Identity & Tenancy Entities

**Files:**
- Create: `Core/GymAppApi.Domain/Entities/Company.cs`
- Create: `Core/GymAppApi.Domain/Entities/Branch.cs`
- Create: `Core/GymAppApi.Domain/Entities/User.cs`
- Create: `Core/GymAppApi.Domain/Entities/Assignment.cs`
- Create: `Core/GymAppApi.Domain/Entities/OtpVerification.cs`
- Create: `Core/GymAppApi.Domain/Entities/DeviceToken.cs`
- Create: `Core/GymAppApi.Domain/Entities/AuditLog.cs`
- Test: `Tests/GymAppApi.UnitTests/Domain/BranchTests.cs`

- [ ] **Step 1: Write the failing test for `Branch` tenant-scoping shape**

```csharp
using GymAppApi.Domain.Common;
using GymAppApi.Domain.Entities;
using Xunit;

namespace GymAppApi.UnitTests.Domain;

public class BranchTests
{
    [Fact]
    public void Branch_IsCompanyScopedAndDeactivatable()
    {
        var branch = new Branch { CompanyId = 1, Name = "Merkez Şube", Address = "..." };

        Assert.IsAssignableFrom<ICompanyScoped>(branch);
        Assert.IsAssignableFrom<IDeactivatable>(branch);
        Assert.True(branch.IsActive); // default must be active
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Tests/GymAppApi.UnitTests --filter BranchTests`
Expected: FAIL — `GymAppApi.Domain.Entities` namespace / `Branch` type not found.

- [ ] **Step 3: Implement the entities**

```csharp
// Core/GymAppApi.Domain/Entities/Company.cs
using GymAppApi.Domain.Common;

namespace GymAppApi.Domain.Entities;

public class Company : EntityBase, IDeactivatable
{
    public string Name { get; set; } = null!;
    public bool IsActive { get; set; } = true;

    public ICollection<Branch> Branches { get; set; } = new List<Branch>();
}
```

```csharp
// Core/GymAppApi.Domain/Entities/Branch.cs
using GymAppApi.Domain.Common;

namespace GymAppApi.Domain.Entities;

public class Branch : EntityBase, ICompanyScoped, IDeactivatable
{
    public int CompanyId { get; set; }
    public Company? Company { get; set; }

    public string Name { get; set; } = null!;
    public string Address { get; set; } = null!;
    public bool IsActive { get; set; } = true;
}
```

```csharp
// Core/GymAppApi.Domain/Entities/User.cs
using GymAppApi.Domain.Common;
using GymAppApi.Domain.Enums;

namespace GymAppApi.Domain.Entities;

// Deliberately NOT built on ASP.NET Core Identity: Identity's role model is
// one-set-of-roles-per-user, but a User here can hold a different Role per
// Company/Branch via Assignment. Password hashing still reuses
// Microsoft.AspNetCore.Identity.PasswordHasher<T> (Infrastructure layer) —
// only the Identity *data model* is skipped, not its hashing algorithm.
public class User : EntityBase
{
    public string Phone { get; set; } = null!;
    public string? Email { get; set; }
    public string PasswordHash { get; set; } = null!;
    public string FullName { get; set; } = null!;
    public Gender Gender { get; set; } = Gender.Unspecified;
    public string PreferredLanguage { get; set; } = "tr";
    public bool PhoneVerified { get; set; }

    public ICollection<Assignment> Assignments { get; set; } = new List<Assignment>();
}
```

```csharp
// Core/GymAppApi.Domain/Entities/Assignment.cs
using GymAppApi.Domain.Common;
using GymAppApi.Domain.Enums;

namespace GymAppApi.Domain.Entities;

// CompanyId/BranchId are nullable by design: a SuperAdmin assignment has
// both null (platform-wide), a GymAdmin assignment has CompanyId set and
// BranchId null (all branches of that company).
public class Assignment : EntityBase, ITenantScoped
{
    public int UserId { get; set; }
    public User? User { get; set; }

    public int? CompanyId { get; set; }
    public Company? Company { get; set; }

    public int? BranchId { get; set; }
    public Branch? Branch { get; set; }

    public AssignmentRole Role { get; set; }
    public bool IsActive { get; set; } = true;
}
```

```csharp
// Core/GymAppApi.Domain/Entities/OtpVerification.cs
using GymAppApi.Domain.Common;
using GymAppApi.Domain.Enums;

namespace GymAppApi.Domain.Entities;

public class OtpVerification : EntityBase
{
    public int UserId { get; set; }
    public User? User { get; set; }

    public string Code { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
    public OtpPurpose Purpose { get; set; }
    public bool IsUsed { get; set; }
    public int AttemptCount { get; set; }
}
```

```csharp
// Core/GymAppApi.Domain/Entities/DeviceToken.cs
using GymAppApi.Domain.Common;
using GymAppApi.Domain.Enums;

namespace GymAppApi.Domain.Entities;

public class DeviceToken : EntityBase
{
    public int UserId { get; set; }
    public User? User { get; set; }

    public string Token { get; set; } = null!;
    public DevicePlatform Platform { get; set; }
}
```

```csharp
// Core/GymAppApi.Domain/Entities/AuditLog.cs
using GymAppApi.Domain.Common;

namespace GymAppApi.Domain.Entities;

public class AuditLog : EntityBase, ITenantScoped
{
    public int? CompanyId { get; set; }
    public int? BranchId { get; set; }

    public int? ActorAssignmentId { get; set; }
    public string Action { get; set; } = null!;
    public string EntityType { get; set; } = null!;
    public int EntityId { get; set; }
    public string? DetailsJson { get; set; }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Tests/GymAppApi.UnitTests --filter BranchTests`
Expected: PASS — 1 test passed.

- [ ] **Step 5: Commit**

```bash
git add Core/GymAppApi.Domain/Entities Tests/GymAppApi.UnitTests/Domain/BranchTests.cs
git commit -m "Add Identity and Tenancy domain entities"
```

---

## Task 4: Application Layer — Repository/UnitOfWork/TenantContext Interfaces

**Files:**
- Create: `Core/GymAppApi.Application/Common/Interfaces/IReadRepository.cs`
- Create: `Core/GymAppApi.Application/Common/Interfaces/IWriteRepository.cs`
- Create: `Core/GymAppApi.Application/Common/Interfaces/IUnitOfWork.cs`
- Create: `Core/GymAppApi.Application/Common/Interfaces/ITenantContext.cs`
- Modify: `Core/GymAppApi.Application/GymAppApi.Application.csproj`

- [ ] **Step 1: Add the EF Core abstractions package needed for `IIncludableQueryable` in the repository signature**

Run:
```bash
dotnet add Core/GymAppApi.Application/GymAppApi.Application.csproj package Microsoft.EntityFrameworkCore.Abstractions
```
Expected: package reference added to `GymAppApi.Application.csproj`.

- [ ] **Step 2: Define `IReadRepository<T>`**

```csharp
// Core/GymAppApi.Application/Common/Interfaces/IReadRepository.cs
using System.Linq.Expressions;
using GymAppApi.Domain.Common;
using Microsoft.EntityFrameworkCore.Query;

namespace GymAppApi.Application.Common.Interfaces;

public interface IReadRepository<T> where T : class, IEntityBase
{
    Task<T?> GetAsync(
        Expression<Func<T, bool>> predicate,
        Func<IQueryable<T>, IIncludableQueryable<T, object>>? include = null,
        bool enableTracking = false,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<T>> GetAllAsync(
        Expression<Func<T, bool>>? predicate = null,
        Func<IQueryable<T>, IIncludableQueryable<T, object>>? include = null,
        Func<IQueryable<T>, IOrderedQueryable<T>>? orderBy = null,
        bool enableTracking = false,
        CancellationToken cancellationToken = default);

    Task<bool> AnyAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);

    Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 3: Define `IWriteRepository<T>`**

```csharp
// Core/GymAppApi.Application/Common/Interfaces/IWriteRepository.cs
using GymAppApi.Domain.Common;

namespace GymAppApi.Application.Common.Interfaces;

public interface IWriteRepository<T> where T : class, IEntityBase
{
    Task AddAsync(T entity, CancellationToken cancellationToken = default);
    void Update(T entity);
    void Remove(T entity);
}
```

- [ ] **Step 4: Define `IUnitOfWork`**

```csharp
// Core/GymAppApi.Application/Common/Interfaces/IUnitOfWork.cs
using GymAppApi.Domain.Common;

namespace GymAppApi.Application.Common.Interfaces;

public interface IUnitOfWork
{
    IReadRepository<T> GetReadRepository<T>() where T : class, IEntityBase;
    IWriteRepository<T> GetWriteRepository<T>() where T : class, IEntityBase;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    // Used by TransactionBehavior (Task 5) for commands that must run
    // read-check + write + save as one atomic unit (e.g. freeze-day-limit
    // checks in a later plan) — not needed by the Branch slice in this plan.
    Task<IDisposable> BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);
}
```

- [ ] **Step 5: Define `ITenantContext`**

```csharp
// Core/GymAppApi.Application/Common/Interfaces/ITenantContext.cs
namespace GymAppApi.Application.Common.Interfaces;

// Populated from JWT claims by WebApi middleware in a later plan (Auth).
// Until then, GymAppApi.Infrastructure ships an AmbientTenantContext stub
// that always reports IsSuperAdmin = true (no filtering) so the Branch
// vertical slice in this plan is testable without auth.
public interface ITenantContext
{
    int? CompanyId { get; }
    int? BranchId { get; }
    bool IsSuperAdmin { get; }
}
```

- [ ] **Step 6: Build to confirm the Application layer compiles**

Run: `dotnet build Core/GymAppApi.Application`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 7: Commit**

```bash
git add Core/GymAppApi.Application/Common/Interfaces Core/GymAppApi.Application/GymAppApi.Application.csproj
git commit -m "Add Repository, UnitOfWork and TenantContext interfaces"
```

---

## Task 5: Application Layer — MediatR Pipeline Behaviors

**Files:**
- Modify: `Core/GymAppApi.Application/GymAppApi.Application.csproj`
- Create: `Core/GymAppApi.Application/Common/Behaviors/ValidationBehavior.cs`
- Create: `Core/GymAppApi.Application/Common/Behaviors/ITransactionalRequest.cs`
- Create: `Core/GymAppApi.Application/Common/Behaviors/TransactionBehavior.cs`
- Create: `Core/GymAppApi.Application/Common/Exceptions/BaseException.cs`
- Create: `Core/GymAppApi.Application/Common/Exceptions/NotFoundException.cs`
- Create: `Core/GymAppApi.Application/Common/Exceptions/ConflictException.cs`
- Test: `Tests/GymAppApi.UnitTests/Behaviors/ValidationBehaviorTests.cs`

- [ ] **Step 1: Add MediatR + FluentValidation packages**

Run:
```bash
dotnet add Core/GymAppApi.Application/GymAppApi.Application.csproj package MediatR
dotnet add Core/GymAppApi.Application/GymAppApi.Application.csproj package FluentValidation
dotnet add Core/GymAppApi.Application/GymAppApi.Application.csproj package FluentValidation.DependencyInjectionExtensions
dotnet add Tests/GymAppApi.UnitTests/GymAppApi.UnitTests.csproj package Moq
```
Expected: packages restored, no errors.
> **Note (license):** MediatR (Jimmy Bogard) moved to a commercial license for larger organizations starting with v13 — free tier exists for small teams/revenue. Verify current terms at https://github.com/jbogard/MediatR before shipping to production; if that becomes a blocker, the free source-generator alternative `Mediator` (martinothamar) is a drop-in-shaped replacement (same `IRequestHandler<TRequest,TResponse>` core interface family) and would only require re-pointing `Registration.cs`, not rewriting handlers.

- [ ] **Step 2: Write the failing test for `ValidationBehavior`**

```csharp
// Tests/GymAppApi.UnitTests/Behaviors/ValidationBehaviorTests.cs
using FluentValidation;
using FluentValidation.Results;
using GymAppApi.Application.Common.Behaviors;
using MediatR;
using Moq;
using Xunit;

namespace GymAppApi.UnitTests.Behaviors;

public class ValidationBehaviorTests
{
    public record Ping(string Name) : IRequest<string>;

    [Fact]
    public async Task Handle_WhenValidatorFails_ThrowsValidationException()
    {
        var validator = new Mock<IValidator<Ping>>();
        validator.Setup(v => v.ValidateAsync(It.IsAny<ValidationContext<Ping>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(new[] { new ValidationFailure("Name", "Name is required") }));

        var behavior = new ValidationBehavior<Ping, string>(new[] { validator.Object });

        await Assert.ThrowsAsync<ValidationException>(() =>
            behavior.Handle(new Ping(""), () => Task.FromResult("unused"), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenNoValidators_CallsNext()
    {
        var behavior = new ValidationBehavior<Ping, string>(Array.Empty<IValidator<Ping>>());

        var result = await behavior.Handle(new Ping("ok"), () => Task.FromResult("next-called"), CancellationToken.None);

        Assert.Equal("next-called", result);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test Tests/GymAppApi.UnitTests --filter ValidationBehaviorTests`
Expected: FAIL — `GymAppApi.Application.Common.Behaviors.ValidationBehavior` not found.

- [ ] **Step 4: Implement `ValidationBehavior` (correctly named — EnvanteriX's `Beheviors` folder/typo is not repeated)**

```csharp
// Core/GymAppApi.Application/Common/Behaviors/ValidationBehavior.cs
using FluentValidation;
using MediatR;

namespace GymAppApi.Application.Common.Behaviors;

public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators) => _validators = validators;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!_validators.Any())
        {
            return await next();
        }

        var context = new ValidationContext<TRequest>(request);
        var results = await Task.WhenAll(_validators.Select(v => v.ValidateAsync(context, cancellationToken)));
        var failures = results.SelectMany(r => r.Errors).Where(f => f != null).ToList();

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        return await next();
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test Tests/GymAppApi.UnitTests --filter ValidationBehaviorTests`
Expected: PASS — 2 tests passed.

- [ ] **Step 6: Implement `ITransactionalRequest` marker + `TransactionBehavior`**

This is the gap the architecture audit flagged: EnvanteriX has no pipeline behavior that opens a real transaction, so concurrency-sensitive rules (checked in a later plan: freeze-day limits, class-capacity limits) would otherwise be read-check-then-write races. Commands opt in by implementing the marker interface; ordinary CRUD (like `CreateBranchCommand` in this plan) does not need it.

```csharp
// Core/GymAppApi.Application/Common/Behaviors/ITransactionalRequest.cs
namespace GymAppApi.Application.Common.Behaviors;

// Marker interface: implement on a Command whose Handler must run inside a
// single DB transaction (e.g. "read current total, assert under a limit,
// write" sequences that would otherwise race under concurrent requests).
public interface ITransactionalRequest
{
}
```

```csharp
// Core/GymAppApi.Application/Common/Behaviors/TransactionBehavior.cs
using GymAppApi.Application.Common.Interfaces;
using MediatR;

namespace GymAppApi.Application.Common.Behaviors;

public class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IUnitOfWork _unitOfWork;

    public TransactionBehavior(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is not ITransactionalRequest)
        {
            return await next();
        }

        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var response = await next();
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
            return response;
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }
}
```

> Note: `await using var transaction = ...BeginTransactionAsync(...)` requires `IDisposable` returned from `IUnitOfWork.BeginTransactionAsync` to also be awaitable-disposable; Task 8's `UnitOfWork` implementation returns EF Core's `IDbContextTransaction` (which is `IAsyncDisposable`) cast through `IDisposable` — adjust the interface to `Task<IAsyncDisposable>` there. **Correction applied directly in Task 4 Step 4 impact:** update `IUnitOfWork.BeginTransactionAsync` return type to `Task<IAsyncDisposable>` (not `Task<IDisposable>`) before implementing Task 8 — do this now:

- [ ] **Step 6a: Fix `IUnitOfWork.BeginTransactionAsync` return type to `IAsyncDisposable`**

In `Core/GymAppApi.Application/Common/Interfaces/IUnitOfWork.cs`, change:
```csharp
Task<IDisposable> BeginTransactionAsync(CancellationToken cancellationToken = default);
```
to:
```csharp
Task<IAsyncDisposable> BeginTransactionAsync(CancellationToken cancellationToken = default);
```
And in `TransactionBehavior.cs`, the `await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);` line is unchanged (works with both), so no further edit needed there.

- [ ] **Step 7: Implement `BaseException` with a `StatusCode` (fixes EnvanteriX's NotFound→500 bug)**

```csharp
// Core/GymAppApi.Application/Common/Exceptions/BaseException.cs
using System.Net;

namespace GymAppApi.Application.Common.Exceptions;

public abstract class BaseException : Exception
{
    public abstract HttpStatusCode StatusCode { get; }

    protected BaseException(string message) : base(message) { }
}
```

```csharp
// Core/GymAppApi.Application/Common/Exceptions/NotFoundException.cs
using System.Net;

namespace GymAppApi.Application.Common.Exceptions;

public class NotFoundException : BaseException
{
    public override HttpStatusCode StatusCode => HttpStatusCode.NotFound;

    public NotFoundException(string message) : base(message) { }
}
```

```csharp
// Core/GymAppApi.Application/Common/Exceptions/ConflictException.cs
using System.Net;

namespace GymAppApi.Application.Common.Exceptions;

public class ConflictException : BaseException
{
    public override HttpStatusCode StatusCode => HttpStatusCode.Conflict;

    public ConflictException(string message) : base(message) { }
}
```

- [ ] **Step 8: Build to confirm everything compiles**

Run: `dotnet build Core/GymAppApi.Application`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 9: Commit**

```bash
git add Core/GymAppApi.Application Tests/GymAppApi.UnitTests/Behaviors
git commit -m "Add MediatR validation/transaction behaviors and typed exceptions"
```

---

## Task 6: Application Layer — DI Registration

**Files:**
- Create: `Core/GymAppApi.Application/Registration.cs`

- [ ] **Step 1: Write `Registration.cs` (matches EnvanteriX's per-layer `Add<Layer>()` extension-method convention)**

```csharp
// Core/GymAppApi.Application/Registration.cs
using System.Reflection;
using FluentValidation;
using GymAppApi.Application.Common.Behaviors;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace GymAppApi.Application;

public static class Registration
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly));
        services.AddValidatorsFromAssembly(assembly);

        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        return services;
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build Core/GymAppApi.Application`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add Core/GymAppApi.Application/Registration.cs
git commit -m "Add Application layer DI registration"
```

---

## Task 7: Persistence Layer — DbContext with Global Query Filters + Save Stamping

**Files:**
- Modify: `Infrastructure/GymAppApi.Persistence/GymAppApi.Persistence.csproj`
- Create: `Infrastructure/GymAppApi.Persistence/Context/GymAppApiDbContext.cs`
- Test: `Tests/GymAppApi.IntegrationTests/GymAppApi.IntegrationTests.csproj` (package refs)
- Test: `Tests/GymAppApi.IntegrationTests/TenantQueryFilterTests.cs`

- [ ] **Step 1: Add EF Core + Npgsql + InMemory (tests only) packages**

Run:
```bash
dotnet add Infrastructure/GymAppApi.Persistence/GymAppApi.Persistence.csproj package Microsoft.EntityFrameworkCore
dotnet add Infrastructure/GymAppApi.Persistence/GymAppApi.Persistence.csproj package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add Infrastructure/GymAppApi.Persistence/GymAppApi.Persistence.csproj package Microsoft.EntityFrameworkCore.Design
dotnet add Tests/GymAppApi.IntegrationTests/GymAppApi.IntegrationTests.csproj package Microsoft.EntityFrameworkCore.InMemory
```
Expected: packages restored.

- [ ] **Step 2: Write the failing integration test for the Company-scoped global filter**

```csharp
// Tests/GymAppApi.IntegrationTests/TenantQueryFilterTests.cs
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GymAppApi.IntegrationTests;

public class FakeTenantContext : ITenantContext
{
    public int? CompanyId { get; set; }
    public int? BranchId { get; set; }
    public bool IsSuperAdmin { get; set; }
}

public class TenantQueryFilterTests
{
    private static GymAppApiDbContext CreateContext(FakeTenantContext tenantContext, string dbName)
    {
        var options = new DbContextOptionsBuilder<GymAppApiDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new GymAppApiDbContext(options, tenantContext);
    }

    [Fact]
    public async Task NonSuperAdmin_OnlySeesOwnCompanyBranches()
    {
        var dbName = Guid.NewGuid().ToString();
        var seedTenant = new FakeTenantContext { IsSuperAdmin = true };
        await using (var seedContext = CreateContext(seedTenant, dbName))
        {
            seedContext.Companies.AddRange(
                new Company { Name = "Company A", IsActive = true },
                new Company { Name = "Company B", IsActive = true });
            await seedContext.SaveChangesAsync();

            var companyA = seedContext.Companies.Single(c => c.Name == "Company A");
            var companyB = seedContext.Companies.Single(c => c.Name == "Company B");

            seedContext.Branches.AddRange(
                new Branch { CompanyId = companyA.Id, Name = "A - Merkez", Address = "..." },
                new Branch { CompanyId = companyB.Id, Name = "B - Merkez", Address = "..." });
            await seedContext.SaveChangesAsync();
        }

        var companyAId = 1;
        var scopedTenant = new FakeTenantContext { IsSuperAdmin = false, CompanyId = companyAId };
        await using var scopedContext = CreateContext(scopedTenant, dbName);

        var visibleBranches = await scopedContext.Branches.ToListAsync();

        Assert.Single(visibleBranches);
        Assert.Equal("A - Merkez", visibleBranches[0].Name);
    }

    [Fact]
    public async Task SuperAdmin_SeesAllBranches()
    {
        var dbName = Guid.NewGuid().ToString();
        var superAdminTenant = new FakeTenantContext { IsSuperAdmin = true };
        await using (var seedContext = CreateContext(superAdminTenant, dbName))
        {
            var company = new Company { Name = "Company A", IsActive = true };
            seedContext.Companies.Add(company);
            await seedContext.SaveChangesAsync();

            seedContext.Branches.AddRange(
                new Branch { CompanyId = company.Id, Name = "Şube 1", Address = "..." },
                new Branch { CompanyId = company.Id, Name = "Şube 2", Address = "..." });
            await seedContext.SaveChangesAsync();
        }

        await using var readContext = CreateContext(superAdminTenant, dbName);
        var branches = await readContext.Branches.ToListAsync();

        Assert.Equal(2, branches.Count);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Tests/GymAppApi.IntegrationTests --filter TenantQueryFilterTests`
Expected: FAIL — `GymAppApi.Persistence.Context.GymAppApiDbContext` not found.

- [ ] **Step 3: Implement `GymAppApiDbContext`**

```csharp
// Infrastructure/GymAppApi.Persistence/Context/GymAppApiDbContext.cs
using System.Reflection;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Common;
using GymAppApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Persistence.Context;

public class GymAppApiDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;

    public GymAppApiDbContext(DbContextOptions<GymAppApiDbContext> options, ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<OtpVerification> OtpVerifications => Set<OtpVerification>();
    public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        ApplyGlobalQueryFilters(modelBuilder);
    }

    // EF Core forbids a generic HasQueryFilter call without a concrete
    // entity type at compile time, so per-entity-type filters are applied
    // via reflection over the model — this loop is the piece EnvanteriX has
    // no equivalent of at all.
    private void ApplyGlobalQueryFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;

            if (typeof(Company).IsAssignableFrom(clrType))
            {
                var method = GetType().GetMethod(nameof(SetCompanySelfFilter), BindingFlags.NonPublic | BindingFlags.Instance)!
                    .MakeGenericMethod(clrType);
                method.Invoke(this, new object[] { modelBuilder });
            }
            else if (typeof(ICompanyScoped).IsAssignableFrom(clrType) && typeof(IDeactivatable).IsAssignableFrom(clrType))
            {
                var method = GetType().GetMethod(nameof(SetCompanyScopedDeactivatableFilter), BindingFlags.NonPublic | BindingFlags.Instance)!
                    .MakeGenericMethod(clrType);
                method.Invoke(this, new object[] { modelBuilder });
            }
            else if (typeof(ICompanyScoped).IsAssignableFrom(clrType))
            {
                var method = GetType().GetMethod(nameof(SetCompanyScopedFilter), BindingFlags.NonPublic | BindingFlags.Instance)!
                    .MakeGenericMethod(clrType);
                method.Invoke(this, new object[] { modelBuilder });
            }
            else if (typeof(ITenantScoped).IsAssignableFrom(clrType))
            {
                var method = GetType().GetMethod(nameof(SetNullableTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!
                    .MakeGenericMethod(clrType);
                method.Invoke(this, new object[] { modelBuilder });
            }
        }
    }

    private void SetCompanySelfFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, IDeactivatable, IEntityBase
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(e =>
            _tenantContext.IsSuperAdmin ||
            ((_tenantContext.CompanyId == null || e.Id == _tenantContext.CompanyId) && e.IsActive));
    }

    private void SetCompanyScopedFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ICompanyScoped
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(e =>
            _tenantContext.IsSuperAdmin || _tenantContext.CompanyId == null || e.CompanyId == _tenantContext.CompanyId);
    }

    private void SetCompanyScopedDeactivatableFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ICompanyScoped, IDeactivatable
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(e =>
            (_tenantContext.IsSuperAdmin || _tenantContext.CompanyId == null || e.CompanyId == _tenantContext.CompanyId) &&
            (e.IsActive || _tenantContext.IsSuperAdmin));
    }

    private void SetNullableTenantFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ITenantScoped
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(e =>
            _tenantContext.IsSuperAdmin ||
            ((e.CompanyId == null || e.CompanyId == _tenantContext.CompanyId) &&
             (e.BranchId == null || _tenantContext.BranchId == null || e.BranchId == _tenantContext.BranchId)));
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<EntityBase>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<ICompanyScoped>())
        {
            if (entry.State == EntityState.Added && entry.Entity.CompanyId == 0 && _tenantContext.CompanyId.HasValue)
            {
                entry.Entity.CompanyId = _tenantContext.CompanyId.Value;
            }
        }

        return await base.SaveChangesAsync(cancellationToken);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Tests/GymAppApi.IntegrationTests --filter TenantQueryFilterTests`
Expected: PASS — 2 tests passed.

- [ ] **Step 5: Commit**

```bash
git add Infrastructure/GymAppApi.Persistence Tests/GymAppApi.IntegrationTests
git commit -m "Add DbContext with multi-tenant global query filters and save stamping"
```

---

## Task 8: Persistence Layer — EF Configurations

**Files:**
- Create: `Infrastructure/GymAppApi.Persistence/Configurations/CompanyConfiguration.cs`
- Create: `Infrastructure/GymAppApi.Persistence/Configurations/BranchConfiguration.cs`
- Create: `Infrastructure/GymAppApi.Persistence/Configurations/UserConfiguration.cs`
- Create: `Infrastructure/GymAppApi.Persistence/Configurations/AssignmentConfiguration.cs`
- Create: `Infrastructure/GymAppApi.Persistence/Configurations/OtpVerificationConfiguration.cs`
- Create: `Infrastructure/GymAppApi.Persistence/Configurations/DeviceTokenConfiguration.cs`
- Create: `Infrastructure/GymAppApi.Persistence/Configurations/AuditLogConfiguration.cs`

- [ ] **Step 1: Write all seven `IEntityTypeConfiguration<T>` classes**

```csharp
// Infrastructure/GymAppApi.Persistence/Configurations/CompanyConfiguration.cs
using GymAppApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymAppApi.Persistence.Configurations;

public class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> builder)
    {
        builder.Property(x => x.Name).IsRequired().HasMaxLength(200);
        builder.Property(x => x.IsActive).HasDefaultValue(true);
    }
}
```

```csharp
// Infrastructure/GymAppApi.Persistence/Configurations/BranchConfiguration.cs
using GymAppApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymAppApi.Persistence.Configurations;

public class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> builder)
    {
        builder.Property(x => x.Name).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Address).IsRequired().HasMaxLength(500);
        builder.Property(x => x.IsActive).HasDefaultValue(true);

        builder.HasOne(x => x.Company)
            .WithMany(c => c.Branches)
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.CompanyId);
    }
}
```

```csharp
// Infrastructure/GymAppApi.Persistence/Configurations/UserConfiguration.cs
using GymAppApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymAppApi.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.Property(x => x.Phone).IsRequired().HasMaxLength(20);
        builder.Property(x => x.Email).HasMaxLength(200);
        builder.Property(x => x.PasswordHash).IsRequired();
        builder.Property(x => x.FullName).IsRequired().HasMaxLength(200);
        builder.Property(x => x.PreferredLanguage).IsRequired().HasMaxLength(10).HasDefaultValue("tr");

        builder.HasIndex(x => x.Phone).IsUnique();
    }
}
```

```csharp
// Infrastructure/GymAppApi.Persistence/Configurations/AssignmentConfiguration.cs
using GymAppApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymAppApi.Persistence.Configurations;

public class AssignmentConfiguration : IEntityTypeConfiguration<Assignment>
{
    public void Configure(EntityTypeBuilder<Assignment> builder)
    {
        builder.Property(x => x.Role).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.IsActive).HasDefaultValue(true);

        builder.HasOne(x => x.User)
            .WithMany(u => u.Assignments)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Branch)
            .WithMany()
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.CompanyId, x.BranchId });
        builder.HasIndex(x => x.UserId);
    }
}
```

```csharp
// Infrastructure/GymAppApi.Persistence/Configurations/OtpVerificationConfiguration.cs
using GymAppApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymAppApi.Persistence.Configurations;

public class OtpVerificationConfiguration : IEntityTypeConfiguration<OtpVerification>
{
    public void Configure(EntityTypeBuilder<OtpVerification> builder)
    {
        builder.Property(x => x.Code).IsRequired().HasMaxLength(10);
        builder.Property(x => x.Purpose).HasConversion<string>().HasMaxLength(30);

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.UserId, x.Purpose });
    }
}
```

```csharp
// Infrastructure/GymAppApi.Persistence/Configurations/DeviceTokenConfiguration.cs
using GymAppApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymAppApi.Persistence.Configurations;

public class DeviceTokenConfiguration : IEntityTypeConfiguration<DeviceToken>
{
    public void Configure(EntityTypeBuilder<DeviceToken> builder)
    {
        builder.Property(x => x.Token).IsRequired().HasMaxLength(500);
        builder.Property(x => x.Platform).HasConversion<string>().HasMaxLength(20);

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.Token).IsUnique();
    }
}
```

```csharp
// Infrastructure/GymAppApi.Persistence/Configurations/AuditLogConfiguration.cs
using GymAppApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymAppApi.Persistence.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.Property(x => x.Action).IsRequired().HasMaxLength(100);
        builder.Property(x => x.EntityType).IsRequired().HasMaxLength(100);
        builder.Property(x => x.DetailsJson).HasColumnType("jsonb");

        builder.HasIndex(x => new { x.CompanyId, x.BranchId });
        builder.HasIndex(x => new { x.EntityType, x.EntityId });
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build Infrastructure/GymAppApi.Persistence`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add Infrastructure/GymAppApi.Persistence/Configurations
git commit -m "Add EF Core entity configurations for Identity and Tenancy tables"
```

---

## Task 9: Persistence Layer — Repository + UnitOfWork Implementation, DI, Migration

**Files:**
- Create: `Infrastructure/GymAppApi.Persistence/Repositories/ReadRepository.cs`
- Create: `Infrastructure/GymAppApi.Persistence/Repositories/WriteRepository.cs`
- Create: `Infrastructure/GymAppApi.Persistence/UnitOfWork/UnitOfWork.cs`
- Create: `Infrastructure/GymAppApi.Persistence/Registration.cs`
- Create: `Infrastructure/GymAppApi.Persistence/Migrations/*` (generated)

- [ ] **Step 1: Implement `ReadRepository<T>` (AsNoTracking by default, matching EnvanteriX's proven default)**

```csharp
// Infrastructure/GymAppApi.Persistence/Repositories/ReadRepository.cs
using System.Linq.Expressions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Common;
using GymAppApi.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace GymAppApi.Persistence.Repositories;

public class ReadRepository<T> : IReadRepository<T> where T : class, IEntityBase
{
    private readonly GymAppApiDbContext _context;

    public ReadRepository(GymAppApiDbContext context) => _context = context;

    private IQueryable<T> Table => _context.Set<T>();

    public async Task<T?> GetAsync(
        Expression<Func<T, bool>> predicate,
        Func<IQueryable<T>, IIncludableQueryable<T, object>>? include = null,
        bool enableTracking = false,
        CancellationToken cancellationToken = default)
    {
        IQueryable<T> query = enableTracking ? Table : Table.AsNoTracking();
        if (include != null) query = include(query);
        return await query.FirstOrDefaultAsync(predicate, cancellationToken);
    }

    public async Task<IReadOnlyList<T>> GetAllAsync(
        Expression<Func<T, bool>>? predicate = null,
        Func<IQueryable<T>, IIncludableQueryable<T, object>>? include = null,
        Func<IQueryable<T>, IOrderedQueryable<T>>? orderBy = null,
        bool enableTracking = false,
        CancellationToken cancellationToken = default)
    {
        IQueryable<T> query = enableTracking ? Table : Table.AsNoTracking();
        if (predicate != null) query = query.Where(predicate);
        if (include != null) query = include(query);
        if (orderBy != null) query = orderBy(query);
        return await query.ToListAsync(cancellationToken);
    }

    public async Task<bool> AnyAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        => await Table.AnyAsync(predicate, cancellationToken);

    public async Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default)
        => predicate == null
            ? await Table.CountAsync(cancellationToken)
            : await Table.CountAsync(predicate, cancellationToken);
}
```

- [ ] **Step 2: Implement `WriteRepository<T>`**

```csharp
// Infrastructure/GymAppApi.Persistence/Repositories/WriteRepository.cs
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Common;
using GymAppApi.Persistence.Context;

namespace GymAppApi.Persistence.Repositories;

public class WriteRepository<T> : IWriteRepository<T> where T : class, IEntityBase
{
    private readonly GymAppApiDbContext _context;

    public WriteRepository(GymAppApiDbContext context) => _context = context;

    public async Task AddAsync(T entity, CancellationToken cancellationToken = default)
        => await _context.Set<T>().AddAsync(entity, cancellationToken);

    public void Update(T entity) => _context.Set<T>().Update(entity);

    public void Remove(T entity) => _context.Set<T>().Remove(entity);
}
```

- [ ] **Step 3: Implement `UnitOfWork`**

```csharp
// Infrastructure/GymAppApi.Persistence/UnitOfWork/UnitOfWork.cs
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Common;
using GymAppApi.Persistence.Context;
using GymAppApi.Persistence.Repositories;

namespace GymAppApi.Persistence.UnitOfWork;

public class UnitOfWork : IUnitOfWork
{
    private readonly GymAppApiDbContext _context;

    public UnitOfWork(GymAppApiDbContext context) => _context = context;

    public IReadRepository<T> GetReadRepository<T>() where T : class, IEntityBase => new ReadRepository<T>(_context);

    public IWriteRepository<T> GetWriteRepository<T>() where T : class, IEntityBase => new WriteRepository<T>(_context);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => _context.SaveChangesAsync(cancellationToken);

    public async Task<IAsyncDisposable> BeginTransactionAsync(CancellationToken cancellationToken = default)
        => await _context.Database.BeginTransactionAsync(cancellationToken);

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
        => await _context.Database.CommitTransactionAsync(cancellationToken);

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
        => await _context.Database.RollbackTransactionAsync(cancellationToken);
}
```

- [ ] **Step 4: Implement `Registration.cs`**

```csharp
// Infrastructure/GymAppApi.Persistence/Registration.cs
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Persistence.Context;
using GymAppApi.Persistence.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GymAppApi.Persistence;

public static class Registration
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<GymAppApiDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));

        services.AddScoped(typeof(IReadRepository<>), typeof(GymAppApi.Persistence.Repositories.ReadRepository<>));
        services.AddScoped(typeof(IWriteRepository<>), typeof(GymAppApi.Persistence.Repositories.WriteRepository<>));
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        return services;
    }
}
```

- [ ] **Step 5: Install the EF Core CLI tool (if not already available) and generate the initial migration**

Run:
```bash
dotnet tool install --global dotnet-ef --version 10.*
dotnet ef migrations add InitialCreate --project Infrastructure/GymAppApi.Persistence --startup-project Presentation/GymAppApi.WebApi --output-dir Migrations
```
Expected: a `Migrations/` folder is created under `Infrastructure/GymAppApi.Persistence` with `InitialCreate.cs`, `InitialCreate.Designer.cs`, and `GymAppApiDbContextModelSnapshot.cs`. This step requires `Presentation/GymAppApi.WebApi` to already have a working `AddDbContext` registration reachable from `Program.cs` and a valid `appsettings.json` connection string — if this fails with "no design-time services", complete Task 10 (WebApi wiring) first, then return to this step.

- [ ] **Step 6: Commit**

```bash
git add Infrastructure/GymAppApi.Persistence
git commit -m "Add Repository/UnitOfWork implementations, Persistence DI, and initial migration"
```

---

## Task 10: Infrastructure Layer — TenantContext Stub + DI

**Files:**
- Create: `Infrastructure/GymAppApi.Infrastructure/Tenancy/AmbientTenantContext.cs`
- Create: `Infrastructure/GymAppApi.Infrastructure/Registration.cs`

- [ ] **Step 1: Implement `AmbientTenantContext`**

```csharp
// Infrastructure/GymAppApi.Infrastructure/Tenancy/AmbientTenantContext.cs
using GymAppApi.Application.Common.Interfaces;

namespace GymAppApi.Infrastructure.Tenancy;

// Placeholder until the Auth plan adds JWT + a middleware that reads
// CompanyId/BranchId/Role claims into a request-scoped implementation of
// this interface. Reporting IsSuperAdmin = true means every global query
// filter in Task 7 is a no-op for now — safe default for an
// unauthenticated foundation, NOT safe once real users exist.
public class AmbientTenantContext : ITenantContext
{
    public int? CompanyId => null;
    public int? BranchId => null;
    public bool IsSuperAdmin => true;
}
```

- [ ] **Step 2: Implement `Registration.cs`**

```csharp
// Infrastructure/GymAppApi.Infrastructure/Registration.cs
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Infrastructure.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace GymAppApi.Infrastructure;

public static class Registration
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<ITenantContext, AmbientTenantContext>();
        return services;
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build Infrastructure/GymAppApi.Infrastructure`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git add Infrastructure/GymAppApi.Infrastructure
git commit -m "Add Infrastructure layer tenant context stub and DI registration"
```

---

## Task 11: WebApi — Program.cs, Configuration, Exception Middleware

**Files:**
- Modify: `Presentation/GymAppApi.WebApi/Program.cs`
- Modify: `Presentation/GymAppApi.WebApi/appsettings.json`
- Modify: `Presentation/GymAppApi.WebApi/appsettings.Development.json`
- Create: `Presentation/GymAppApi.WebApi/Middleware/ExceptionMiddleware.cs`

- [ ] **Step 1: Set the PostgreSQL connection string via user-secrets (not committed to appsettings.json)**

Run:
```bash
cd Presentation/GymAppApi.WebApi
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=gymapp_dev;Username=postgres;Password=postgres"
cd ../..
```
Expected: a `UserSecretsId` is added to `GymAppApi.WebApi.csproj`, and the secret is stored outside the repo (`%APPDATA%\Microsoft\UserSecrets\<id>\secrets.json` on Windows).

- [ ] **Step 2: Leave a placeholder (no real value) in `appsettings.json` so the section is documented**

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "ConnectionStrings": {
    "DefaultConnection": ""
  }
}
```

- [ ] **Step 3: Implement `ExceptionMiddleware` (corrects EnvanteriX's NotFound→500 bug via `BaseException.StatusCode`)**

```csharp
// Presentation/GymAppApi.WebApi/Middleware/ExceptionMiddleware.cs
using System.Net;
using System.Text.Json;
using FluentValidation;
using GymAppApi.Application.Common.Exceptions;

namespace GymAppApi.WebApi.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, errors) = exception switch
        {
            ValidationException validationException => (
                (int)HttpStatusCode.UnprocessableEntity,
                validationException.Errors.Select(e => e.ErrorMessage)),
            BaseException baseException => (
                (int)baseException.StatusCode,
                new[] { baseException.Message }.AsEnumerable()),
            _ => ((int)HttpStatusCode.InternalServerError, new[] { "Beklenmeyen bir hata oluştu." }.AsEnumerable())
        };

        if (statusCode == (int)HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception");
        }

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = statusCode;

        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            Status = statusCode,
            Errors = errors
        }));
    }
}
```

- [ ] **Step 4: Wire everything in `Program.cs`**

```csharp
// Presentation/GymAppApi.WebApi/Program.cs
using GymAppApi.Application;
using GymAppApi.Infrastructure;
using GymAppApi.Persistence;
using GymAppApi.WebApi.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure();
builder.Services.AddApplication();
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddHttpContextAccessor();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<ExceptionMiddleware>();
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program { }
```

> `public partial class Program { }` at the bottom makes the WebAssembly-style minimal-API `Program` class visible to `WebApplicationFactory<Program>` in a later integration-test task — harmless no-op today, needed once controller integration tests are added.

- [ ] **Step 5: Build and run**

Run:
```bash
dotnet build Presentation/GymAppApi.WebApi
dotnet run --project Presentation/GymAppApi.WebApi
```
Expected: `Now listening on: https://localhost:....`; visiting `/swagger` in a browser shows an empty (no controllers yet) Swagger UI without errors. Stop with Ctrl+C.

- [ ] **Step 6: Commit**

```bash
git add Presentation/GymAppApi.WebApi
git commit -m "Wire WebApi Program.cs, exception middleware, and Swagger"
```

---

## Task 12: Vertical Slice Feature — Branch Create & List

**Files:**
- Create: `Core/GymAppApi.Application/Features/Branches/Exceptions/CompanyNotFoundException.cs`
- Create: `Core/GymAppApi.Application/Features/Branches/Rules/BranchRules.cs`
- Create: `Core/GymAppApi.Application/Features/Branches/Commands/CreateBranch/CreateBranchCommand.cs`
- Create: `Core/GymAppApi.Application/Features/Branches/Commands/CreateBranch/CreateBranchCommandValidator.cs`
- Create: `Core/GymAppApi.Application/Features/Branches/Commands/CreateBranch/CreateBranchCommandResult.cs`
- Create: `Core/GymAppApi.Application/Features/Branches/Commands/CreateBranch/CreateBranchCommandHandler.cs`
- Create: `Core/GymAppApi.Application/Features/Branches/Queries/GetBranches/GetBranchesQuery.cs`
- Create: `Core/GymAppApi.Application/Features/Branches/Queries/GetBranches/BranchListItemDto.cs`
- Create: `Core/GymAppApi.Application/Features/Branches/Queries/GetBranches/GetBranchesQueryHandler.cs`
- Create: `Presentation/GymAppApi.WebApi/Controllers/BranchesController.cs`
- Test: `Tests/GymAppApi.UnitTests/Features/Branches/CreateBranchCommandHandlerTests.cs`

- [ ] **Step 1: Write the failing handler test**

```csharp
// Tests/GymAppApi.UnitTests/Features/Branches/CreateBranchCommandHandlerTests.cs
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Branches.Commands.CreateBranch;
using GymAppApi.Application.Features.Branches.Exceptions;
using GymAppApi.Application.Features.Branches.Rules;
using GymAppApi.Domain.Entities;
using Moq;
using Xunit;

namespace GymAppApi.UnitTests.Features.Branches;

public class CreateBranchCommandHandlerTests
{
    [Fact]
    public async Task Handle_WhenCompanyDoesNotExist_ThrowsCompanyNotFoundException()
    {
        var readRepo = new Mock<IReadRepository<Company>>();
        readRepo.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Company, bool>>>(), default))
            .ReturnsAsync(false);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.GetReadRepository<Company>()).Returns(readRepo.Object);

        var handler = new CreateBranchCommandHandler(unitOfWork.Object, new BranchRules(unitOfWork.Object));
        var command = new CreateBranchCommand { CompanyId = 999, Name = "Şube", Address = "Adres" };

        await Assert.ThrowsAsync<CompanyNotFoundException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenCompanyExists_AddsBranchAndSaves()
    {
        var companyReadRepo = new Mock<IReadRepository<Company>>();
        companyReadRepo.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Company, bool>>>(), default))
            .ReturnsAsync(true);

        var branchWriteRepo = new Mock<IWriteRepository<Branch>>();

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.GetReadRepository<Company>()).Returns(companyReadRepo.Object);
        unitOfWork.Setup(u => u.GetWriteRepository<Branch>()).Returns(branchWriteRepo.Object);
        unitOfWork.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        var handler = new CreateBranchCommandHandler(unitOfWork.Object, new BranchRules(unitOfWork.Object));
        var command = new CreateBranchCommand { CompanyId = 1, Name = "Merkez Şube", Address = "Adres" };

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.Equal("Merkez Şube", result.Name);
        branchWriteRepo.Verify(r => r.AddAsync(It.Is<Branch>(b => b.Name == "Merkez Şube" && b.CompanyId == 1), default), Times.Once);
        unitOfWork.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Tests/GymAppApi.UnitTests --filter CreateBranchCommandHandlerTests`
Expected: FAIL — types under `GymAppApi.Application.Features.Branches.*` not found.

- [ ] **Step 3: Implement the exception and rules**

```csharp
// Core/GymAppApi.Application/Features/Branches/Exceptions/CompanyNotFoundException.cs
using GymAppApi.Application.Common.Exceptions;

namespace GymAppApi.Application.Features.Branches.Exceptions;

public class CompanyNotFoundException : NotFoundException
{
    public CompanyNotFoundException(int companyId) : base($"Company {companyId} bulunamadı.") { }
}
```

```csharp
// Core/GymAppApi.Application/Features/Branches/Rules/BranchRules.cs
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Branches.Exceptions;
using GymAppApi.Domain.Entities;

namespace GymAppApi.Application.Features.Branches.Rules;

public class BranchRules
{
    private readonly IUnitOfWork _unitOfWork;

    public BranchRules(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task CompanyMustExistAsync(int companyId, CancellationToken cancellationToken)
    {
        var exists = await _unitOfWork.GetReadRepository<Company>().AnyAsync(c => c.Id == companyId, cancellationToken);
        if (!exists)
        {
            throw new CompanyNotFoundException(companyId);
        }
    }
}
```

- [ ] **Step 4: Implement the Create command**

```csharp
// Core/GymAppApi.Application/Features/Branches/Commands/CreateBranch/CreateBranchCommand.cs
using MediatR;

namespace GymAppApi.Application.Features.Branches.Commands.CreateBranch;

public class CreateBranchCommand : IRequest<CreateBranchCommandResult>
{
    public int CompanyId { get; set; }
    public string Name { get; set; } = null!;
    public string Address { get; set; } = null!;
}
```

```csharp
// Core/GymAppApi.Application/Features/Branches/Commands/CreateBranch/CreateBranchCommandValidator.cs
using FluentValidation;

namespace GymAppApi.Application.Features.Branches.Commands.CreateBranch;

public class CreateBranchCommandValidator : AbstractValidator<CreateBranchCommand>
{
    public CreateBranchCommandValidator()
    {
        RuleFor(x => x.CompanyId).GreaterThan(0);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Address).NotEmpty().MaximumLength(500);
    }
}
```

```csharp
// Core/GymAppApi.Application/Features/Branches/Commands/CreateBranch/CreateBranchCommandResult.cs
namespace GymAppApi.Application.Features.Branches.Commands.CreateBranch;

public class CreateBranchCommandResult
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
}
```

```csharp
// Core/GymAppApi.Application/Features/Branches/Commands/CreateBranch/CreateBranchCommandHandler.cs
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Branches.Rules;
using GymAppApi.Domain.Entities;
using MediatR;

namespace GymAppApi.Application.Features.Branches.Commands.CreateBranch;

public class CreateBranchCommandHandler : IRequestHandler<CreateBranchCommand, CreateBranchCommandResult>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly BranchRules _branchRules;

    public CreateBranchCommandHandler(IUnitOfWork unitOfWork, BranchRules branchRules)
    {
        _unitOfWork = unitOfWork;
        _branchRules = branchRules;
    }

    public async Task<CreateBranchCommandResult> Handle(CreateBranchCommand request, CancellationToken cancellationToken)
    {
        await _branchRules.CompanyMustExistAsync(request.CompanyId, cancellationToken);

        var branch = new Branch
        {
            CompanyId = request.CompanyId,
            Name = request.Name,
            Address = request.Address
        };

        await _unitOfWork.GetWriteRepository<Branch>().AddAsync(branch, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CreateBranchCommandResult { Id = branch.Id, Name = branch.Name };
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test Tests/GymAppApi.UnitTests --filter CreateBranchCommandHandlerTests`
Expected: PASS — 2 tests passed.

- [ ] **Step 6: Implement the Get query (no test required — trivial pass-through, covered by the controller smoke-test in Step 8)**

```csharp
// Core/GymAppApi.Application/Features/Branches/Queries/GetBranches/GetBranchesQuery.cs
using MediatR;

namespace GymAppApi.Application.Features.Branches.Queries.GetBranches;

public class GetBranchesQuery : IRequest<IReadOnlyList<BranchListItemDto>>
{
}
```

```csharp
// Core/GymAppApi.Application/Features/Branches/Queries/GetBranches/BranchListItemDto.cs
namespace GymAppApi.Application.Features.Branches.Queries.GetBranches;

public class BranchListItemDto
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string Name { get; set; } = null!;
    public string Address { get; set; } = null!;
    public bool IsActive { get; set; }
}
```

```csharp
// Core/GymAppApi.Application/Features/Branches/Queries/GetBranches/GetBranchesQueryHandler.cs
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using MediatR;

namespace GymAppApi.Application.Features.Branches.Queries.GetBranches;

public class GetBranchesQueryHandler : IRequestHandler<GetBranchesQuery, IReadOnlyList<BranchListItemDto>>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetBranchesQueryHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<IReadOnlyList<BranchListItemDto>> Handle(GetBranchesQuery request, CancellationToken cancellationToken)
    {
        var branches = await _unitOfWork.GetReadRepository<Branch>().GetAllAsync(cancellationToken: cancellationToken);

        return branches.Select(b => new BranchListItemDto
        {
            Id = b.Id,
            CompanyId = b.CompanyId,
            Name = b.Name,
            Address = b.Address,
            IsActive = b.IsActive
        }).ToList();
    }
}
```

- [ ] **Step 7: Implement `BranchesController`**

```csharp
// Presentation/GymAppApi.WebApi/Controllers/BranchesController.cs
using GymAppApi.Application.Features.Branches.Commands.CreateBranch;
using GymAppApi.Application.Features.Branches.Queries.GetBranches;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace GymAppApi.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BranchesController : ControllerBase
{
    private readonly IMediator _mediator;

    public BranchesController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetBranchesQuery(), cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create(CreateBranchCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }
}
```

- [ ] **Step 8: Register `BranchRules` in `Application/Registration.cs`**

```csharp
// Core/GymAppApi.Application/Registration.cs — add inside AddApplication(), before `return services;`
services.AddTransient<GymAppApi.Application.Features.Branches.Rules.BranchRules>();
```

- [ ] **Step 9: Full solution build + full test run**

Run:
```bash
dotnet build
dotnet test
```
Expected: `Build succeeded`, all tests pass (unit + integration).

- [ ] **Step 10: Manual smoke test against a local Postgres**

Run:
```bash
dotnet ef database update --project Infrastructure/GymAppApi.Persistence --startup-project Presentation/GymAppApi.WebApi
dotnet run --project Presentation/GymAppApi.WebApi
```
In another terminal (adjust the port to match the `dotnet run` output):
```bash
curl -X POST https://localhost:5001/api/branches -k -H "Content-Type: application/json" -d "{\"companyId\":1,\"name\":\"Test Şube\",\"address\":\"Test Adres\"}"
curl https://localhost:5001/api/branches -k
```
Expected: first call `400` (CompanyId 1 doesn't exist yet — expected, no Company seed in this plan) proving the exception middleware maps `CompanyNotFoundException` to the right status via `BaseException.StatusCode`; the actual green-path proof is the passing test suite from Step 9. Stop the server with Ctrl+C.

- [ ] **Step 11: Commit**

```bash
git add Core/GymAppApi.Application Presentation/GymAppApi.WebApi Tests/GymAppApi.UnitTests
git commit -m "Add Branch create/list vertical slice through the full Onion+CQRS stack"
```

---

## Task 13: Mobile API Documentation Seed

**Files:**
- Create: `docs/mobile-api/README.md`

- [ ] **Step 1: Write the initial mobile-facing API doc**

```markdown
# GymAppApi — Mobil Ekip için API Dokümantasyonu

Bu doküman mobil (Flutter) ekibin backend'i entegre ederken ihtiyaç duyacağı
bilgileri tutar. Her yeni backend planı tamamlandığında bu dosya güncellenir
— buradaki bilgi her zaman **gerçekten deploy edilmiş** davranışı yansıtır,
henüz yapılmamış özellik burada "yakında" olarak değil, hiç yazılmadan durur.

## Ortamlar

| Ortam | Base URL | Not |
|---|---|---|
| Local (geliştirme) | `https://localhost:5001` | `dotnet run` ile |
| Development | TBD | CI/CD planı tamamlanınca eklenecek |
| Staging | TBD | |
| Production | TBD | |

## Canlı API Referansı

Her ortamda `/swagger` altında tam, güncel OpenAPI/Swagger UI mevcuttur —
bu dosya endpoint'leri tekrar listelemez, sadece mobil entegrasyon için
Swagger'da görünmeyen bağlamı (auth akışı, hata formatı, versiyonlama gibi)
anlatır.

## Hata (Error) Formatı

Her hata yanıtı şu şekildedir (`ExceptionMiddleware` tarafından merkezi üretilir):

```json
{
  "status": 404,
  "errors": ["Company 999 bulunamadı."]
}
```

- Validasyon hataları (eksik/hatalı alan): `422 Unprocessable Entity`, `errors` dizisinde alan bazlı mesajlar.
- İş kuralı ihlalleri (ör. "zaten var", "bulunamadı"): `409 Conflict` / `404 Not Found`.
- Beklenmeyen sunucu hataları: `500 Internal Server Error`, `errors: ["Beklenmeyen bir hata oluştu."]` (detay loglanır, client'a sızdırılmaz).

## Kimlik Doğrulama

**Henüz eklenmedi.** Auth (telefon+şifre+SMS OTP login, JWT, context/rol
seçimi) ayrı bir planla gelecek — bu bölüm o plan tamamlandığında
doldurulacak. Şu an tüm endpoint'ler anonim erişime açık (geliştirme/test
amaçlı, production'a bu haliyle çıkmaz).

## Mevcut Endpoint'ler (bu plan sonunda)

### `POST /api/branches` — Şube oluştur

**Not:** Bu endpoint şu an sadece backend altyapısını kanıtlamak için var,
mobil ekranı henüz yok — gerçek "Yeni Firma/Şube Ekle" akışı Super Admin'e
özel bir sonraki planda (Tenant Onboarding) gelecek.

Request:
```json
{ "companyId": 1, "name": "Merkez Şube", "address": "..." }
```

Response `201 Created`:
```json
{ "id": 5, "name": "Merkez Şube" }
```

### `GET /api/branches` — Şubeleri listele

Response `200 OK`:
```json
[
  { "id": 5, "companyId": 1, "name": "Merkez Şube", "address": "...", "isActive": true }
]
```

## Sonraki Planlarda Eklenecekler

- Auth: `POST /auth/login`, `POST /auth/verify-otp`, `POST /auth/refresh-token`, context/rol seçim ekranı için `GET /me/assignments`.
- Tenant Onboarding: Super Admin'in yeni Company/Branch/GymAdmin açma akışı.
- Paket & Üyelik: Package CRUD, PackageAssignment, MembershipFreeze.
- Çok dilli içerik: `Translation` tablosu tüketen endpoint'ler.
```

- [ ] **Step 2: Commit**

```bash
git add docs/mobile-api/README.md
git commit -m "Seed mobile API documentation"
```

---

## Self-Review Notes

- **Spec coverage (Faz 1 slice covered by this plan):** Identity & Tenancy data model (Company, Branch, User, Assignment, OtpVerification, DeviceToken, AuditLog) ✓; global query filter by CompanyId/BranchId ✓; `IsActive` bypass for Super Admin ✓; `SaveChangesAsync` auto-stamping ✓; CompanyId/BranchId indexes ✓; Onion + CQRS/MediatR + Repository/UnitOfWork ✓ per user's explicit instruction. **Not** covered here (by design, separate plans): Auth/JWT/OTP, Tenant Onboarding flow, Package/Membership/Freeze, Translation table, AuditLog write-on-every-mutation (only the table + config exist; nothing writes to it yet — the next plan that adds mutating features must call `AuditLog` inserts explicitly), Faz 2 modules.
- **Placeholder scan:** no "TBD/handle it later" left in code steps; the two forward-looking notes (`AmbientTenantContext`, mobile-docs "Auth: henüz eklenmedi") are explicit, intentional deferrals to a named future plan, not vague gaps.
- **Type consistency check:** `IUnitOfWork.BeginTransactionAsync` return type corrected to `Task<IAsyncDisposable>` in Task 5 Step 6a *before* Task 9's `UnitOfWork` implements it — verified both use the same signature. `ICompanyScoped`/`ITenantScoped`/`IDeactivatable` names are used identically across Task 2 (definition), Task 3 (entities), and Task 7 (DbContext filters). `IReadRepository<T>`/`IWriteRepository<T>` method names match between Task 4 (interface) and Task 9 (implementation) and Task 12 (handler usage: `GetAllAsync`, `AnyAsync`, `AddAsync`).
