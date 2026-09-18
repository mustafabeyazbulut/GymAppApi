# Package Core Module Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the Package/PackageAssignment core module described in `docs/superpowers/specs/2026-09-18-package-core-module-design.md` — Members link to a Company by being given a Package, confirmed via the same SMS-invitation pattern already used for staff.

**Architecture:** Mirrors the existing Assignment/PendingAssignmentInvitation pattern exactly: CQRS commands via MediatR, a generic `IUnitOfWork`/`IReadRepository`/`IWriteRepository` repository layer, FluentValidation validators, EF Core with `ICompanyScoped` tenant filtering, and an invitation-confirmation flow (`PendingPackageAssignmentInvitation` + `PackageAssignmentInvitationService`) that copies `AssignmentInvitationService` almost line for line.

**Tech Stack:** ASP.NET Core 10 / EF Core (Npgsql in prod, InMemory in tests) / MediatR / FluentValidation / xUnit + Moq.

---

## Key Facts (read before starting)

- Migration command: `dotnet ef migrations add <Name> --project Infrastructure/GymAppApi.Persistence --startup-project Presentation/GymAppApi.WebApi`, run from repo root.
- `ICompanyScoped` (`int CompanyId`, non-nullable) → EF global query filter `CompanyId == ambient.CompanyId` (or SuperAdmin bypass). Combined with `IDeactivatable` (`bool IsActive`) → filter also requires `IsActive`. `Package` implements both (mirrors `Branch`). `PackageAssignment` implements only `ICompanyScoped` (it uses a `Status` enum, not `IsActive` — cancelled/frozen rows must stay queryable, not be hidden by the global filter).
- `PendingPackageAssignmentInvitation` is **not** tenant-scoped (mirrors `PendingAssignmentInvitation`) — it must be added to `GymAppApiDbContext.IntentionallyUnscopedEntityTypes`, otherwise `OnModelCreating` throws at startup. Reason: the confirming Member has no company assignment yet, so a company-scoped filter would hide their own pending invitation.
- Enum columns are stored as strings: `builder.Property(x => x.Foo).HasConversion<string>().HasMaxLength(30);`.
- FK navigation without a back-collection: `.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);`. FK navigation WITH a back-collection (only where one already exists, e.g. `User.Assignments`): `.HasOne(x => x.User).WithMany(u => u.Assignments)...`.
- The `"StaffManagement"` authorization policy already means "caller holds a BranchManager, GymAdmin, or SuperAdmin assignment somewhere" (coarse-grained). Every handler that uses it re-checks the caller is scoped to the *specific* company/branch in the request — this plan follows that exact pattern throughout, it does not add a new policy.
- Controller pattern: `[Authorize(Policy = "...")]` on the action, `command.RequestedByUserId = CurrentUserId;` (from JWT `sub`) set in the controller, never trusted from the request body.
- Test mock pattern for a repository method called more than once with different predicates in the same `Handle()`: `Mock.SetupSequence(...)` returning one value per call, in call order (see `AddStaffMemberCommandHandlerTests.Wire` in the existing codebase for the exact idiom).
- Every command/handler/test file in this plan has a directly analogous existing file — named in each task — copy its structure, don't invent a new one.

---

## Task 1: Domain enums

**Files:**
- Create: `Core/GymAppApi.Domain/Enums/PackageType.cs`
- Create: `Core/GymAppApi.Domain/Enums/PackageAccessTier.cs`
- Create: `Core/GymAppApi.Domain/Enums/PackageAssignmentStatus.cs`

No test for this task — matches existing precedent (`AssignmentRole.cs` has no dedicated test file; enums are exercised through the handlers that use them).

- [ ] **Step 1: Create the enums**

```csharp
// Core/GymAppApi.Domain/Enums/PackageType.cs
namespace GymAppApi.Domain.Enums;

public enum PackageType
{
    Duration,
    SessionBased,
}
```

```csharp
// Core/GymAppApi.Domain/Enums/PackageAccessTier.cs
namespace GymAppApi.Domain.Enums;

// Not read or enforced anywhere yet - reserved for a future content/video
// entitlement system. See .claude/memory/project-member-package-linkage-design.md.
public enum PackageAccessTier
{
    Standard,
    Premium,
}
```

```csharp
// Core/GymAppApi.Domain/Enums/PackageAssignmentStatus.cs
namespace GymAppApi.Domain.Enums;

// Expired is deliberately NOT a stored status - a PackageAssignment whose
// EndDate has passed is still "Active" in the database; callers compute
// "is this currently usable" as Status == Active && (EndDate == null || EndDate > now).
public enum PackageAssignmentStatus
{
    Active,
    Frozen,
    Cancelled,
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build`
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add Core/GymAppApi.Domain/Enums/PackageType.cs Core/GymAppApi.Domain/Enums/PackageAccessTier.cs Core/GymAppApi.Domain/Enums/PackageAssignmentStatus.cs
git commit -m "Add Package domain enums"
```

---

## Task 2: `Package` entity + EF configuration + DbContext registration

**Files:**
- Create: `Core/GymAppApi.Domain/Entities/Package.cs`
- Create: `Infrastructure/GymAppApi.Persistence/Configurations/PackageConfiguration.cs`
- Modify: `Infrastructure/GymAppApi.Persistence/Context/GymAppApiDbContext.cs`

- [ ] **Step 1: Create the `Package` entity**

```csharp
// Core/GymAppApi.Domain/Entities/Package.cs
using GymAppApi.Domain.Common;
using GymAppApi.Domain.Enums;

namespace GymAppApi.Domain.Entities;

public class Package : EntityBase, ICompanyScoped, IDeactivatable
{
    public int CompanyId { get; set; }
    public Company? Company { get; set; }

    // null = valid at every branch of the company; set = valid only at this
    // one branch. Decided at template level, not per-assignment - see
    // .claude/memory/project-member-package-linkage-design.md.
    public int? BranchId { get; set; }
    public Branch? Branch { get; set; }

    public string Name { get; set; } = null!;
    public string? Description { get; set; }

    public PackageType Type { get; set; }
    // Duration: required. SessionBased: optional upper bound ("60 gün içinde kullan").
    public int? DurationDays { get; set; }
    // SessionBased: required. Duration: always null.
    public int? SessionCount { get; set; }

    public decimal Price { get; set; }
    public PackageAccessTier AccessTier { get; set; } = PackageAccessTier.Standard;
    public bool IsActive { get; set; } = true;
}
```

- [ ] **Step 2: Create the EF configuration**

```csharp
// Infrastructure/GymAppApi.Persistence/Configurations/PackageConfiguration.cs
using GymAppApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymAppApi.Persistence.Configurations;

public class PackageConfiguration : IEntityTypeConfiguration<Package>
{
    public void Configure(EntityTypeBuilder<Package> builder)
    {
        builder.Property(x => x.Name).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.AccessTier).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.Price).HasColumnType("decimal(10,2)");
        builder.Property(x => x.IsActive).HasDefaultValue(true);

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Branch)
            .WithMany()
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.CompanyId);
    }
}
```

- [ ] **Step 3: Register the `DbSet` on `GymAppApiDbContext`**

Find in `Infrastructure/GymAppApi.Persistence/Context/GymAppApiDbContext.cs`:

```csharp
    public DbSet<PendingAssignmentInvitation> PendingAssignmentInvitations => Set<PendingAssignmentInvitation>();
```

Add directly below it:

```csharp
    public DbSet<PendingAssignmentInvitation> PendingAssignmentInvitations => Set<PendingAssignmentInvitation>();
    public DbSet<Package> Packages => Set<Package>();
```

- [ ] **Step 4: Build to verify it compiles**

Run: `dotnet build`
Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add Core/GymAppApi.Domain/Entities/Package.cs Infrastructure/GymAppApi.Persistence/Configurations/PackageConfiguration.cs Infrastructure/GymAppApi.Persistence/Context/GymAppApiDbContext.cs
git commit -m "Add Package entity"
```

---

## Task 3: `PackageAssignment` entity + EF configuration + `User.PackageAssignments`

**Files:**
- Create: `Core/GymAppApi.Domain/Entities/PackageAssignment.cs`
- Create: `Infrastructure/GymAppApi.Persistence/Configurations/PackageAssignmentConfiguration.cs`
- Modify: `Core/GymAppApi.Domain/Entities/User.cs`
- Modify: `Infrastructure/GymAppApi.Persistence/Context/GymAppApiDbContext.cs`

- [ ] **Step 1: Create the `PackageAssignment` entity**

```csharp
// Core/GymAppApi.Domain/Entities/PackageAssignment.cs
using GymAppApi.Domain.Common;
using GymAppApi.Domain.Enums;

namespace GymAppApi.Domain.Entities;

public class PackageAssignment : EntityBase, ICompanyScoped
{
    public int PackageId { get; set; }
    public Package? Package { get; set; }

    public int MemberUserId { get; set; }
    public User? MemberUser { get; set; }

    // Snapshot of Package.CompanyId/BranchId at confirmation time - if the
    // Package is retired or changed later, this assignment's own scope stays
    // exactly as it was when granted.
    public int CompanyId { get; set; }
    public Company? Company { get; set; }
    public int? BranchId { get; set; }
    public Branch? Branch { get; set; }

    public int AssignedByUserId { get; set; }

    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    // Copied from Package.SessionCount - informational only in this module,
    // does not decrement (no check-in system yet).
    public int? RemainingSessions { get; set; }

    public PackageAssignmentStatus Status { get; set; } = PackageAssignmentStatus.Active;
    // Only set while Status == Frozen. On unfreeze, EndDate is pushed forward
    // by (now - FrozenAt) and this is cleared back to null.
    public DateTime? FrozenAt { get; set; }
}
```

- [ ] **Step 2: Create the EF configuration**

```csharp
// Infrastructure/GymAppApi.Persistence/Configurations/PackageAssignmentConfiguration.cs
using GymAppApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymAppApi.Persistence.Configurations;

public class PackageAssignmentConfiguration : IEntityTypeConfiguration<PackageAssignment>
{
    public void Configure(EntityTypeBuilder<PackageAssignment> builder)
    {
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);

        builder.HasOne(x => x.Package)
            .WithMany()
            .HasForeignKey(x => x.PackageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.MemberUser)
            .WithMany(u => u.PackageAssignments)
            .HasForeignKey(x => x.MemberUserId)
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
        builder.HasIndex(x => x.MemberUserId);
    }
}
```

- [ ] **Step 3: Add the `PackageAssignments` navigation to `User`**

Find in `Core/GymAppApi.Domain/Entities/User.cs`:

```csharp
    public ICollection<Assignment> Assignments { get; set; } = new List<Assignment>();
```

Add directly below it:

```csharp
    public ICollection<Assignment> Assignments { get; set; } = new List<Assignment>();
    public ICollection<PackageAssignment> PackageAssignments { get; set; } = new List<PackageAssignment>();
```

- [ ] **Step 4: Register the `DbSet`**

Find in `Infrastructure/GymAppApi.Persistence/Context/GymAppApiDbContext.cs`:

```csharp
    public DbSet<Package> Packages => Set<Package>();
```

Add directly below it:

```csharp
    public DbSet<Package> Packages => Set<Package>();
    public DbSet<PackageAssignment> PackageAssignments => Set<PackageAssignment>();
```

- [ ] **Step 5: Build to verify it compiles**

Run: `dotnet build`
Expected: `Build succeeded.`

- [ ] **Step 6: Commit**

```bash
git add Core/GymAppApi.Domain/Entities/PackageAssignment.cs Core/GymAppApi.Domain/Entities/User.cs Infrastructure/GymAppApi.Persistence/Configurations/PackageAssignmentConfiguration.cs Infrastructure/GymAppApi.Persistence/Context/GymAppApiDbContext.cs
git commit -m "Add PackageAssignment entity"
```

---

## Task 4: `PendingPackageAssignmentInvitation` entity + EF configuration + unscoped registration

**Files:**
- Create: `Core/GymAppApi.Domain/Entities/PendingPackageAssignmentInvitation.cs`
- Create: `Infrastructure/GymAppApi.Persistence/Configurations/PendingPackageAssignmentInvitationConfiguration.cs`
- Modify: `Infrastructure/GymAppApi.Persistence/Context/GymAppApiDbContext.cs`

- [ ] **Step 1: Create the entity**

```csharp
// Core/GymAppApi.Domain/Entities/PendingPackageAssignmentInvitation.cs
using GymAppApi.Domain.Common;

namespace GymAppApi.Domain.Entities;

// The security gate for CreatePackageAssignmentCommand - a Package is never
// attached to a Member immediately from the inviter's request alone, it only
// takes effect once the MEMBER proves control of their own phone by
// confirming this row's Code (see ConfirmPackageAssignmentCommand). Mirrors
// PendingAssignmentInvitation exactly. Not tenant-scoped - the member may not
// have any assignment to that company yet, that's the whole point of this row.
public class PendingPackageAssignmentInvitation : EntityBase
{
    public int TargetUserId { get; set; }
    public User? TargetUser { get; set; }

    public int PackageId { get; set; }
    public Package? Package { get; set; }

    // Snapshot of Package.CompanyId/BranchId at issue time.
    public int CompanyId { get; set; }
    public int? BranchId { get; set; }
    public int RequestedByUserId { get; set; }

    public string Code { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
    public int AttemptCount { get; set; }
    public bool IsUsed { get; set; }
}
```

- [ ] **Step 2: Create the EF configuration**

```csharp
// Infrastructure/GymAppApi.Persistence/Configurations/PendingPackageAssignmentInvitationConfiguration.cs
using GymAppApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymAppApi.Persistence.Configurations;

public class PendingPackageAssignmentInvitationConfiguration : IEntityTypeConfiguration<PendingPackageAssignmentInvitation>
{
    public void Configure(EntityTypeBuilder<PendingPackageAssignmentInvitation> builder)
    {
        builder.Property(x => x.Code).IsRequired().HasMaxLength(10);

        builder.HasOne(x => x.TargetUser)
            .WithMany()
            .HasForeignKey(x => x.TargetUserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Package)
            .WithMany()
            .HasForeignKey(x => x.PackageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.TargetUserId, x.CompanyId });
    }
}
```

- [ ] **Step 3: Register the `DbSet` and add to `IntentionallyUnscopedEntityTypes`**

Find in `Infrastructure/GymAppApi.Persistence/Context/GymAppApiDbContext.cs`:

```csharp
        typeof(PendingAssignmentInvitation),
    };
```

Replace with:

```csharp
        typeof(PendingAssignmentInvitation),
        typeof(PendingPackageAssignmentInvitation),
    };
```

Find:

```csharp
    public DbSet<PendingAssignmentInvitation> PendingAssignmentInvitations => Set<PendingAssignmentInvitation>();
```

Add directly below it:

```csharp
    public DbSet<PendingAssignmentInvitation> PendingAssignmentInvitations => Set<PendingAssignmentInvitation>();
    public DbSet<PendingPackageAssignmentInvitation> PendingPackageAssignmentInvitations => Set<PendingPackageAssignmentInvitation>();
```

- [ ] **Step 4: Build to verify it compiles**

Run: `dotnet build`
Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add Core/GymAppApi.Domain/Entities/PendingPackageAssignmentInvitation.cs Infrastructure/GymAppApi.Persistence/Configurations/PendingPackageAssignmentInvitationConfiguration.cs Infrastructure/GymAppApi.Persistence/Context/GymAppApiDbContext.cs
git commit -m "Add PendingPackageAssignmentInvitation entity"
```

---

## Task 5: EF migration for all three entities

**Files:**
- Create: `Infrastructure/GymAppApi.Persistence/Migrations/<timestamp>_AddPackageCoreModule.cs` (generated)
- Modify: `Infrastructure/GymAppApi.Persistence/Migrations/GymAppApiDbContextModelSnapshot.cs` (generated)

- [ ] **Step 1: Generate the migration**

Run (from repo root): `dotnet ef migrations add AddPackageCoreModule --project Infrastructure/GymAppApi.Persistence --startup-project Presentation/GymAppApi.WebApi`
Expected: a new `Infrastructure/GymAppApi.Persistence/Migrations/<timestamp>_AddPackageCoreModule.cs` (+ `.Designer.cs`) is created, and `GymAppApiDbContextModelSnapshot.cs` is updated.

- [ ] **Step 2: Verify the generated migration**

Open the new migration's `Up()` method. Confirm it contains three `migrationBuilder.CreateTable(...)` calls: `Packages` (columns include `CompanyId`, `BranchId` nullable, `Name`, `Description` nullable, `Type` string, `DurationDays` nullable int, `SessionCount` nullable int, `Price` decimal, `AccessTier` string, `IsActive`), `PackageAssignments` (columns include `PackageId`, `MemberUserId`, `CompanyId`, `BranchId` nullable, `AssignedByUserId`, `StartDate`, `EndDate` nullable, `RemainingSessions` nullable, `Status` string, `FrozenAt` nullable), and `PendingPackageAssignmentInvitations` (columns include `TargetUserId`, `PackageId`, `CompanyId`, `BranchId` nullable, `RequestedByUserId`, `Code`, `ExpiresAt`, `AttemptCount`, `IsUsed`). If any table or column is missing, a Task 2-4 configuration step was skipped — fix it, delete the migration's new file, revert the snapshot (`git checkout -- Infrastructure/GymAppApi.Persistence/Migrations/GymAppApiDbContextModelSnapshot.cs`), and repeat Step 1.

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build`
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add Infrastructure/GymAppApi.Persistence/Migrations/
git commit -m "Add AddPackageCoreModule migration"
```

---

## Task 6: `PackageAssignmentInvitationService`

**Files:**
- Create: `Core/GymAppApi.Application/Common/Invitations/PackageAssignmentInvitationService.cs`
- Test: `Tests/GymAppApi.UnitTests/Common/Invitations/PackageAssignmentInvitationServiceTests.cs`

Copies `AssignmentInvitationService`/`AssignmentInvitationServiceTests` exactly, adapted for `PendingPackageAssignmentInvitation`'s shape (`PackageId` instead of `Role`).

- [ ] **Step 1: Write the failing tests**

```csharp
// Tests/GymAppApi.UnitTests/Common/Invitations/PackageAssignmentInvitationServiceTests.cs
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.Invitations;
using GymAppApi.Application.Features.Auth.Exceptions;
using GymAppApi.Domain.Entities;
using Moq;

namespace GymAppApi.UnitTests.Common.Invitations;

public class PackageAssignmentInvitationServiceTests
{
    private static (Mock<IUnitOfWork> uow, Mock<IWriteRepository<PendingPackageAssignmentInvitation>> writeRepo) Wire(
        IReadOnlyList<PendingPackageAssignmentInvitation> priorLive)
    {
        var readRepo = new Mock<IReadRepository<PendingPackageAssignmentInvitation>>();
        readRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<PendingPackageAssignmentInvitation, bool>>>(), null, null, false, default))
            .ReturnsAsync(priorLive);
        var writeRepo = new Mock<IWriteRepository<PendingPackageAssignmentInvitation>>();
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<PendingPackageAssignmentInvitation>()).Returns(readRepo.Object);
        uow.Setup(u => u.GetWriteRepository<PendingPackageAssignmentInvitation>()).Returns(writeRepo.Object);

        return (uow, writeRepo);
    }

    [Fact]
    public async Task IssueAsync_WhenNoPriorInvitation_CreatesANewOneWithASixDigitCode()
    {
        var (uow, writeRepo) = Wire(priorLive: new List<PendingPackageAssignmentInvitation>());

        var code = await PackageAssignmentInvitationService.IssueAsync(
            uow.Object, targetUserId: 7, packageId: 5, companyId: 1, branchId: 2, requestedByUserId: 42, CancellationToken.None);

        Assert.Matches("^[0-9]{6}$", code);
        writeRepo.Verify(r => r.AddAsync(It.Is<PendingPackageAssignmentInvitation>(p =>
            p.TargetUserId == 7 && p.PackageId == 5 && p.CompanyId == 1 && p.BranchId == 2 &&
            p.RequestedByUserId == 42 && p.Code == code && !p.IsUsed && p.AttemptCount == 0), default), Times.Once);
    }

    [Fact]
    public async Task IssueAsync_WhenAPriorLiveInvitationIsOutsideTheCooldown_InvalidatesItAndIssuesANewCode()
    {
        var prior = new PendingPackageAssignmentInvitation
        {
            TargetUserId = 7,
            PackageId = 5,
            CompanyId = 1,
            Code = "111111",
            IsUsed = false,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5),
            CreatedAt = DateTime.UtcNow.AddSeconds(-61),
        };
        var (uow, writeRepo) = Wire(priorLive: new List<PendingPackageAssignmentInvitation> { prior });

        var code = await PackageAssignmentInvitationService.IssueAsync(
            uow.Object, targetUserId: 7, packageId: 5, companyId: 1, branchId: null, requestedByUserId: 42, CancellationToken.None);

        Assert.True(prior.IsUsed);
        writeRepo.Verify(r => r.Update(prior), Times.Once);
        writeRepo.Verify(r => r.AddAsync(It.Is<PendingPackageAssignmentInvitation>(p => p.Code == code), default), Times.Once);
    }

    [Fact]
    public async Task IssueAsync_WhenAPriorLiveInvitationIsWithinTheCooldown_ThrowsTooManyVerificationRequestsException()
    {
        var prior = new PendingPackageAssignmentInvitation
        {
            TargetUserId = 7,
            PackageId = 5,
            CompanyId = 1,
            Code = "111111",
            IsUsed = false,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5),
            CreatedAt = DateTime.UtcNow.AddSeconds(-10),
        };
        var (uow, writeRepo) = Wire(priorLive: new List<PendingPackageAssignmentInvitation> { prior });

        await Assert.ThrowsAsync<TooManyVerificationRequestsException>(() =>
            PackageAssignmentInvitationService.IssueAsync(
                uow.Object, targetUserId: 7, packageId: 5, companyId: 1, branchId: null, requestedByUserId: 42, CancellationToken.None));

        writeRepo.Verify(r => r.AddAsync(It.IsAny<PendingPackageAssignmentInvitation>(), default), Times.Never);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Tests/GymAppApi.UnitTests --filter "FullyQualifiedName~PackageAssignmentInvitationServiceTests"`
Expected: FAIL to compile (`PackageAssignmentInvitationService` does not exist).

- [ ] **Step 3: Implement the service**

```csharp
// Core/GymAppApi.Application/Common/Invitations/PackageAssignmentInvitationService.cs
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Auth.Exceptions;
using GymAppApi.Domain.Entities;

namespace GymAppApi.Application.Common.Invitations;

// Mirrors AssignmentInvitationService exactly, for Package instead of role
// assignments - see CreatePackageAssignmentCommandHandler/ConfirmPackageAssignmentCommand.
public static class PackageAssignmentInvitationService
{
    public const int MaxAttempts = 5;
    private const int CodeExpiryMinutes = 10;
    private const int CooldownSeconds = 60;

    public static async Task<string> IssueAsync(
        IUnitOfWork unitOfWork, int targetUserId, int packageId, int companyId, int? branchId,
        int requestedByUserId, CancellationToken cancellationToken)
    {
        var readRepo = unitOfWork.GetReadRepository<PendingPackageAssignmentInvitation>();
        var writeRepo = unitOfWork.GetWriteRepository<PendingPackageAssignmentInvitation>();
        var now = DateTime.UtcNow;

        var priorLive = await readRepo.GetAllAsync(
            p => p.TargetUserId == targetUserId && p.CompanyId == companyId && !p.IsUsed && p.ExpiresAt > now,
            cancellationToken: cancellationToken);

        var mostRecentPrior = priorLive.OrderByDescending(p => p.CreatedAt).FirstOrDefault();
        if (mostRecentPrior is not null && now - mostRecentPrior.CreatedAt < TimeSpan.FromSeconds(CooldownSeconds))
        {
            throw new TooManyVerificationRequestsException();
        }

        foreach (var prior in priorLive)
        {
            prior.IsUsed = true;
            writeRepo.Update(prior);
        }

        var code = Random.Shared.Next(100000, 999999).ToString();
        await writeRepo.AddAsync(new PendingPackageAssignmentInvitation
        {
            TargetUserId = targetUserId,
            PackageId = packageId,
            CompanyId = companyId,
            BranchId = branchId,
            RequestedByUserId = requestedByUserId,
            Code = code,
            ExpiresAt = now.AddMinutes(CodeExpiryMinutes),
            AttemptCount = 0,
            IsUsed = false,
        }, cancellationToken);

        return code;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Tests/GymAppApi.UnitTests --filter "FullyQualifiedName~PackageAssignmentInvitationServiceTests"`
Expected: `Passed! - Failed: 0, Passed: 3`

- [ ] **Step 5: Commit**

```bash
git add Core/GymAppApi.Application/Common/Invitations/PackageAssignmentInvitationService.cs Tests/GymAppApi.UnitTests/Common/Invitations/PackageAssignmentInvitationServiceTests.cs
git commit -m "Add PackageAssignmentInvitationService"
```

---

## Task 7: `CreatePackageCommand`

**Files:**
- Create: `Core/GymAppApi.Application/Features/Packages/Commands/CreatePackage/CreatePackageCommand.cs`
- Create: `Core/GymAppApi.Application/Features/Packages/Commands/CreatePackage/CreatePackageCommandValidator.cs`
- Create: `Core/GymAppApi.Application/Features/Packages/Commands/CreatePackage/CreatePackageCommandResult.cs`
- Create: `Core/GymAppApi.Application/Features/Packages/Commands/CreatePackage/CreatePackageCommandHandler.cs`
- Test: `Tests/GymAppApi.UnitTests/Features/Packages/CreatePackageCommandHandlerTests.cs`

Authorization: `BranchId == null` (company-wide) ⇒ GymAdmin(of company)/SuperAdmin only (mirrors `CreateBranchCommandHandler`). `BranchId != null` ⇒ GymAdmin(of company) OR BranchManager(of that exact branch)/SuperAdmin (mirrors `AddStaffMemberCommandHandler`'s non-BranchManager-role branch of its ternary).

- [ ] **Step 1: Write the failing tests**

```csharp
// Tests/GymAppApi.UnitTests/Features/Packages/CreatePackageCommandHandlerTests.cs
using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Packages.Commands.CreatePackage;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Packages;

public class CreatePackageCommandHandlerTests
{
    private const int CallerId = 42;
    private const int CompanyId = 1;
    private const int BranchIdInCompany = 10;

    private static (Mock<IUnitOfWork> uow, Mock<IWriteRepository<Package>> writeRepo) Wire(
        IReadOnlyList<Assignment> callerAssignments, Company? company = null)
    {
        var assignmentReadRepo = new Mock<IReadRepository<Assignment>>();
        assignmentReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Assignment, bool>>>(), null, null, false, default))
            .ReturnsAsync(callerAssignments);
        var writeRepo = new Mock<IWriteRepository<Package>>();

        var companyReadRepo = new Mock<IReadRepository<Company>>();
        companyReadRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Company, bool>>>(), null, false, default))
            .ReturnsAsync(company ?? new Company { Id = CompanyId, Name = "Test Co", IsActive = true });

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(assignmentReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<Package>()).Returns(writeRepo.Object);
        uow.Setup(u => u.GetReadRepository<Company>()).Returns(companyReadRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        return (uow, writeRepo);
    }

    private static CreatePackageCommand ValidCommand() => new()
    {
        CompanyId = CompanyId,
        BranchId = BranchIdInCompany,
        Name = "10 Seans",
        Type = PackageType.SessionBased,
        SessionCount = 10,
        Price = 1000m,
        RequestedByUserId = CallerId,
    };

    [Fact]
    public async Task Handle_WhenCompanyDoesNotExist_ThrowsNotFoundException()
    {
        var (uow, writeRepo) = Wire(callerAssignments: new List<Assignment>());
        var companyReadRepo = new Mock<IReadRepository<Company>>();
        companyReadRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Company, bool>>>(), null, false, default))
            .ReturnsAsync((Company?)null);
        uow.Setup(u => u.GetReadRepository<Company>()).Returns(companyReadRepo.Object);
        var handler = new CreatePackageCommandHandler(uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(ValidCommand(), CancellationToken.None));
        writeRepo.Verify(r => r.AddAsync(It.IsAny<Package>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenCallerIsGymAdminOfCompanyCreatingABranchSpecificPackage_Succeeds()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = CompanyId, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, writeRepo) = Wire(callerAssignments);
        var handler = new CreatePackageCommandHandler(uow.Object);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.Equal("10 Seans", result.Name);
        writeRepo.Verify(r => r.AddAsync(It.Is<Package>(p =>
            p.CompanyId == CompanyId && p.BranchId == BranchIdInCompany && p.Type == PackageType.SessionBased && p.SessionCount == 10), default), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCallerIsBranchManagerOfThatExactBranch_Succeeds()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = CompanyId, BranchId = BranchIdInCompany, Role = AssignmentRole.BranchManager, IsActive = true } };
        var (uow, writeRepo) = Wire(callerAssignments);
        var handler = new CreatePackageCommandHandler(uow.Object);

        await handler.Handle(ValidCommand(), CancellationToken.None);

        writeRepo.Verify(r => r.AddAsync(It.IsAny<Package>(), default), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCallerIsBranchManagerOfADifferentBranch_ThrowsForbiddenException()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = CompanyId, BranchId = 999, Role = AssignmentRole.BranchManager, IsActive = true } };
        var (uow, writeRepo) = Wire(callerAssignments);
        var handler = new CreatePackageCommandHandler(uow.Object);

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(ValidCommand(), CancellationToken.None));
        writeRepo.Verify(r => r.AddAsync(It.IsAny<Package>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenBranchManagerTriesToCreateACompanyWidePackage_ThrowsForbiddenException()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = CompanyId, BranchId = BranchIdInCompany, Role = AssignmentRole.BranchManager, IsActive = true } };
        var (uow, writeRepo) = Wire(callerAssignments);
        var command = ValidCommand();
        command.BranchId = null;
        var handler = new CreatePackageCommandHandler(uow.Object);

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(command, CancellationToken.None));
        writeRepo.Verify(r => r.AddAsync(It.IsAny<Package>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenCallerHasNoRelevantAssignment_ThrowsForbiddenException()
    {
        var callerAssignments = new List<Assignment>();
        var (uow, writeRepo) = Wire(callerAssignments);
        var handler = new CreatePackageCommandHandler(uow.Object);

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(ValidCommand(), CancellationToken.None));
        writeRepo.Verify(r => r.AddAsync(It.IsAny<Package>(), default), Times.Never);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Tests/GymAppApi.UnitTests --filter "FullyQualifiedName~CreatePackageCommandHandlerTests"`
Expected: FAIL to compile (types don't exist yet).

- [ ] **Step 3: Implement the command, validator, result, and handler**

```csharp
// Core/GymAppApi.Application/Features/Packages/Commands/CreatePackage/CreatePackageCommand.cs
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.Packages.Commands.CreatePackage;

public class CreatePackageCommand : IRequest<CreatePackageCommandResult>
{
    public int CompanyId { get; set; }
    // null = valid at every branch of the company - only a GymAdmin/SuperAdmin
    // may create one of these; a BranchManager must supply their own branch.
    public int? BranchId { get; set; }

    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public PackageType Type { get; set; }
    public int? DurationDays { get; set; }
    public int? SessionCount { get; set; }
    public decimal Price { get; set; }

    // Set by the controller from the caller's own JWT sub claim.
    public int RequestedByUserId { get; set; }
}
```

```csharp
// Core/GymAppApi.Application/Features/Packages/Commands/CreatePackage/CreatePackageCommandValidator.cs
using FluentValidation;
using GymAppApi.Domain.Enums;

namespace GymAppApi.Application.Features.Packages.Commands.CreatePackage;

public class CreatePackageCommandValidator : AbstractValidator<CreatePackageCommand>
{
    public CreatePackageCommandValidator()
    {
        RuleFor(x => x.CompanyId).GreaterThan(0);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0);

        RuleFor(x => x.DurationDays).NotNull().When(x => x.Type == PackageType.Duration)
            .WithMessage("DurationDays is required for a Duration package.");
        RuleFor(x => x.SessionCount).Null().When(x => x.Type == PackageType.Duration)
            .WithMessage("SessionCount must not be set for a Duration package.");

        RuleFor(x => x.SessionCount).NotNull().GreaterThan(0).When(x => x.Type == PackageType.SessionBased)
            .WithMessage("SessionCount is required and must be greater than 0 for a SessionBased package.");
    }
}
```

```csharp
// Core/GymAppApi.Application/Features/Packages/Commands/CreatePackage/CreatePackageCommandResult.cs
namespace GymAppApi.Application.Features.Packages.Commands.CreatePackage;

public class CreatePackageCommandResult
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
}
```

```csharp
// Core/GymAppApi.Application/Features/Packages/Commands/CreatePackage/CreatePackageCommandHandler.cs
using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.Packages.Commands.CreatePackage;

public class CreatePackageCommandHandler : IRequestHandler<CreatePackageCommand, CreatePackageCommandResult>
{
    private readonly IUnitOfWork _unitOfWork;

    public CreatePackageCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<CreatePackageCommandResult> Handle(CreatePackageCommand request, CancellationToken cancellationToken)
    {
        var company = await _unitOfWork.GetReadRepository<Company>()
            .GetAsync(c => c.Id == request.CompanyId, cancellationToken: cancellationToken);
        if (company is null)
        {
            throw new NotFoundException($"Firma {request.CompanyId} bulunamadı.");
        }

        var callerAssignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
            a => a.UserId == request.RequestedByUserId && a.IsActive, cancellationToken: cancellationToken);

        // A company-wide package (BranchId == null) is a GymAdmin/SuperAdmin-only
        // decision, same as creating the company's own resources - a BranchManager
        // may only create a package scoped to their own exact branch.
        var callerIsAuthorized = request.BranchId is null
            ? callerAssignments.Any(a =>
                a.Role == AssignmentRole.SuperAdmin ||
                (a.Role == AssignmentRole.GymAdmin && a.CompanyId == request.CompanyId))
            : callerAssignments.Any(a =>
                a.Role == AssignmentRole.SuperAdmin ||
                (a.Role == AssignmentRole.GymAdmin && a.CompanyId == request.CompanyId) ||
                (a.Role == AssignmentRole.BranchManager && a.BranchId == request.BranchId));
        if (!callerIsAuthorized)
        {
            throw new ForbiddenException("Bu firma/şube için paket oluşturma yetkiniz yok.");
        }

        var package = new Package
        {
            CompanyId = request.CompanyId,
            BranchId = request.BranchId,
            Name = request.Name,
            Description = request.Description,
            Type = request.Type,
            DurationDays = request.DurationDays,
            SessionCount = request.SessionCount,
            Price = request.Price,
        };

        await _unitOfWork.GetWriteRepository<Package>().AddAsync(package, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CreatePackageCommandResult { Id = package.Id, Name = package.Name };
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Tests/GymAppApi.UnitTests --filter "FullyQualifiedName~CreatePackageCommandHandlerTests"`
Expected: `Passed! - Failed: 0, Passed: 6`

- [ ] **Step 5: Commit**

```bash
git add Core/GymAppApi.Application/Features/Packages/Commands/CreatePackage/ Tests/GymAppApi.UnitTests/Features/Packages/CreatePackageCommandHandlerTests.cs
git commit -m "Add CreatePackageCommand"
```

---

## Task 8: `GetPackagesQuery` + `GetPackageDetailQuery`

**Files:**
- Create: `Core/GymAppApi.Application/Features/Packages/Queries/GetPackages/GetPackagesQuery.cs`
- Create: `Core/GymAppApi.Application/Features/Packages/Queries/GetPackages/PackageDto.cs`
- Create: `Core/GymAppApi.Application/Features/Packages/Queries/GetPackages/GetPackagesQueryHandler.cs`
- Create: `Core/GymAppApi.Application/Features/Packages/Queries/GetPackageDetail/GetPackageDetailQuery.cs`
- Create: `Core/GymAppApi.Application/Features/Packages/Queries/GetPackageDetail/GetPackageDetailQueryHandler.cs`
- Test: `Tests/GymAppApi.UnitTests/Features/Packages/GetPackagesQueryHandlerTests.cs`
- Test: `Tests/GymAppApi.UnitTests/Features/Packages/GetPackageDetailQueryHandlerTests.cs`

List relies purely on the existing `ICompanyScoped`/`IDeactivatable` tenant query filter (mirrors `GetBranchesQueryHandler` — no manual predicate needed).

- [ ] **Step 1: Write the failing tests**

```csharp
// Tests/GymAppApi.UnitTests/Features/Packages/GetPackagesQueryHandlerTests.cs
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Packages.Queries.GetPackages;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Packages;

public class GetPackagesQueryHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsAllPackagesVisibleToTheCaller()
    {
        var packages = new List<Package>
        {
            new() { Id = 1, CompanyId = 1, BranchId = null, Name = "Yıllık Üyelik", Type = PackageType.Duration, DurationDays = 365, Price = 5000m, IsActive = true },
        };
        var readRepo = new Mock<IReadRepository<Package>>();
        readRepo.Setup(r => r.GetAllAsync(null, null, null, false, default)).ReturnsAsync(packages);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Package>()).Returns(readRepo.Object);

        var handler = new GetPackagesQueryHandler(uow.Object);
        var result = await handler.Handle(new GetPackagesQuery(), CancellationToken.None);

        var dto = Assert.Single(result);
        Assert.Equal("Yıllık Üyelik", dto.Name);
        Assert.Equal(365, dto.DurationDays);
    }
}
```

```csharp
// Tests/GymAppApi.UnitTests/Features/Packages/GetPackageDetailQueryHandlerTests.cs
using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Packages.Queries.GetPackageDetail;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Packages;

public class GetPackageDetailQueryHandlerTests
{
    [Fact]
    public async Task Handle_WhenPackageDoesNotExist_ThrowsNotFoundException()
    {
        var readRepo = new Mock<IReadRepository<Package>>();
        readRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Package, bool>>>(), null, false, default))
            .ReturnsAsync((Package?)null);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Package>()).Returns(readRepo.Object);

        var handler = new GetPackageDetailQueryHandler(uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(new GetPackageDetailQuery(1), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenPackageExists_ReturnsIt()
    {
        var package = new Package { Id = 1, CompanyId = 1, Name = "10 Seans", Type = PackageType.SessionBased, SessionCount = 10, Price = 1000m, IsActive = true };
        var readRepo = new Mock<IReadRepository<Package>>();
        readRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Package, bool>>>(), null, false, default))
            .ReturnsAsync(package);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Package>()).Returns(readRepo.Object);

        var handler = new GetPackageDetailQueryHandler(uow.Object);
        var result = await handler.Handle(new GetPackageDetailQuery(1), CancellationToken.None);

        Assert.Equal("10 Seans", result.Name);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Tests/GymAppApi.UnitTests --filter "FullyQualifiedName~GetPackagesQueryHandlerTests|FullyQualifiedName~GetPackageDetailQueryHandlerTests"`
Expected: FAIL to compile.

- [ ] **Step 3: Implement**

```csharp
// Core/GymAppApi.Application/Features/Packages/Queries/GetPackages/GetPackagesQuery.cs
using MediatR;

namespace GymAppApi.Application.Features.Packages.Queries.GetPackages;

public class GetPackagesQuery : IRequest<IReadOnlyList<PackageDto>>
{
}
```

```csharp
// Core/GymAppApi.Application/Features/Packages/Queries/GetPackages/PackageDto.cs
namespace GymAppApi.Application.Features.Packages.Queries.GetPackages;

public class PackageDto
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int? BranchId { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string Type { get; set; } = null!;
    public int? DurationDays { get; set; }
    public int? SessionCount { get; set; }
    public decimal Price { get; set; }
    public string AccessTier { get; set; } = null!;
    public bool IsActive { get; set; }
}
```

```csharp
// Core/GymAppApi.Application/Features/Packages/Queries/GetPackages/GetPackagesQueryHandler.cs
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using MediatR;

namespace GymAppApi.Application.Features.Packages.Queries.GetPackages;

public class GetPackagesQueryHandler : IRequestHandler<GetPackagesQuery, IReadOnlyList<PackageDto>>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetPackagesQueryHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<IReadOnlyList<PackageDto>> Handle(GetPackagesQuery request, CancellationToken cancellationToken)
    {
        var packages = await _unitOfWork.GetReadRepository<Package>().GetAllAsync(cancellationToken: cancellationToken);

        return packages.Select(ToDto).ToList();
    }

    internal static PackageDto ToDto(Package p) => new()
    {
        Id = p.Id,
        CompanyId = p.CompanyId,
        BranchId = p.BranchId,
        Name = p.Name,
        Description = p.Description,
        Type = p.Type.ToString(),
        DurationDays = p.DurationDays,
        SessionCount = p.SessionCount,
        Price = p.Price,
        AccessTier = p.AccessTier.ToString(),
        IsActive = p.IsActive,
    };
}
```

```csharp
// Core/GymAppApi.Application/Features/Packages/Queries/GetPackageDetail/GetPackageDetailQuery.cs
using GymAppApi.Application.Features.Packages.Queries.GetPackages;
using MediatR;

namespace GymAppApi.Application.Features.Packages.Queries.GetPackageDetail;

public class GetPackageDetailQuery : IRequest<PackageDto>
{
    public GetPackageDetailQuery(int packageId) => PackageId = packageId;

    public int PackageId { get; }
}
```

```csharp
// Core/GymAppApi.Application/Features/Packages/Queries/GetPackageDetail/GetPackageDetailQueryHandler.cs
using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Packages.Queries.GetPackages;
using GymAppApi.Domain.Entities;
using MediatR;

namespace GymAppApi.Application.Features.Packages.Queries.GetPackageDetail;

public class GetPackageDetailQueryHandler : IRequestHandler<GetPackageDetailQuery, PackageDto>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetPackageDetailQueryHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<PackageDto> Handle(GetPackageDetailQuery request, CancellationToken cancellationToken)
    {
        var package = await _unitOfWork.GetReadRepository<Package>()
            .GetAsync(p => p.Id == request.PackageId, cancellationToken: cancellationToken);
        if (package is null)
        {
            throw new NotFoundException($"Paket {request.PackageId} bulunamadı.");
        }

        return GetPackagesQueryHandler.ToDto(package);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Tests/GymAppApi.UnitTests --filter "FullyQualifiedName~GetPackagesQueryHandlerTests|FullyQualifiedName~GetPackageDetailQueryHandlerTests"`
Expected: `Passed! - Failed: 0, Passed: 3`

- [ ] **Step 5: Commit**

```bash
git add Core/GymAppApi.Application/Features/Packages/Queries/ Tests/GymAppApi.UnitTests/Features/Packages/GetPackagesQueryHandlerTests.cs Tests/GymAppApi.UnitTests/Features/Packages/GetPackageDetailQueryHandlerTests.cs
git commit -m "Add GetPackages/GetPackageDetail queries"
```

---

## Task 9: `SetPackageActiveCommand`

**Files:**
- Create: `Core/GymAppApi.Application/Features/Packages/Commands/SetPackageActive/SetPackageActiveCommand.cs`
- Create: `Core/GymAppApi.Application/Features/Packages/Commands/SetPackageActive/SetPackageActiveCommandHandler.cs`
- Test: `Tests/GymAppApi.UnitTests/Features/Packages/SetPackageActiveCommandHandlerTests.cs`

Mirrors `SetBranchActiveCommand`/`SetBranchActiveCommandHandler` exactly (GymAdmin of company / SuperAdmin only — not BranchManager, matching that a branch's own BranchManager can't open/close it either).

- [ ] **Step 1: Write the failing tests**

```csharp
// Tests/GymAppApi.UnitTests/Features/Packages/SetPackageActiveCommandHandlerTests.cs
using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Packages.Commands.SetPackageActive;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Packages;

public class SetPackageActiveCommandHandlerTests
{
    private const int CallerId = 42;

    private static (Mock<IUnitOfWork> uow, Mock<IWriteRepository<Package>> writeRepo) Wire(Package? package, IReadOnlyList<Assignment> callerAssignments)
    {
        var packageReadRepo = new Mock<IReadRepository<Package>>();
        packageReadRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Package, bool>>>(), null, false, default))
            .ReturnsAsync(package);
        var writeRepo = new Mock<IWriteRepository<Package>>();

        var assignmentReadRepo = new Mock<IReadRepository<Assignment>>();
        assignmentReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Assignment, bool>>>(), null, null, false, default))
            .ReturnsAsync(callerAssignments);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<Package>()).Returns(packageReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<Package>()).Returns(writeRepo.Object);
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(assignmentReadRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        return (uow, writeRepo);
    }

    [Fact]
    public async Task Handle_WhenPackageDoesNotExist_ThrowsNotFoundException()
    {
        var (uow, _) = Wire(package: null, callerAssignments: new List<Assignment>());
        var handler = new SetPackageActiveCommandHandler(uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new SetPackageActiveCommand { PackageId = 1, IsActive = false, RequestedByUserId = CallerId }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenCallerIsGymAdminOfThePackagesCompany_TogglesIsActive()
    {
        var package = new Package { Id = 1, CompanyId = 1, Name = "X", IsActive = true };
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, writeRepo) = Wire(package, callerAssignments);
        var handler = new SetPackageActiveCommandHandler(uow.Object);

        await handler.Handle(new SetPackageActiveCommand { PackageId = 1, IsActive = false, RequestedByUserId = CallerId }, CancellationToken.None);

        Assert.False(package.IsActive);
        writeRepo.Verify(r => r.Update(package), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCallerIsBranchManagerOfThePackagesBranch_ThrowsForbiddenException()
    {
        var package = new Package { Id = 1, CompanyId = 1, BranchId = 10, Name = "X", IsActive = true };
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, BranchId = 10, Role = AssignmentRole.BranchManager, IsActive = true } };
        var (uow, writeRepo) = Wire(package, callerAssignments);
        var handler = new SetPackageActiveCommandHandler(uow.Object);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new SetPackageActiveCommand { PackageId = 1, IsActive = false, RequestedByUserId = CallerId }, CancellationToken.None));
        writeRepo.Verify(r => r.Update(It.IsAny<Package>()), Times.Never);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Tests/GymAppApi.UnitTests --filter "FullyQualifiedName~SetPackageActiveCommandHandlerTests"`
Expected: FAIL to compile.

- [ ] **Step 3: Implement**

```csharp
// Core/GymAppApi.Application/Features/Packages/Commands/SetPackageActive/SetPackageActiveCommand.cs
using MediatR;

namespace GymAppApi.Application.Features.Packages.Commands.SetPackageActive;

public class SetPackageActiveCommand : IRequest
{
    // Set by the controller from the route segment.
    public int PackageId { get; set; }
    public bool IsActive { get; set; }
    // Set by the controller from the caller's own JWT sub claim.
    public int RequestedByUserId { get; set; }
}
```

```csharp
// Core/GymAppApi.Application/Features/Packages/Commands/SetPackageActive/SetPackageActiveCommandHandler.cs
using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.Packages.Commands.SetPackageActive;

public class SetPackageActiveCommandHandler : IRequestHandler<SetPackageActiveCommand>
{
    private readonly IUnitOfWork _unitOfWork;

    public SetPackageActiveCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task Handle(SetPackageActiveCommand request, CancellationToken cancellationToken)
    {
        var package = await _unitOfWork.GetReadRepository<Package>()
            .GetAsync(p => p.Id == request.PackageId, cancellationToken: cancellationToken);
        if (package is null)
        {
            throw new NotFoundException($"Paket {request.PackageId} bulunamadı.");
        }

        // Same GymAdmin(of company)/SuperAdmin-only rule as SetBranchActiveCommandHandler -
        // a BranchManager may not retire/reactivate even their own branch's package.
        var callerAssignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
            a => a.UserId == request.RequestedByUserId && a.IsActive, cancellationToken: cancellationToken);
        var callerIsAuthorized = callerAssignments.Any(a =>
            a.Role == AssignmentRole.SuperAdmin ||
            (a.Role == AssignmentRole.GymAdmin && a.CompanyId == package.CompanyId));
        if (!callerIsAuthorized)
        {
            throw new ForbiddenException("Bu paketi aktif/pasif yapma yetkiniz yok.");
        }

        package.IsActive = request.IsActive;
        _unitOfWork.GetWriteRepository<Package>().Update(package);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Tests/GymAppApi.UnitTests --filter "FullyQualifiedName~SetPackageActiveCommandHandlerTests"`
Expected: `Passed! - Failed: 0, Passed: 3`

- [ ] **Step 5: Commit**

```bash
git add Core/GymAppApi.Application/Features/Packages/Commands/SetPackageActive/ Tests/GymAppApi.UnitTests/Features/Packages/SetPackageActiveCommandHandlerTests.cs
git commit -m "Add SetPackageActiveCommand"
```

---

## Task 10: `PackagesController`

**Files:**
- Create: `Presentation/GymAppApi.WebApi/Controllers/PackagesController.cs`

No new unit tests (controllers are thin dispatch, covered by Task 15's integration tests).

- [ ] **Step 1: Implement the controller**

```csharp
// Presentation/GymAppApi.WebApi/Controllers/PackagesController.cs
using System.IdentityModel.Tokens.Jwt;
using GymAppApi.Application.Features.Packages.Commands.CreatePackage;
using GymAppApi.Application.Features.Packages.Commands.SetPackageActive;
using GymAppApi.Application.Features.Packages.Queries.GetPackageDetail;
using GymAppApi.Application.Features.Packages.Queries.GetPackages;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymAppApi.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PackagesController : ControllerBase
{
    private readonly IMediator _mediator;

    public PackagesController(IMediator mediator) => _mediator = mediator;

    private int CurrentUserId => int.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetPackagesQuery(), cancellationToken));

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetPackageDetailQuery(id), cancellationToken));

    [Authorize(Policy = "StaffManagement")]
    [HttpPost]
    public async Task<IActionResult> Create(CreatePackageCommand command, CancellationToken cancellationToken)
    {
        command.RequestedByUserId = CurrentUserId;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [Authorize(Policy = "StaffManagement")]
    [HttpPatch("{id}/active")]
    public async Task<IActionResult> SetActive(int id, SetPackageActiveCommand command, CancellationToken cancellationToken)
    {
        command.PackageId = id;
        command.RequestedByUserId = CurrentUserId;
        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build`
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add Presentation/GymAppApi.WebApi/Controllers/PackagesController.cs
git commit -m "Add PackagesController"
```

---

## Task 11: `CreatePackageAssignmentCommand`

**Files:**
- Create: `Core/GymAppApi.Application/Features/Packages/Exceptions/MemberAlreadyHasThisPackageException.cs`
- Create: `Core/GymAppApi.Application/Features/Packages/Commands/CreatePackageAssignment/CreatePackageAssignmentCommand.cs`
- Create: `Core/GymAppApi.Application/Features/Packages/Commands/CreatePackageAssignment/CreatePackageAssignmentCommandValidator.cs`
- Create: `Core/GymAppApi.Application/Features/Packages/Commands/CreatePackageAssignment/CreatePackageAssignmentCommandResult.cs`
- Create: `Core/GymAppApi.Application/Features/Packages/Commands/CreatePackageAssignment/CreatePackageAssignmentCommandHandler.cs`
- Test: `Tests/GymAppApi.UnitTests/Features/Packages/CreatePackageAssignmentCommandHandlerTests.cs`

Mirrors `AddStaffMemberCommand`/`AddStaffMemberCommandHandler`: looks up the member by phone (never creates a new `User`), rejects an exact-duplicate live/active assignment to the same Package, issues an invitation instead of assigning directly. Authorization: GymAdmin (of the package's company, any branch) OR BranchManager (only if `Package.BranchId` equals their own branch — a BranchManager can never assign a company-wide package, that's GymAdmin-only, same limit as creating one).

- [ ] **Step 1: Write the failing tests**

```csharp
// Tests/GymAppApi.UnitTests/Features/Packages/CreatePackageAssignmentCommandHandlerTests.cs
using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Packages.Commands.CreatePackageAssignment;
using GymAppApi.Application.Features.Packages.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Packages;

public class CreatePackageAssignmentCommandHandlerTests
{
    private const int CallerId = 42;
    private const int PackageId = 5;

    private static (Mock<IUnitOfWork> uow, Mock<IWriteRepository<PendingPackageAssignmentInvitation>> invitationWriteRepo) Wire(
        IReadOnlyList<Assignment> callerAssignments, Package? package, User? existingUser, bool alreadyAssigned)
    {
        var uow = new Mock<IUnitOfWork>();

        var assignmentReadRepo = new Mock<IReadRepository<Assignment>>();
        assignmentReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Assignment, bool>>>(), null, null, false, default))
            .ReturnsAsync(callerAssignments);
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(assignmentReadRepo.Object);

        var packageReadRepo = new Mock<IReadRepository<Package>>();
        packageReadRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Package, bool>>>(), null, false, default))
            .ReturnsAsync(package);
        uow.Setup(u => u.GetReadRepository<Package>()).Returns(packageReadRepo.Object);

        var userReadRepo = new Mock<IReadRepository<User>>();
        userReadRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), null, false, default))
            .ReturnsAsync(existingUser);
        uow.Setup(u => u.GetReadRepository<User>()).Returns(userReadRepo.Object);

        var packageAssignmentReadRepo = new Mock<IReadRepository<PackageAssignment>>();
        packageAssignmentReadRepo.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<PackageAssignment, bool>>>(), default))
            .ReturnsAsync(alreadyAssigned);
        uow.Setup(u => u.GetReadRepository<PackageAssignment>()).Returns(packageAssignmentReadRepo.Object);

        var invitationReadRepo = new Mock<IReadRepository<PendingPackageAssignmentInvitation>>();
        invitationReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<PendingPackageAssignmentInvitation, bool>>>(), null, null, false, default))
            .ReturnsAsync(new List<PendingPackageAssignmentInvitation>());
        uow.Setup(u => u.GetReadRepository<PendingPackageAssignmentInvitation>()).Returns(invitationReadRepo.Object);
        var invitationWriteRepo = new Mock<IWriteRepository<PendingPackageAssignmentInvitation>>();
        uow.Setup(u => u.GetWriteRepository<PendingPackageAssignmentInvitation>()).Returns(invitationWriteRepo.Object);

        var notificationWriteRepo = new Mock<IWriteRepository<Notification>>();
        uow.Setup(u => u.GetWriteRepository<Notification>()).Returns(notificationWriteRepo.Object);
        var deviceTokenReadRepo = new Mock<IReadRepository<DeviceToken>>();
        deviceTokenReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<DeviceToken, bool>>>(), null, null, false, default))
            .ReturnsAsync(new List<DeviceToken>());
        uow.Setup(u => u.GetReadRepository<DeviceToken>()).Returns(deviceTokenReadRepo.Object);

        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        return (uow, invitationWriteRepo);
    }

    private static Package BranchPackage() => new() { Id = PackageId, CompanyId = 1, BranchId = 10, Name = "10 Seans", IsActive = true };

    private static CreatePackageAssignmentCommand ValidCommand() => new()
    {
        PackageId = PackageId,
        MemberPhone = "+905550003333",
        RequestedByUserId = CallerId,
    };

    [Fact]
    public async Task Handle_WhenCallerIsGymAdminOfThePackagesCompany_IssuesAPendingInvitation()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var existingUser = new User { Id = 7, FullName = "Member", Phone = "+905550003333", PasswordHash = "x" };
        var (uow, invitationWriteRepo) = Wire(callerAssignments, BranchPackage(), existingUser, alreadyAssigned: false);
        var smsSender = new Mock<ISmsSender>();
        var handler = new CreatePackageAssignmentCommandHandler(uow.Object, smsSender.Object, Mock.Of<IPushNotificationSender>());

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.Equal(7, result.UserId);
        Assert.Equal(PackageId, result.PackageId);
        invitationWriteRepo.Verify(r => r.AddAsync(It.Is<PendingPackageAssignmentInvitation>(p =>
            p.TargetUserId == 7 && p.PackageId == PackageId && p.CompanyId == 1 && p.BranchId == 10), default), Times.Once);
        smsSender.Verify(s => s.SendAsync("+905550003333", It.IsAny<string>(), default), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCallerIsBranchManagerOfADifferentBranch_ThrowsForbiddenException()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, BranchId = 999, Role = AssignmentRole.BranchManager, IsActive = true } };
        var (uow, invitationWriteRepo) = Wire(callerAssignments, BranchPackage(), existingUser: null, alreadyAssigned: false);
        var handler = new CreatePackageAssignmentCommandHandler(uow.Object, Mock.Of<ISmsSender>(), Mock.Of<IPushNotificationSender>());

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(ValidCommand(), CancellationToken.None));
        invitationWriteRepo.Verify(r => r.AddAsync(It.IsAny<PendingPackageAssignmentInvitation>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenPackageDoesNotExist_ThrowsNotFoundException()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, _) = Wire(callerAssignments, package: null, existingUser: null, alreadyAssigned: false);
        var handler = new CreatePackageAssignmentCommandHandler(uow.Object, Mock.Of<ISmsSender>(), Mock.Of<IPushNotificationSender>());

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(ValidCommand(), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenPhoneDoesNotBelongToAnyRegisteredUser_ThrowsNotFoundException()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, invitationWriteRepo) = Wire(callerAssignments, BranchPackage(), existingUser: null, alreadyAssigned: false);
        var handler = new CreatePackageAssignmentCommandHandler(uow.Object, Mock.Of<ISmsSender>(), Mock.Of<IPushNotificationSender>());

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(ValidCommand(), CancellationToken.None));
        invitationWriteRepo.Verify(r => r.AddAsync(It.IsAny<PendingPackageAssignmentInvitation>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenMemberAlreadyHasAnActiveAssignmentToThisPackage_ThrowsMemberAlreadyHasThisPackageException()
    {
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var existingUser = new User { Id = 7, FullName = "Member", Phone = "+905550003333", PasswordHash = "x" };
        var (uow, invitationWriteRepo) = Wire(callerAssignments, BranchPackage(), existingUser, alreadyAssigned: true);
        var handler = new CreatePackageAssignmentCommandHandler(uow.Object, Mock.Of<ISmsSender>(), Mock.Of<IPushNotificationSender>());

        await Assert.ThrowsAsync<MemberAlreadyHasThisPackageException>(() => handler.Handle(ValidCommand(), CancellationToken.None));
        invitationWriteRepo.Verify(r => r.AddAsync(It.IsAny<PendingPackageAssignmentInvitation>(), default), Times.Never);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Tests/GymAppApi.UnitTests --filter "FullyQualifiedName~CreatePackageAssignmentCommandHandlerTests"`
Expected: FAIL to compile.

- [ ] **Step 3: Implement**

```csharp
// Core/GymAppApi.Application/Features/Packages/Exceptions/MemberAlreadyHasThisPackageException.cs
using GymAppApi.Application.Common.Exceptions;

namespace GymAppApi.Application.Features.Packages.Exceptions;

public class MemberAlreadyHasThisPackageException : ConflictException
{
    public MemberAlreadyHasThisPackageException() : base("Bu üyenin bu pakete zaten aktif bir ataması var.") { }
}
```

```csharp
// Core/GymAppApi.Application/Features/Packages/Commands/CreatePackageAssignment/CreatePackageAssignmentCommand.cs
using MediatR;

namespace GymAppApi.Application.Features.Packages.Commands.CreatePackageAssignment;

public class CreatePackageAssignmentCommand : IRequest<CreatePackageAssignmentCommandResult>
{
    public int PackageId { get; set; }
    // Looks up an already-registered user by phone - never creates a new User.
    public string MemberPhone { get; set; } = null!;

    // Set by the controller from the caller's own JWT sub claim.
    public int RequestedByUserId { get; set; }
}
```

```csharp
// Core/GymAppApi.Application/Features/Packages/Commands/CreatePackageAssignment/CreatePackageAssignmentCommandValidator.cs
using FluentValidation;

namespace GymAppApi.Application.Features.Packages.Commands.CreatePackageAssignment;

public class CreatePackageAssignmentCommandValidator : AbstractValidator<CreatePackageAssignmentCommand>
{
    public CreatePackageAssignmentCommandValidator()
    {
        RuleFor(x => x.PackageId).GreaterThan(0);
        RuleFor(x => x.MemberPhone).NotEmpty().Matches(@"^\+[1-9]\d{7,14}$");
    }
}
```

```csharp
// Core/GymAppApi.Application/Features/Packages/Commands/CreatePackageAssignment/CreatePackageAssignmentCommandResult.cs
namespace GymAppApi.Application.Features.Packages.Commands.CreatePackageAssignment;

// No PackageAssignmentId - nothing is assigned yet, only invited. The real
// PackageAssignment only comes into existence once the member confirms
// (ConfirmPackageAssignmentCommand).
public class CreatePackageAssignmentCommandResult
{
    public int UserId { get; set; }
    public int PackageId { get; set; }
}
```

```csharp
// Core/GymAppApi.Application/Features/Packages/Commands/CreatePackageAssignment/CreatePackageAssignmentCommandHandler.cs
using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.Invitations;
using GymAppApi.Application.Common.Notifications;
using GymAppApi.Application.Features.Packages.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.Packages.Commands.CreatePackageAssignment;

public class CreatePackageAssignmentCommandHandler : IRequestHandler<CreatePackageAssignmentCommand, CreatePackageAssignmentCommandResult>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISmsSender _smsSender;
    private readonly IPushNotificationSender _pushNotificationSender;

    public CreatePackageAssignmentCommandHandler(IUnitOfWork unitOfWork, ISmsSender smsSender, IPushNotificationSender pushNotificationSender)
    {
        _unitOfWork = unitOfWork;
        _smsSender = smsSender;
        _pushNotificationSender = pushNotificationSender;
    }

    public async Task<CreatePackageAssignmentCommandResult> Handle(CreatePackageAssignmentCommand request, CancellationToken cancellationToken)
    {
        var package = await _unitOfWork.GetReadRepository<Package>()
            .GetAsync(p => p.Id == request.PackageId, cancellationToken: cancellationToken);
        if (package is null)
        {
            throw new NotFoundException($"Paket {request.PackageId} bulunamadı.");
        }

        // A company-wide package (BranchId == null) may only be assigned by a
        // GymAdmin/SuperAdmin, same limit as creating one - a BranchManager may
        // only assign a package scoped to their own exact branch.
        var callerAssignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
            a => a.UserId == request.RequestedByUserId && a.IsActive, cancellationToken: cancellationToken);
        var callerIsAuthorized = package.BranchId is null
            ? callerAssignments.Any(a =>
                a.Role == AssignmentRole.SuperAdmin ||
                (a.Role == AssignmentRole.GymAdmin && a.CompanyId == package.CompanyId))
            : callerAssignments.Any(a =>
                a.Role == AssignmentRole.SuperAdmin ||
                (a.Role == AssignmentRole.GymAdmin && a.CompanyId == package.CompanyId) ||
                (a.Role == AssignmentRole.BranchManager && a.BranchId == package.BranchId));
        if (!callerIsAuthorized)
        {
            throw new ForbiddenException("Bu paketi atama yetkiniz yok.");
        }

        var member = await _unitOfWork.GetReadRepository<User>()
            .GetAsync(u => u.Phone == request.MemberPhone, cancellationToken: cancellationToken);
        if (member is null)
        {
            throw new NotFoundException($"'{request.MemberPhone}' numaralı kayıtlı bir kullanıcı bulunamadı.");
        }

        var alreadyHasThisPackage = await _unitOfWork.GetReadRepository<PackageAssignment>().AnyAsync(
            pa => pa.MemberUserId == member.Id && pa.PackageId == package.Id && pa.Status != PackageAssignmentStatus.Cancelled,
            cancellationToken);
        if (alreadyHasThisPackage)
        {
            throw new MemberAlreadyHasThisPackageException();
        }

        var code = await PackageAssignmentInvitationService.IssueAsync(
            _unitOfWork, member.Id, package.Id, package.CompanyId, package.BranchId, request.RequestedByUserId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _smsSender.SendAsync(
            request.MemberPhone,
            $"GymApp'te '{package.Name}' paketi size tanımlanmak üzere. Onay kodu: {code} (10 dakika geçerli).",
            cancellationToken);
        await NotificationDispatcher.NotifyUserAsync(
            _unitOfWork, _pushNotificationSender, member.Id,
            "Yeni paket daveti",
            "Bir paket size tanımlanmak üzere. Telefonunuza gelen kodla onaylayabilirsiniz.",
            cancellationToken);

        return new CreatePackageAssignmentCommandResult { UserId = member.Id, PackageId = package.Id };
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Tests/GymAppApi.UnitTests --filter "FullyQualifiedName~CreatePackageAssignmentCommandHandlerTests"`
Expected: `Passed! - Failed: 0, Passed: 5`

- [ ] **Step 5: Commit**

```bash
git add Core/GymAppApi.Application/Features/Packages/Exceptions/MemberAlreadyHasThisPackageException.cs Core/GymAppApi.Application/Features/Packages/Commands/CreatePackageAssignment/ Tests/GymAppApi.UnitTests/Features/Packages/CreatePackageAssignmentCommandHandlerTests.cs
git commit -m "Add CreatePackageAssignmentCommand"
```

---

## Task 12: `ConfirmPackageAssignmentCommand`

**Files:**
- Create: `Core/GymAppApi.Application/Features/Packages/Exceptions/InvalidPackageAssignmentInvitationCodeException.cs`
- Create: `Core/GymAppApi.Application/Features/Packages/Commands/ConfirmPackageAssignment/ConfirmPackageAssignmentCommand.cs`
- Create: `Core/GymAppApi.Application/Features/Packages/Commands/ConfirmPackageAssignment/ConfirmPackageAssignmentCommandValidator.cs`
- Create: `Core/GymAppApi.Application/Features/Packages/Commands/ConfirmPackageAssignment/ConfirmPackageAssignmentCommandResult.cs`
- Create: `Core/GymAppApi.Application/Features/Packages/Commands/ConfirmPackageAssignment/ConfirmPackageAssignmentCommandHandler.cs`
- Test: `Tests/GymAppApi.UnitTests/Features/Packages/ConfirmPackageAssignmentCommandHandlerTests.cs`

Mirrors `ConfirmAssignmentInvitationCommandHandler` exactly, minus the cross-role check (not applicable to Members). On confirm, `StartDate = now`, `EndDate` computed from `Package.DurationDays`, `RemainingSessions` copied from `Package.SessionCount`.

- [ ] **Step 1: Write the failing tests**

```csharp
// Tests/GymAppApi.UnitTests/Features/Packages/ConfirmPackageAssignmentCommandHandlerTests.cs
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Packages.Commands.ConfirmPackageAssignment;
using GymAppApi.Application.Features.Packages.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Packages;

public class ConfirmPackageAssignmentCommandHandlerTests
{
    private const int TargetUserId = 7;

    private static (Mock<IUnitOfWork> uow, Mock<IWriteRepository<PendingPackageAssignmentInvitation>> invitationWriteRepo, Mock<IWriteRepository<PackageAssignment>> assignmentWriteRepo) Wire(
        IReadOnlyList<PendingPackageAssignmentInvitation> liveInvitations, Package? package, bool alreadyAssigned = false)
    {
        var invitationReadRepo = new Mock<IReadRepository<PendingPackageAssignmentInvitation>>();
        invitationReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<PendingPackageAssignmentInvitation, bool>>>(), null, null, false, default))
            .ReturnsAsync(liveInvitations);
        var invitationWriteRepo = new Mock<IWriteRepository<PendingPackageAssignmentInvitation>>();

        var packageReadRepo = new Mock<IReadRepository<Package>>();
        packageReadRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Package, bool>>>(), null, false, default))
            .ReturnsAsync(package);

        var assignmentReadRepo = new Mock<IReadRepository<PackageAssignment>>();
        assignmentReadRepo.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<PackageAssignment, bool>>>(), default))
            .ReturnsAsync(alreadyAssigned);
        var assignmentWriteRepo = new Mock<IWriteRepository<PackageAssignment>>();

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<PendingPackageAssignmentInvitation>()).Returns(invitationReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<PendingPackageAssignmentInvitation>()).Returns(invitationWriteRepo.Object);
        uow.Setup(u => u.GetReadRepository<Package>()).Returns(packageReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<PackageAssignment>()).Returns(assignmentReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<PackageAssignment>()).Returns(assignmentWriteRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        return (uow, invitationWriteRepo, assignmentWriteRepo);
    }

    private static PendingPackageAssignmentInvitation LiveInvitation(string code = "123456", int attemptCount = 0) => new()
    {
        Id = 1,
        TargetUserId = TargetUserId,
        PackageId = 5,
        CompanyId = 1,
        BranchId = 10,
        RequestedByUserId = 42,
        Code = code,
        IsUsed = false,
        ExpiresAt = DateTime.UtcNow.AddMinutes(5),
        AttemptCount = attemptCount,
    };

    private static Package DurationPackage() => new() { Id = 5, CompanyId = 1, BranchId = 10, Name = "Aylık Üyelik", Type = PackageType.Duration, DurationDays = 30, IsActive = true };

    [Fact]
    public async Task Handle_WhenCodeMatchesALiveInvitation_CreatesThePackageAssignmentWithComputedEndDate()
    {
        var invitation = LiveInvitation();
        var (uow, invitationWriteRepo, assignmentWriteRepo) = Wire(new List<PendingPackageAssignmentInvitation> { invitation }, DurationPackage());
        var handler = new ConfirmPackageAssignmentCommandHandler(uow.Object);

        var result = await handler.Handle(new ConfirmPackageAssignmentCommand { Code = "123456", UserId = TargetUserId }, CancellationToken.None);

        Assert.Equal(5, result.PackageId);
        Assert.True(invitation.IsUsed);
        invitationWriteRepo.Verify(r => r.Update(invitation), Times.Once);
        assignmentWriteRepo.Verify(r => r.AddAsync(It.Is<PackageAssignment>(a =>
            a.MemberUserId == TargetUserId && a.PackageId == 5 && a.CompanyId == 1 && a.BranchId == 10 &&
            a.Status == PackageAssignmentStatus.Active && a.EndDate != null), default), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenNoLiveInvitationMatchesTheCode_ThrowsInvalidPackageAssignmentInvitationCodeExceptionAndBurnsAnAttempt()
    {
        var invitation = LiveInvitation(code: "123456");
        var (uow, invitationWriteRepo, assignmentWriteRepo) = Wire(new List<PendingPackageAssignmentInvitation> { invitation }, DurationPackage());
        var handler = new ConfirmPackageAssignmentCommandHandler(uow.Object);

        await Assert.ThrowsAsync<InvalidPackageAssignmentInvitationCodeException>(() =>
            handler.Handle(new ConfirmPackageAssignmentCommand { Code = "999999", UserId = TargetUserId }, CancellationToken.None));

        Assert.Equal(1, invitation.AttemptCount);
        assignmentWriteRepo.Verify(r => r.AddAsync(It.IsAny<PackageAssignment>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenAlreadyAssignedToThatPackage_MarksInvitationUsedAndThrowsMemberAlreadyHasThisPackageException()
    {
        var invitation = LiveInvitation();
        var (uow, invitationWriteRepo, assignmentWriteRepo) = Wire(new List<PendingPackageAssignmentInvitation> { invitation }, DurationPackage(), alreadyAssigned: true);
        var handler = new ConfirmPackageAssignmentCommandHandler(uow.Object);

        await Assert.ThrowsAsync<MemberAlreadyHasThisPackageException>(() =>
            handler.Handle(new ConfirmPackageAssignmentCommand { Code = "123456", UserId = TargetUserId }, CancellationToken.None));

        Assert.True(invitation.IsUsed);
        assignmentWriteRepo.Verify(r => r.AddAsync(It.IsAny<PackageAssignment>(), default), Times.Never);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Tests/GymAppApi.UnitTests --filter "FullyQualifiedName~ConfirmPackageAssignmentCommandHandlerTests"`
Expected: FAIL to compile.

- [ ] **Step 3: Implement**

```csharp
// Core/GymAppApi.Application/Features/Packages/Exceptions/InvalidPackageAssignmentInvitationCodeException.cs
using GymAppApi.Application.Common.Exceptions;

namespace GymAppApi.Application.Features.Packages.Exceptions;

public class InvalidPackageAssignmentInvitationCodeException : UnauthorizedException
{
    public InvalidPackageAssignmentInvitationCodeException() : base("Kod hatalı, süresi dolmuş veya çok fazla deneme yapıldı.") { }
}
```

```csharp
// Core/GymAppApi.Application/Features/Packages/Commands/ConfirmPackageAssignment/ConfirmPackageAssignmentCommand.cs
using MediatR;

namespace GymAppApi.Application.Features.Packages.Commands.ConfirmPackageAssignment;

public class ConfirmPackageAssignmentCommand : IRequest<ConfirmPackageAssignmentCommandResult>
{
    public string Code { get; set; } = null!;

    // Set by the controller from the caller's own JWT sub claim - only the
    // invited member themselves can confirm an invitation addressed to them.
    public int UserId { get; set; }
}
```

```csharp
// Core/GymAppApi.Application/Features/Packages/Commands/ConfirmPackageAssignment/ConfirmPackageAssignmentCommandValidator.cs
using FluentValidation;

namespace GymAppApi.Application.Features.Packages.Commands.ConfirmPackageAssignment;

public class ConfirmPackageAssignmentCommandValidator : AbstractValidator<ConfirmPackageAssignmentCommand>
{
    public ConfirmPackageAssignmentCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().Length(6);
    }
}
```

```csharp
// Core/GymAppApi.Application/Features/Packages/Commands/ConfirmPackageAssignment/ConfirmPackageAssignmentCommandResult.cs
namespace GymAppApi.Application.Features.Packages.Commands.ConfirmPackageAssignment;

public class ConfirmPackageAssignmentCommandResult
{
    public int PackageAssignmentId { get; set; }
    public int PackageId { get; set; }
    public DateTime? EndDate { get; set; }
}
```

```csharp
// Core/GymAppApi.Application/Features/Packages/Commands/ConfirmPackageAssignment/ConfirmPackageAssignmentCommandHandler.cs
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.Invitations;
using GymAppApi.Application.Features.Packages.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.Packages.Commands.ConfirmPackageAssignment;

public class ConfirmPackageAssignmentCommandHandler : IRequestHandler<ConfirmPackageAssignmentCommand, ConfirmPackageAssignmentCommandResult>
{
    private readonly IUnitOfWork _unitOfWork;

    public ConfirmPackageAssignmentCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<ConfirmPackageAssignmentCommandResult> Handle(ConfirmPackageAssignmentCommand request, CancellationToken cancellationToken)
    {
        var invitationWriteRepo = _unitOfWork.GetWriteRepository<PendingPackageAssignmentInvitation>();
        var now = DateTime.UtcNow;

        var liveInvitations = await _unitOfWork.GetReadRepository<PendingPackageAssignmentInvitation>().GetAllAsync(
            p => p.TargetUserId == request.UserId && !p.IsUsed && p.ExpiresAt > now, cancellationToken: cancellationToken);

        var matching = liveInvitations.FirstOrDefault(p => p.Code == request.Code && p.AttemptCount < PackageAssignmentInvitationService.MaxAttempts);
        if (matching is null)
        {
            foreach (var invitation in liveInvitations.Where(p => p.AttemptCount < PackageAssignmentInvitationService.MaxAttempts))
            {
                invitation.AttemptCount += 1;
                invitationWriteRepo.Update(invitation);
            }
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            throw new InvalidPackageAssignmentInvitationCodeException();
        }

        matching.IsUsed = true;
        invitationWriteRepo.Update(matching);

        // Defense in depth, same rationale as ConfirmAssignmentInvitationCommandHandler:
        // something else could have assigned this package to the member in the
        // meantime. The invitation is consumed either way.
        var alreadyAssigned = await _unitOfWork.GetReadRepository<PackageAssignment>().AnyAsync(
            pa => pa.MemberUserId == matching.TargetUserId && pa.PackageId == matching.PackageId &&
                  pa.Status != PackageAssignmentStatus.Cancelled, cancellationToken);
        if (alreadyAssigned)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            throw new MemberAlreadyHasThisPackageException();
        }

        var package = await _unitOfWork.GetReadRepository<Package>()
            .GetAsync(p => p.Id == matching.PackageId, cancellationToken: cancellationToken);

        var assignment = new PackageAssignment
        {
            PackageId = matching.PackageId,
            MemberUserId = matching.TargetUserId,
            CompanyId = matching.CompanyId,
            BranchId = matching.BranchId,
            AssignedByUserId = matching.RequestedByUserId,
            StartDate = now,
            EndDate = package?.DurationDays is int days ? now.AddDays(days) : null,
            RemainingSessions = package?.SessionCount,
            Status = PackageAssignmentStatus.Active,
        };
        await _unitOfWork.GetWriteRepository<PackageAssignment>().AddAsync(assignment, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new ConfirmPackageAssignmentCommandResult
        {
            PackageAssignmentId = assignment.Id,
            PackageId = assignment.PackageId,
            EndDate = assignment.EndDate,
        };
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Tests/GymAppApi.UnitTests --filter "FullyQualifiedName~ConfirmPackageAssignmentCommandHandlerTests"`
Expected: `Passed! - Failed: 0, Passed: 3`

- [ ] **Step 5: Commit**

```bash
git add Core/GymAppApi.Application/Features/Packages/Exceptions/InvalidPackageAssignmentInvitationCodeException.cs Core/GymAppApi.Application/Features/Packages/Commands/ConfirmPackageAssignment/ Tests/GymAppApi.UnitTests/Features/Packages/ConfirmPackageAssignmentCommandHandlerTests.cs
git commit -m "Add ConfirmPackageAssignmentCommand"
```

---

## Task 13: `FreezePackageAssignmentCommand` + `UnfreezePackageAssignmentCommand`

**Files:**
- Create: `Core/GymAppApi.Application/Features/Packages/Commands/FreezePackageAssignment/FreezePackageAssignmentCommand.cs`
- Create: `Core/GymAppApi.Application/Features/Packages/Commands/FreezePackageAssignment/FreezePackageAssignmentCommandHandler.cs`
- Create: `Core/GymAppApi.Application/Features/Packages/Commands/UnfreezePackageAssignment/UnfreezePackageAssignmentCommand.cs`
- Create: `Core/GymAppApi.Application/Features/Packages/Commands/UnfreezePackageAssignment/UnfreezePackageAssignmentCommandHandler.cs`
- Test: `Tests/GymAppApi.UnitTests/Features/Packages/FreezePackageAssignmentCommandHandlerTests.cs`
- Test: `Tests/GymAppApi.UnitTests/Features/Packages/UnfreezePackageAssignmentCommandHandlerTests.cs`

Authorization for both: GymAdmin (of the assignment's `CompanyId`) or BranchManager (of the assignment's exact `BranchId`) or SuperAdmin — mirrors `AddStaffMemberCommandHandler`'s non-BranchManager-role authorization branch.

- [ ] **Step 1: Write the failing tests**

```csharp
// Tests/GymAppApi.UnitTests/Features/Packages/FreezePackageAssignmentCommandHandlerTests.cs
using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Packages.Commands.FreezePackageAssignment;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Packages;

public class FreezePackageAssignmentCommandHandlerTests
{
    private const int CallerId = 42;

    private static (Mock<IUnitOfWork> uow, Mock<IWriteRepository<PackageAssignment>> writeRepo) Wire(PackageAssignment? assignment, IReadOnlyList<Assignment> callerAssignments)
    {
        var assignmentReadRepo = new Mock<IReadRepository<PackageAssignment>>();
        assignmentReadRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<PackageAssignment, bool>>>(), null, false, default))
            .ReturnsAsync(assignment);
        var writeRepo = new Mock<IWriteRepository<PackageAssignment>>();

        var callerReadRepo = new Mock<IReadRepository<Assignment>>();
        callerReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Assignment, bool>>>(), null, null, false, default))
            .ReturnsAsync(callerAssignments);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<PackageAssignment>()).Returns(assignmentReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<PackageAssignment>()).Returns(writeRepo.Object);
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(callerReadRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        return (uow, writeRepo);
    }

    [Fact]
    public async Task Handle_WhenPackageAssignmentDoesNotExist_ThrowsNotFoundException()
    {
        var (uow, _) = Wire(assignment: null, callerAssignments: new List<Assignment>());
        var handler = new FreezePackageAssignmentCommandHandler(uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new FreezePackageAssignmentCommand { PackageAssignmentId = 1, RequestedByUserId = CallerId }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenCallerIsGymAdminOfTheAssignmentsCompany_SetsStatusFrozenAndFrozenAt()
    {
        var assignment = new PackageAssignment { Id = 1, CompanyId = 1, BranchId = 10, PackageId = 5, MemberUserId = 7, Status = PackageAssignmentStatus.Active };
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, writeRepo) = Wire(assignment, callerAssignments);
        var handler = new FreezePackageAssignmentCommandHandler(uow.Object);

        await handler.Handle(new FreezePackageAssignmentCommand { PackageAssignmentId = 1, RequestedByUserId = CallerId }, CancellationToken.None);

        Assert.Equal(PackageAssignmentStatus.Frozen, assignment.Status);
        Assert.NotNull(assignment.FrozenAt);
        writeRepo.Verify(r => r.Update(assignment), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCallerHasNoRelevantAssignment_ThrowsForbiddenException()
    {
        var assignment = new PackageAssignment { Id = 1, CompanyId = 1, BranchId = 10, PackageId = 5, MemberUserId = 7, Status = PackageAssignmentStatus.Active };
        var (uow, writeRepo) = Wire(assignment, callerAssignments: new List<Assignment>());
        var handler = new FreezePackageAssignmentCommandHandler(uow.Object);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new FreezePackageAssignmentCommand { PackageAssignmentId = 1, RequestedByUserId = CallerId }, CancellationToken.None));
        writeRepo.Verify(r => r.Update(It.IsAny<PackageAssignment>()), Times.Never);
    }
}
```

```csharp
// Tests/GymAppApi.UnitTests/Features/Packages/UnfreezePackageAssignmentCommandHandlerTests.cs
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Packages.Commands.UnfreezePackageAssignment;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Packages;

public class UnfreezePackageAssignmentCommandHandlerTests
{
    private const int CallerId = 42;

    private static (Mock<IUnitOfWork> uow, Mock<IWriteRepository<PackageAssignment>> writeRepo) Wire(PackageAssignment assignment, IReadOnlyList<Assignment> callerAssignments)
    {
        var assignmentReadRepo = new Mock<IReadRepository<PackageAssignment>>();
        assignmentReadRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<PackageAssignment, bool>>>(), null, false, default))
            .ReturnsAsync(assignment);
        var writeRepo = new Mock<IWriteRepository<PackageAssignment>>();

        var callerReadRepo = new Mock<IReadRepository<Assignment>>();
        callerReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Assignment, bool>>>(), null, null, false, default))
            .ReturnsAsync(callerAssignments);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<PackageAssignment>()).Returns(assignmentReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<PackageAssignment>()).Returns(writeRepo.Object);
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(callerReadRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        return (uow, writeRepo);
    }

    [Fact]
    public async Task Handle_WhenFrozenWithAnEndDate_PushesEndDateForwardByTheFrozenDurationAndClearsFrozenAt()
    {
        var frozenAt = DateTime.UtcNow.AddDays(-5);
        var originalEndDate = DateTime.UtcNow.AddDays(10);
        var assignment = new PackageAssignment
        {
            Id = 1, CompanyId = 1, BranchId = 10, PackageId = 5, MemberUserId = 7,
            Status = PackageAssignmentStatus.Frozen, FrozenAt = frozenAt, EndDate = originalEndDate,
        };
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, writeRepo) = Wire(assignment, callerAssignments);
        var handler = new UnfreezePackageAssignmentCommandHandler(uow.Object);

        await handler.Handle(new UnfreezePackageAssignmentCommand { PackageAssignmentId = 1, RequestedByUserId = CallerId }, CancellationToken.None);

        Assert.Equal(PackageAssignmentStatus.Active, assignment.Status);
        Assert.Null(assignment.FrozenAt);
        Assert.True(assignment.EndDate > originalEndDate);
        writeRepo.Verify(r => r.Update(assignment), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenFrozenWithNoEndDate_LeavesEndDateNull()
    {
        var assignment = new PackageAssignment
        {
            Id = 1, CompanyId = 1, BranchId = 10, PackageId = 5, MemberUserId = 7,
            Status = PackageAssignmentStatus.Frozen, FrozenAt = DateTime.UtcNow.AddDays(-5), EndDate = null,
        };
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, Role = AssignmentRole.GymAdmin, IsActive = true } };
        var (uow, _) = Wire(assignment, callerAssignments);
        var handler = new UnfreezePackageAssignmentCommandHandler(uow.Object);

        await handler.Handle(new UnfreezePackageAssignmentCommand { PackageAssignmentId = 1, RequestedByUserId = CallerId }, CancellationToken.None);

        Assert.Equal(PackageAssignmentStatus.Active, assignment.Status);
        Assert.Null(assignment.EndDate);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Tests/GymAppApi.UnitTests --filter "FullyQualifiedName~FreezePackageAssignmentCommandHandlerTests|FullyQualifiedName~UnfreezePackageAssignmentCommandHandlerTests"`
Expected: FAIL to compile.

- [ ] **Step 3: Implement**

```csharp
// Core/GymAppApi.Application/Features/Packages/Commands/FreezePackageAssignment/FreezePackageAssignmentCommand.cs
using MediatR;

namespace GymAppApi.Application.Features.Packages.Commands.FreezePackageAssignment;

public class FreezePackageAssignmentCommand : IRequest
{
    // Set by the controller from the route segment.
    public int PackageAssignmentId { get; set; }
    // Set by the controller from the caller's own JWT sub claim.
    public int RequestedByUserId { get; set; }
}
```

```csharp
// Core/GymAppApi.Application/Features/Packages/Commands/FreezePackageAssignment/FreezePackageAssignmentCommandHandler.cs
using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.Packages.Commands.FreezePackageAssignment;

public class FreezePackageAssignmentCommandHandler : IRequestHandler<FreezePackageAssignmentCommand>
{
    private readonly IUnitOfWork _unitOfWork;

    public FreezePackageAssignmentCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task Handle(FreezePackageAssignmentCommand request, CancellationToken cancellationToken)
    {
        var assignment = await _unitOfWork.GetReadRepository<PackageAssignment>()
            .GetAsync(a => a.Id == request.PackageAssignmentId, cancellationToken: cancellationToken);
        if (assignment is null)
        {
            throw new NotFoundException($"Paket ataması {request.PackageAssignmentId} bulunamadı.");
        }

        var callerAssignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
            a => a.UserId == request.RequestedByUserId && a.IsActive, cancellationToken: cancellationToken);
        var callerIsAuthorized = callerAssignments.Any(a =>
            a.Role == AssignmentRole.SuperAdmin ||
            (a.Role == AssignmentRole.GymAdmin && a.CompanyId == assignment.CompanyId) ||
            (a.Role == AssignmentRole.BranchManager && a.BranchId == assignment.BranchId));
        if (!callerIsAuthorized)
        {
            throw new ForbiddenException("Bu paket atamasını dondurma yetkiniz yok.");
        }

        assignment.Status = PackageAssignmentStatus.Frozen;
        assignment.FrozenAt = DateTime.UtcNow;
        _unitOfWork.GetWriteRepository<PackageAssignment>().Update(assignment);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
```

```csharp
// Core/GymAppApi.Application/Features/Packages/Commands/UnfreezePackageAssignment/UnfreezePackageAssignmentCommand.cs
using MediatR;

namespace GymAppApi.Application.Features.Packages.Commands.UnfreezePackageAssignment;

public class UnfreezePackageAssignmentCommand : IRequest
{
    public int PackageAssignmentId { get; set; }
    public int RequestedByUserId { get; set; }
}
```

```csharp
// Core/GymAppApi.Application/Features/Packages/Commands/UnfreezePackageAssignment/UnfreezePackageAssignmentCommandHandler.cs
using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.Packages.Commands.UnfreezePackageAssignment;

public class UnfreezePackageAssignmentCommandHandler : IRequestHandler<UnfreezePackageAssignmentCommand>
{
    private readonly IUnitOfWork _unitOfWork;

    public UnfreezePackageAssignmentCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task Handle(UnfreezePackageAssignmentCommand request, CancellationToken cancellationToken)
    {
        var assignment = await _unitOfWork.GetReadRepository<PackageAssignment>()
            .GetAsync(a => a.Id == request.PackageAssignmentId, cancellationToken: cancellationToken);
        if (assignment is null)
        {
            throw new NotFoundException($"Paket ataması {request.PackageAssignmentId} bulunamadı.");
        }

        var callerAssignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
            a => a.UserId == request.RequestedByUserId && a.IsActive, cancellationToken: cancellationToken);
        var callerIsAuthorized = callerAssignments.Any(a =>
            a.Role == AssignmentRole.SuperAdmin ||
            (a.Role == AssignmentRole.GymAdmin && a.CompanyId == assignment.CompanyId) ||
            (a.Role == AssignmentRole.BranchManager && a.BranchId == assignment.BranchId));
        if (!callerIsAuthorized)
        {
            throw new ForbiddenException("Bu paket atamasını aktifleştirme yetkiniz yok.");
        }

        var now = DateTime.UtcNow;
        if (assignment.EndDate is not null && assignment.FrozenAt is not null)
        {
            assignment.EndDate = assignment.EndDate.Value.Add(now - assignment.FrozenAt.Value);
        }
        assignment.Status = PackageAssignmentStatus.Active;
        assignment.FrozenAt = null;
        _unitOfWork.GetWriteRepository<PackageAssignment>().Update(assignment);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Tests/GymAppApi.UnitTests --filter "FullyQualifiedName~FreezePackageAssignmentCommandHandlerTests|FullyQualifiedName~UnfreezePackageAssignmentCommandHandlerTests"`
Expected: `Passed! - Failed: 0, Passed: 5`

- [ ] **Step 5: Commit**

```bash
git add Core/GymAppApi.Application/Features/Packages/Commands/FreezePackageAssignment/ Core/GymAppApi.Application/Features/Packages/Commands/UnfreezePackageAssignment/ Tests/GymAppApi.UnitTests/Features/Packages/FreezePackageAssignmentCommandHandlerTests.cs Tests/GymAppApi.UnitTests/Features/Packages/UnfreezePackageAssignmentCommandHandlerTests.cs
git commit -m "Add Freeze/UnfreezePackageAssignmentCommand"
```

---

## Task 14: `CancelPackageAssignmentCommand`

**Files:**
- Create: `Core/GymAppApi.Application/Features/Packages/Commands/CancelPackageAssignment/CancelPackageAssignmentCommand.cs`
- Create: `Core/GymAppApi.Application/Features/Packages/Commands/CancelPackageAssignment/CancelPackageAssignmentCommandHandler.cs`
- Test: `Tests/GymAppApi.UnitTests/Features/Packages/CancelPackageAssignmentCommandHandlerTests.cs`

Same authorization pattern as Freeze/Unfreeze.

- [ ] **Step 1: Write the failing tests**

```csharp
// Tests/GymAppApi.UnitTests/Features/Packages/CancelPackageAssignmentCommandHandlerTests.cs
using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Packages.Commands.CancelPackageAssignment;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Packages;

public class CancelPackageAssignmentCommandHandlerTests
{
    private const int CallerId = 42;

    private static (Mock<IUnitOfWork> uow, Mock<IWriteRepository<PackageAssignment>> writeRepo) Wire(PackageAssignment? assignment, IReadOnlyList<Assignment> callerAssignments)
    {
        var assignmentReadRepo = new Mock<IReadRepository<PackageAssignment>>();
        assignmentReadRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<PackageAssignment, bool>>>(), null, false, default))
            .ReturnsAsync(assignment);
        var writeRepo = new Mock<IWriteRepository<PackageAssignment>>();

        var callerReadRepo = new Mock<IReadRepository<Assignment>>();
        callerReadRepo.Setup(r => r.GetAllAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Assignment, bool>>>(), null, null, false, default))
            .ReturnsAsync(callerAssignments);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<PackageAssignment>()).Returns(assignmentReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<PackageAssignment>()).Returns(writeRepo.Object);
        uow.Setup(u => u.GetReadRepository<Assignment>()).Returns(callerReadRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        return (uow, writeRepo);
    }

    [Fact]
    public async Task Handle_WhenPackageAssignmentDoesNotExist_ThrowsNotFoundException()
    {
        var (uow, _) = Wire(assignment: null, callerAssignments: new List<Assignment>());
        var handler = new CancelPackageAssignmentCommandHandler(uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new CancelPackageAssignmentCommand { PackageAssignmentId = 1, RequestedByUserId = CallerId }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenCallerIsBranchManagerOfTheAssignmentsBranch_SetsStatusCancelled()
    {
        var assignment = new PackageAssignment { Id = 1, CompanyId = 1, BranchId = 10, PackageId = 5, MemberUserId = 7, Status = PackageAssignmentStatus.Active };
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, BranchId = 10, Role = AssignmentRole.BranchManager, IsActive = true } };
        var (uow, writeRepo) = Wire(assignment, callerAssignments);
        var handler = new CancelPackageAssignmentCommandHandler(uow.Object);

        await handler.Handle(new CancelPackageAssignmentCommand { PackageAssignmentId = 1, RequestedByUserId = CallerId }, CancellationToken.None);

        Assert.Equal(PackageAssignmentStatus.Cancelled, assignment.Status);
        writeRepo.Verify(r => r.Update(assignment), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCallerIsBranchManagerOfADifferentBranch_ThrowsForbiddenException()
    {
        var assignment = new PackageAssignment { Id = 1, CompanyId = 1, BranchId = 10, PackageId = 5, MemberUserId = 7, Status = PackageAssignmentStatus.Active };
        var callerAssignments = new List<Assignment> { new() { UserId = CallerId, CompanyId = 1, BranchId = 999, Role = AssignmentRole.BranchManager, IsActive = true } };
        var (uow, writeRepo) = Wire(assignment, callerAssignments);
        var handler = new CancelPackageAssignmentCommandHandler(uow.Object);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new CancelPackageAssignmentCommand { PackageAssignmentId = 1, RequestedByUserId = CallerId }, CancellationToken.None));
        writeRepo.Verify(r => r.Update(It.IsAny<PackageAssignment>()), Times.Never);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Tests/GymAppApi.UnitTests --filter "FullyQualifiedName~CancelPackageAssignmentCommandHandlerTests"`
Expected: FAIL to compile.

- [ ] **Step 3: Implement**

```csharp
// Core/GymAppApi.Application/Features/Packages/Commands/CancelPackageAssignment/CancelPackageAssignmentCommand.cs
using MediatR;

namespace GymAppApi.Application.Features.Packages.Commands.CancelPackageAssignment;

public class CancelPackageAssignmentCommand : IRequest
{
    public int PackageAssignmentId { get; set; }
    public int RequestedByUserId { get; set; }
}
```

```csharp
// Core/GymAppApi.Application/Features/Packages/Commands/CancelPackageAssignment/CancelPackageAssignmentCommandHandler.cs
using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.Packages.Commands.CancelPackageAssignment;

public class CancelPackageAssignmentCommandHandler : IRequestHandler<CancelPackageAssignmentCommand>
{
    private readonly IUnitOfWork _unitOfWork;

    public CancelPackageAssignmentCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task Handle(CancelPackageAssignmentCommand request, CancellationToken cancellationToken)
    {
        var assignment = await _unitOfWork.GetReadRepository<PackageAssignment>()
            .GetAsync(a => a.Id == request.PackageAssignmentId, cancellationToken: cancellationToken);
        if (assignment is null)
        {
            throw new NotFoundException($"Paket ataması {request.PackageAssignmentId} bulunamadı.");
        }

        var callerAssignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
            a => a.UserId == request.RequestedByUserId && a.IsActive, cancellationToken: cancellationToken);
        var callerIsAuthorized = callerAssignments.Any(a =>
            a.Role == AssignmentRole.SuperAdmin ||
            (a.Role == AssignmentRole.GymAdmin && a.CompanyId == assignment.CompanyId) ||
            (a.Role == AssignmentRole.BranchManager && a.BranchId == assignment.BranchId));
        if (!callerIsAuthorized)
        {
            throw new ForbiddenException("Bu paket atamasını iptal etme yetkiniz yok.");
        }

        assignment.Status = PackageAssignmentStatus.Cancelled;
        _unitOfWork.GetWriteRepository<PackageAssignment>().Update(assignment);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Tests/GymAppApi.UnitTests --filter "FullyQualifiedName~CancelPackageAssignmentCommandHandlerTests"`
Expected: `Passed! - Failed: 0, Passed: 3`

- [ ] **Step 5: Commit**

```bash
git add Core/GymAppApi.Application/Features/Packages/Commands/CancelPackageAssignment/ Tests/GymAppApi.UnitTests/Features/Packages/CancelPackageAssignmentCommandHandlerTests.cs
git commit -m "Add CancelPackageAssignmentCommand"
```

---

## Task 15: `PackageAssignmentsController`

**Files:**
- Create: `Presentation/GymAppApi.WebApi/Controllers/PackageAssignmentsController.cs`

- [ ] **Step 1: Implement the controller**

```csharp
// Presentation/GymAppApi.WebApi/Controllers/PackageAssignmentsController.cs
using System.IdentityModel.Tokens.Jwt;
using GymAppApi.Application.Features.Packages.Commands.CancelPackageAssignment;
using GymAppApi.Application.Features.Packages.Commands.ConfirmPackageAssignment;
using GymAppApi.Application.Features.Packages.Commands.CreatePackageAssignment;
using GymAppApi.Application.Features.Packages.Commands.FreezePackageAssignment;
using GymAppApi.Application.Features.Packages.Commands.UnfreezePackageAssignment;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymAppApi.WebApi.Controllers;

// Explicit route, not "api/[controller]" - this codebase has no kebab-case
// route token transformer configured (see Program.cs), so the [controller]
// token would resolve to the literal class name "PackageAssignments"
// (/api/packageassignments, no hyphen) instead of the hyphenated path the
// design spec calls for.
[ApiController]
[Route("api/package-assignments")]
public class PackageAssignmentsController : ControllerBase
{
    private readonly IMediator _mediator;

    public PackageAssignmentsController(IMediator mediator) => _mediator = mediator;

    private int CurrentUserId => int.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    [Authorize(Policy = "StaffManagement")]
    [HttpPost]
    public async Task<IActionResult> Create(CreatePackageAssignmentCommand command, CancellationToken cancellationToken)
    {
        command.RequestedByUserId = CurrentUserId;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    // Any authenticated user can call this - it's how the MEMBER (not the
    // inviter) confirms a pending package invitation sent to their own phone.
    [Authorize]
    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm(ConfirmPackageAssignmentCommand command, CancellationToken cancellationToken)
    {
        command.UserId = CurrentUserId;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [Authorize(Policy = "StaffManagement")]
    [HttpPost("{id}/freeze")]
    public async Task<IActionResult> Freeze(int id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new FreezePackageAssignmentCommand { PackageAssignmentId = id, RequestedByUserId = CurrentUserId }, cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = "StaffManagement")]
    [HttpPost("{id}/unfreeze")]
    public async Task<IActionResult> Unfreeze(int id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new UnfreezePackageAssignmentCommand { PackageAssignmentId = id, RequestedByUserId = CurrentUserId }, cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = "StaffManagement")]
    [HttpPost("{id}/cancel")]
    public async Task<IActionResult> Cancel(int id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new CancelPackageAssignmentCommand { PackageAssignmentId = id, RequestedByUserId = CurrentUserId }, cancellationToken);
        return NoContent();
    }
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build`
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add Presentation/GymAppApi.WebApi/Controllers/PackageAssignmentsController.cs
git commit -m "Add PackageAssignmentsController"
```

---

## Task 16: `GetMeQuery` extension — `PackageAssignments`

**Files:**
- Modify: `Core/GymAppApi.Application/Features/Auth/Queries/GetMe/MeResultDto.cs`
- Modify: `Core/GymAppApi.Application/Features/Auth/Queries/GetMe/GetMeQueryHandler.cs`
- Modify: `Tests/GymAppApi.UnitTests/Features/Auth/GetMeQueryHandlerTests.cs`

This is the concrete fulfillment of the module's goal: a Member's `GET /api/auth/me` now lists every company they can see through an active/frozen package.

- [ ] **Step 1: Write the failing test**

Add to `Tests/GymAppApi.UnitTests/Features/Auth/GetMeQueryHandlerTests.cs` (append as a new `[Fact]` in the existing class, after `Handle_MapsActiveAssignmentsWithRoleAsString`):

```csharp
    [Fact]
    public async Task Handle_MapsPackageAssignmentsExcludingCancelledOnes()
    {
        var company = new Company { Id = 3, Name = "MAT & MOVE Kadıköy", IsActive = true };
        var package = new Package { Id = 5, CompanyId = 3, Name = "10 Seans", IsActive = true };
        var user = new User
        {
            Id = 1, FullName = "Ayşe", Phone = "+905551112233", PasswordHash = "x",
            Assignments = new List<Assignment>(),
            PackageAssignments = new List<PackageAssignment>
            {
                new() { Id = 20, MemberUserId = 1, PackageId = 5, Package = package, CompanyId = 3, Company = company, BranchId = null, Status = PackageAssignmentStatus.Active, EndDate = null },
                new() { Id = 21, MemberUserId = 1, PackageId = 5, Package = package, CompanyId = 3, Company = company, Status = PackageAssignmentStatus.Cancelled },
            },
        };
        var userReadRepo = new Mock<IReadRepository<User>>();
        userReadRepo.Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), It.IsAny<Func<IQueryable<User>, Microsoft.EntityFrameworkCore.Query.IIncludableQueryable<User, object>>?>(), false, default))
            .ReturnsAsync(user);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<User>()).Returns(userReadRepo.Object);

        var handler = new GetMeQueryHandler(uow.Object);
        var result = await handler.Handle(new GetMeQuery { UserId = 1 }, CancellationToken.None);

        var pa = Assert.Single(result.PackageAssignments);
        Assert.Equal(3, pa.CompanyId);
        Assert.Equal("MAT & MOVE Kadıköy", pa.CompanyName);
        Assert.Equal(5, pa.PackageId);
        Assert.Equal("10 Seans", pa.PackageName);
        Assert.Equal("Active", pa.Status);
    }
```

The file's existing `using GymAppApi.Domain.Entities;` and `using GymAppApi.Domain.Enums;` already cover `Package`/`PackageAssignment`/`PackageAssignmentStatus` — no new `using` needed.

- [ ] **Step 2: Run tests to verify the new one fails**

Run: `dotnet test Tests/GymAppApi.UnitTests --filter "FullyQualifiedName~GetMeQueryHandlerTests"`
Expected: FAIL to compile (`MeResultDto.PackageAssignments` does not exist).

- [ ] **Step 3: Extend `MeResultDto`**

Find in `Core/GymAppApi.Application/Features/Auth/Queries/GetMe/MeResultDto.cs`:

```csharp
    public IReadOnlyList<MeAssignmentDto> Assignments { get; set; } = new List<MeAssignmentDto>();
}
```

Replace with:

```csharp
    public IReadOnlyList<MeAssignmentDto> Assignments { get; set; } = new List<MeAssignmentDto>();
    public IReadOnlyList<MePackageAssignmentDto> PackageAssignments { get; set; } = new List<MePackageAssignmentDto>();
}
```

Add at the end of the file:

```csharp
public class MePackageAssignmentDto
{
    public int CompanyId { get; set; }
    public string? CompanyName { get; set; }
    public int? BranchId { get; set; }
    public int PackageId { get; set; }
    public string? PackageName { get; set; }
    public string Status { get; set; } = null!;
    public DateTime? EndDate { get; set; }
}
```

- [ ] **Step 4: Extend `GetMeQueryHandler`**

Find in `Core/GymAppApi.Application/Features/Auth/Queries/GetMe/GetMeQueryHandler.cs`:

```csharp
using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
```

Replace with:

```csharp
using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
```

Find:

```csharp
        var user = await _unitOfWork.GetReadRepository<User>().GetAsync(
            u => u.Id == request.UserId,
            include: q => q.Include(u => u.Assignments).ThenInclude(a => a.Company),
            cancellationToken: cancellationToken);
```

Replace with:

```csharp
        var user = await _unitOfWork.GetReadRepository<User>().GetAsync(
            u => u.Id == request.UserId,
            include: q => q.Include(u => u.Assignments).ThenInclude(a => a.Company)
                .Include(u => u.PackageAssignments).ThenInclude(pa => pa.Company)
                .Include(u => u.PackageAssignments).ThenInclude(pa => pa.Package),
            cancellationToken: cancellationToken);
```

Find:

```csharp
            Assignments = user.Assignments
                .Where(a => a.IsActive)
                .Select(a => new MeAssignmentDto
                {
                    CompanyId = a.CompanyId,
                    CompanyName = a.Company?.Name,
                    BranchId = a.BranchId,
                    Role = a.Role.ToString(),
                })
                .ToList(),
        };
```

Replace with:

```csharp
            Assignments = user.Assignments
                .Where(a => a.IsActive)
                .Select(a => new MeAssignmentDto
                {
                    CompanyId = a.CompanyId,
                    CompanyName = a.Company?.Name,
                    BranchId = a.BranchId,
                    Role = a.Role.ToString(),
                })
                .ToList(),
            PackageAssignments = user.PackageAssignments
                .Where(pa => pa.Status != PackageAssignmentStatus.Cancelled)
                .Select(pa => new MePackageAssignmentDto
                {
                    CompanyId = pa.CompanyId,
                    CompanyName = pa.Company?.Name,
                    BranchId = pa.BranchId,
                    PackageId = pa.PackageId,
                    PackageName = pa.Package?.Name,
                    Status = pa.Status.ToString(),
                    EndDate = pa.EndDate,
                })
                .ToList(),
        };
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test Tests/GymAppApi.UnitTests --filter "FullyQualifiedName~GetMeQueryHandlerTests"`
Expected: `Passed! - Failed: 0, Passed: 5`

- [ ] **Step 6: Commit**

```bash
git add Core/GymAppApi.Application/Features/Auth/Queries/GetMe/MeResultDto.cs Core/GymAppApi.Application/Features/Auth/Queries/GetMe/GetMeQueryHandler.cs Tests/GymAppApi.UnitTests/Features/Auth/GetMeQueryHandlerTests.cs
git commit -m "Add PackageAssignments to GetMe response"
```

---

## Task 17: Full-flow integration test

**Files:**
- Create: `Tests/GymAppApi.IntegrationTests/PackageAssignmentFlowTests.cs`

Mirrors `AddStaffMemberAuthorizationTests`'s `SeedAsync`/JWT pattern. Exercises the real HTTP pipeline end to end: create package → assign → confirm → `GET /api/auth/me` shows it.

- [ ] **Step 1: Write the failing tests**

```csharp
// Tests/GymAppApi.IntegrationTests/PackageAssignmentFlowTests.cs
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.Infrastructure.Tenancy;
using GymAppApi.Persistence.Context;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GymAppApi.IntegrationTests;

public class PackageAssignmentFlowTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public PackageAssignmentFlowTests(CustomWebApplicationFactory factory) => _factory = factory;

    private static string UniquePhone() => $"+9055502{Random.Shared.Next(10000, 99999)}";

    private async Task<(int companyId, int branchId, int memberId, string memberPhone, string gymAdminToken, string memberToken)> SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();

        var company = new Company { Name = "Test Co", IsActive = true };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var branch = new Branch { CompanyId = company.Id, Name = "Merkez", Address = "..." };
        db.Branches.Add(branch);
        await db.SaveChangesAsync();

        var memberPhone = UniquePhone();
        var member = new User { FullName = "Member", Phone = memberPhone, PasswordHash = "x" };
        var gymAdmin = new User { FullName = "Gym Admin", Phone = UniquePhone(), PasswordHash = "x" };
        db.Users.AddRange(member, gymAdmin);
        await db.SaveChangesAsync();
        db.Assignments.Add(new Assignment { UserId = gymAdmin.Id, CompanyId = company.Id, Role = AssignmentRole.GymAdmin, IsActive = true });
        await db.SaveChangesAsync();

        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var gymAdminToken = jwtService.GenerateAccessToken(new AccessTokenClaims(gymAdmin.Id, gymAdmin.FullName, gymAdmin.Email, gymAdmin.Phone)).Token;
        var memberToken = jwtService.GenerateAccessToken(new AccessTokenClaims(member.Id, member.FullName, member.Email, member.Phone)).Token;

        return (company.Id, branch.Id, member.Id, memberPhone, gymAdminToken, memberToken);
    }

    [Fact]
    public async Task FullFlow_CreatePackage_Assign_Confirm_ShowsUpOnGetMe()
    {
        var (companyId, branchId, memberId, memberPhone, gymAdminToken, memberToken) = await SeedAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", gymAdminToken);

        var createResponse = await client.PostAsJsonAsync("/api/packages", new
        {
            companyId,
            branchId,
            name = "10 Seans",
            type = "SessionBased",
            sessionCount = 10,
            price = 1000,
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var packageId = created.GetProperty("id").GetInt32();

        var assignResponse = await client.PostAsJsonAsync("/api/package-assignments", new { packageId, memberPhone });
        Assert.Equal(HttpStatusCode.Created, assignResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();
        scope.ServiceProvider.GetRequiredService<AmbientTenantContext>().IsSuperAdmin = true;
        var code = db.PendingPackageAssignmentInvitations.Single(p => p.TargetUserId == memberId).Code;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", memberToken);
        var confirmResponse = await client.PostAsJsonAsync("/api/package-assignments/confirm", new { code });
        Assert.Equal(HttpStatusCode.Created, confirmResponse.StatusCode);

        var meResponse = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        var me = await meResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var packageAssignments = me.GetProperty("packageAssignments");
        Assert.Equal(1, packageAssignments.GetArrayLength());
        Assert.Equal(companyId, packageAssignments[0].GetProperty("companyId").GetInt32());
        Assert.Equal("10 Seans", packageAssignments[0].GetProperty("packageName").GetString());
    }

    [Fact]
    public async Task Create_WithBranchManagerTokenForAnotherBranch_Returns403()
    {
        var (companyId, branchId, _, _, gymAdminToken, _) = await SeedAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymAppApiDbContext>();
        scope.ServiceProvider.GetRequiredService<AmbientTenantContext>().IsSuperAdmin = true;
        var otherBranch = new Branch { CompanyId = companyId, Name = "İkinci Şube", Address = "..." };
        db.Branches.Add(otherBranch);
        var branchManager = new User { FullName = "Branch Manager", Phone = UniquePhone(), PasswordHash = "x" };
        db.Users.Add(branchManager);
        await db.SaveChangesAsync();
        db.Assignments.Add(new Assignment { UserId = branchManager.Id, CompanyId = companyId, BranchId = otherBranch.Id, Role = AssignmentRole.BranchManager, IsActive = true });
        await db.SaveChangesAsync();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var branchManagerToken = jwtService.GenerateAccessToken(new AccessTokenClaims(branchManager.Id, branchManager.FullName, branchManager.Email, branchManager.Phone)).Token;

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", branchManagerToken);
        var response = await client.PostAsJsonAsync("/api/packages", new
        {
            companyId,
            branchId,
            name = "10 Seans",
            type = "SessionBased",
            sessionCount = 10,
            price = 1000,
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Tests/GymAppApi.IntegrationTests --filter "FullyQualifiedName~PackageAssignmentFlowTests"`
Expected: FAIL (endpoints already exist from earlier tasks, so this step mainly confirms the test itself is exercised — if a prior task's code has a defect, the failure shows here).

- [ ] **Step 3: Run tests to verify they pass**

Run: `dotnet test Tests/GymAppApi.IntegrationTests --filter "FullyQualifiedName~PackageAssignmentFlowTests"`
Expected: `Passed! - Failed: 0, Passed: 2`

If either fails, debug against the actual handlers/controllers built in Tasks 7-16 rather than editing this test to match a bug.

- [ ] **Step 4: Run the full test suite**

Run: `dotnet test Tests/GymAppApi.UnitTests` then `dotnet test Tests/GymAppApi.IntegrationTests`
Expected: all green, no regressions in any pre-existing test.

- [ ] **Step 5: Commit**

```bash
git add Tests/GymAppApi.IntegrationTests/PackageAssignmentFlowTests.cs
git commit -m "Add Package core module integration tests"
```

---

## Task 18: Update memory

**Files:**
- Modify: `.claude/memory/project-member-package-linkage-design.md`
- Modify: `.claude/memory/MEMORY.md`

- [ ] **Step 1: Mark step 2 of the roadmap done**

In `.claude/memory/project-member-package-linkage-design.md`, find the line starting with `2. **Package core module**` under "Decomposition and build order" and replace its "Not yet built." ending with a summary of what shipped (commit range, test count) — same style as how step 1 was marked done earlier in that file.

- [ ] **Step 2: Commit**

```bash
git add .claude/memory/project-member-package-linkage-design.md
git commit -m "Record Package core module completion in memory"
```
