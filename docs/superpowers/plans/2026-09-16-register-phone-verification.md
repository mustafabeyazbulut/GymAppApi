# Kayıtta Telefon/E-posta Doğrulama (Backend) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `POST /api/auth/register` (which creates an account with zero proof of phone/email ownership) with a two-step "request-otp / complete" flow where the account is only created after the caller proves ownership of the phone (and email, if given) by entering a code — per `docs/superpowers/specs/2026-09-16-register-phone-verification-design.md`.

**Architecture:** One half of a two-repo feature. The mobile half is planned separately at `C:\Users\MBEYAZBULUT\Documents\GitHub\GymApp\docs\superpowers\plans\2026-09-16-register-phone-verification.md`. That plan's Task 3 (`AuthRepository` changes) is hard-blocked on this plan's Tasks 4–6 (the two new endpoints) existing and reachable. Every new endpoint mirrors the existing ForgotPassword/ResetPassword pair's CQRS/MediatR/FluentValidation/Repository-UnitOfWork shape exactly.

**Tech Stack:** ASP.NET Core 10 / .NET 10, MediatR 14, FluentValidation, EF Core 10 + Npgsql, xUnit + Moq (unit), xUnit + EF Core InMemory (integration) — same stack as the completed Real Auth plan, no new libraries.

---

## Grounding facts confirmed by reading the actual codebase (do not re-derive, do not contradict)

- `User.cs` **already has `public bool PhoneVerified { get; set; }`** (currently always `false`, never set by any handler) — do NOT add a new `IsPhoneVerified` field, reuse this exact one. There is no `EmailVerified` field yet; Task 1 adds it, named `EmailVerified` (not `IsEmailVerified`) to match the existing field's naming style.
- **`RegisterCommandResult` (in `Features/Auth/Commands/Register/RegisterCommandResult.cs`) is reused as the return type of `LoginCommandHandler` AND `RefreshCommandHandler`** (both `using GymAppApi.Application.Features.Auth.Commands.Register;` and return `new RegisterCommandResult { ... }`). This is a real, load-bearing dependency the spec did not mention — deleting the `Register` folder outright (as the spec's "kaldırılıyor" language might suggest) would break Login and Refresh. **Task 3 extracts this shared shape into a new `AuthTokenResult` type before anything in the `Register` folder is touched**, so Login/Refresh point at the new shared type and the old `Register` folder can later be deleted cleanly with nothing left depending on it.
- **`ExceptionMiddleware.cs` maps `FluentValidation.ValidationException` to HTTP `422` (`UnprocessableEntity`), NOT `400`.** Any format-validation failure (bad phone format, non-6-digit code, missing `emailCode` when `email` given) surfaces as `422`, not `400`. `BaseException`-derived exceptions return whatever `StatusCode` they declare.
- `IWriteRepository<T>` has `Remove(T entity)` (not just `AddAsync`/`Update`) — used to delete the consumed `PendingContactVerification` row(s) in Task 5.
- `TransactionBehavior<TRequest,TResponse>` wraps a handler's entire `Handle()` call in one DB transaction **only when the command implements `ITransactionalRequest`**, and rolls back that whole transaction if the handler throws — including any `SaveChangesAsync()` the handler itself called earlier in the same `Handle()` invocation. `ResetPasswordCommand` deliberately does **not** implement `ITransactionalRequest`, precisely because its "wrong code → increment `AttemptCount` → `SaveChangesAsync()` → throw" pattern needs that save to survive the throw, not get rolled back. **`RegisterCompleteCommand` has the exact same requirement for its OTP-attempt-counting** (Task 5) but *also* needs the User+RefreshToken+pending-row-cleanup part to be atomic (like the old `RegisterCommand` needed). Task 5's handler resolves this by NOT implementing `ITransactionalRequest` on the command, and instead manually opening a transaction via `_unitOfWork.BeginTransactionAsync()` only around the atomic success-path segment. Do not "simplify" this by adding `ITransactionalRequest` — that would silently defeat the 5-attempt cap (a wrong-code guess's `AttemptCount` increment would be rolled back every time, giving unlimited retries).
- `AuthController` already has `[EnableRateLimiting("auth")]` at the class level (10 req/min, applies automatically to any new action added to this controller) — this is unrelated to and in addition to the new per-phone/per-email 60s/hourly-5 limit this plan adds inside the handler itself.
- `GymAppApiDbContext.IntentionallyUnscopedEntityTypes` is a `HashSet<Type>` that MUST list any entity with no `ICompanyScoped`/`ITenantScoped` marker interface, or `ApplyGlobalQueryFilters` throws `InvalidOperationException` at startup/migration time. `PendingContactVerification` (no `User` yet to scope to) must be added to it.
- Migration commands run from the repo root: `dotnet ef migrations add <Name> --project Infrastructure/GymAppApi.Persistence --startup-project Presentation/GymAppApi.WebApi`. Last migration so far: `20260915121926_SeedSuperAdmin`.
- Existing exception classes for reference: `Core/GymAppApi.Application/Common/Exceptions/{BaseException,ConflictException,UnauthorizedException,NotFoundException,ForbiddenException}.cs`. There is no `TooManyRequests`(429)-mapped exception yet — Task 2 adds one.

---

## Task 1: Domain + Persistence — `PendingContactVerification`, `ContactChannel`, `User.EmailVerified`, migration

**Files:**
- Create: `Core/GymAppApi.Domain/Enums/ContactChannel.cs`
- Create: `Core/GymAppApi.Domain/Entities/PendingContactVerification.cs`
- Modify: `Core/GymAppApi.Domain/Entities/User.cs`
- Create: `Infrastructure/GymAppApi.Persistence/Configurations/PendingContactVerificationConfiguration.cs`
- Modify: `Infrastructure/GymAppApi.Persistence/Context/GymAppApiDbContext.cs`
- Migration: new file under `Infrastructure/GymAppApi.Persistence/Migrations/` (generated by `dotnet ef`, not hand-written)

No dedicated unit test for this task: `PendingContactVerification` is a plain data holder with no non-trivial defaults to assert (unlike `RefreshToken`'s nullable `RevokedAt`, this entity's `int`/`DateTime` fields all take C#'s ordinary defaults) — its actual behavior is exercised meaningfully by Task 4/5's handler tests instead. This mirrors the Real Auth plan's own code-quality finding that a POCO-defaults-only test is low value.

- [ ] **Step 1: Create the `ContactChannel` enum**

```csharp
// Core/GymAppApi.Domain/Enums/ContactChannel.cs
namespace GymAppApi.Domain.Enums;

public enum ContactChannel { Phone, Email }
```

- [ ] **Step 2: Create the `PendingContactVerification` entity**

```csharp
// Core/GymAppApi.Domain/Entities/PendingContactVerification.cs
using GymAppApi.Domain.Common;
using GymAppApi.Domain.Enums;

namespace GymAppApi.Domain.Entities;

// Not tied to a User: this row exists ONLY during registration, before any
// User exists ("verify first, create second" — see
// docs/superpowers/specs/2026-09-16-register-phone-verification-design.md).
// One active row per (Channel, Target): request-otp upserts it (regenerating
// Code/ExpiresAt/AttemptCount, tracking LastSentAt/SendCount/WindowStartAt
// for the 60s/hourly-5 send limit), complete deletes it once consumed.
public class PendingContactVerification : EntityBase
{
    public ContactChannel Channel { get; set; }
    public string Target { get; set; } = null!;
    public string Code { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
    public int AttemptCount { get; set; }
    public DateTime LastSentAt { get; set; }
    public int SendCount { get; set; }
    public DateTime WindowStartAt { get; set; }
}
```

- [ ] **Step 3: Add `EmailVerified` to `User`**

Open `Core/GymAppApi.Domain/Entities/User.cs`. Find this line:

```csharp
    public bool PhoneVerified { get; set; }
```

Add a new line directly below it:

```csharp
    public bool PhoneVerified { get; set; }
    public bool EmailVerified { get; set; }
```

- [ ] **Step 4: Create the EF configuration**

```csharp
// Infrastructure/GymAppApi.Persistence/Configurations/PendingContactVerificationConfiguration.cs
using GymAppApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GymAppApi.Persistence.Configurations;

public class PendingContactVerificationConfiguration : IEntityTypeConfiguration<PendingContactVerification>
{
    public void Configure(EntityTypeBuilder<PendingContactVerification> builder)
    {
        builder.Property(x => x.Channel).HasConversion<string>().HasMaxLength(10);
        // 320 = RFC 5321 max email length; comfortably covers E.164 phone too (max 16 chars incl. '+').
        builder.Property(x => x.Target).IsRequired().HasMaxLength(320);
        builder.Property(x => x.Code).IsRequired().HasMaxLength(6);

        builder.HasIndex(x => new { x.Channel, x.Target }).IsUnique();
    }
}
```

- [ ] **Step 5: Register the DbSet and the unscoped-entity allowlist entry**

Open `Infrastructure/GymAppApi.Persistence/Context/GymAppApiDbContext.cs`. Find:

```csharp
    private static readonly HashSet<Type> IntentionallyUnscopedEntityTypes = new()
    {
        typeof(User),
        typeof(OtpVerification),
        typeof(DeviceToken),
        typeof(RefreshToken),
    };
```

Replace with:

```csharp
    private static readonly HashSet<Type> IntentionallyUnscopedEntityTypes = new()
    {
        typeof(User),
        typeof(OtpVerification),
        typeof(DeviceToken),
        typeof(RefreshToken),
        typeof(PendingContactVerification),
    };
```

Find:

```csharp
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
```

Add directly below it:

```csharp
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<PendingContactVerification> PendingContactVerifications => Set<PendingContactVerification>();
```

- [ ] **Step 6: Build to verify it compiles**

Run: `dotnet build`
Expected: `Build succeeded.` (0 errors — the existing MSB3277 EF Core version-conflict warning is pre-existing and expected)

- [ ] **Step 7: Generate the migration**

Run: `dotnet ef migrations add AddPendingContactVerificationAndUserEmailVerified --project Infrastructure/GymAppApi.Persistence --startup-project Presentation/GymAppApi.WebApi`
Expected: a new file `Infrastructure/GymAppApi.Persistence/Migrations/<timestamp>_AddPendingContactVerificationAndUserEmailVerified.cs` is created, and `GymAppApiDbContextModelSnapshot.cs` is updated.

- [ ] **Step 8: Verify the generated migration**

Open the new migration file's `Up()` method. Confirm it contains:
- `migrationBuilder.AddColumn<bool>(name: "EmailVerified", table: "Users", ...)`
- `migrationBuilder.CreateTable(name: "PendingContactVerifications", ...)` with columns `Channel` (string), `Target` (string, maxLength 320), `Code` (string, maxLength 6), `ExpiresAt`, `AttemptCount`, `LastSentAt`, `SendCount`, `WindowStartAt`
- `migrationBuilder.CreateIndex(name: "IX_PendingContactVerifications_Channel_Target", table: "PendingContactVerifications", columns: new[] { "Channel", "Target" }, unique: true)`

If any of these are missing, the EF configuration in Step 4 or the DbContext wiring in Step 5 has a mistake — fix it and regenerate (delete the migration folder's new file + revert the snapshot, then repeat Step 7) rather than hand-editing the generated migration.

- [ ] **Step 9: Commit**

```bash
git add Core/GymAppApi.Domain/Enums/ContactChannel.cs Core/GymAppApi.Domain/Entities/PendingContactVerification.cs Core/GymAppApi.Domain/Entities/User.cs Infrastructure/GymAppApi.Persistence/Configurations/PendingContactVerificationConfiguration.cs Infrastructure/GymAppApi.Persistence/Context/GymAppApiDbContext.cs Infrastructure/GymAppApi.Persistence/Migrations/
git commit -m "Add PendingContactVerification entity and User.EmailVerified"
```

---

## Task 2: Application — New exceptions (`TooManyVerificationRequestsException`, `InvalidContactVerificationCodeException`)

**Files:**
- Create: `Core/GymAppApi.Application/Features/Auth/Exceptions/TooManyVerificationRequestsException.cs`
- Create: `Core/GymAppApi.Application/Features/Auth/Exceptions/InvalidContactVerificationCodeException.cs`

No dedicated unit test — matches this codebase's existing convention (`PhoneAlreadyRegisteredException`, `EmailAlreadyRegisteredException`, `InvalidResetCodeException` have no test files either; they're exercised indirectly by the handler tests that throw them, added in Tasks 4/5).

- [ ] **Step 1: Create `TooManyVerificationRequestsException`**

```csharp
// Core/GymAppApi.Application/Features/Auth/Exceptions/TooManyVerificationRequestsException.cs
using System.Net;
using GymAppApi.Application.Common.Exceptions;

namespace GymAppApi.Application.Features.Auth.Exceptions;

public class TooManyVerificationRequestsException : BaseException
{
    public override HttpStatusCode StatusCode => HttpStatusCode.TooManyRequests;

    public TooManyVerificationRequestsException() : base("Çok fazla kod isteği gönderildi. Lütfen daha sonra tekrar deneyin.") { }
}
```

- [ ] **Step 2: Create `InvalidContactVerificationCodeException`**

Its message must say *which* channel's code was wrong (phone, email, or both) so the mobile client can tell the user which field to fix — do not reuse a single generic message for all three cases.

```csharp
// Core/GymAppApi.Application/Features/Auth/Exceptions/InvalidContactVerificationCodeException.cs
using GymAppApi.Application.Common.Exceptions;

namespace GymAppApi.Application.Features.Auth.Exceptions;

public class InvalidContactVerificationCodeException : UnauthorizedException
{
    public InvalidContactVerificationCodeException(bool phoneFailed, bool emailFailed)
        : base(BuildMessage(phoneFailed, emailFailed))
    {
    }

    private static string BuildMessage(bool phoneFailed, bool emailFailed)
    {
        if (phoneFailed && emailFailed)
        {
            return "Telefon ve e-posta kodu hatalı, süresi dolmuş veya çok fazla deneme yapıldı.";
        }

        return phoneFailed
            ? "Telefon kodu hatalı, süresi dolmuş veya çok fazla deneme yapıldı."
            : "E-posta kodu hatalı, süresi dolmuş veya çok fazla deneme yapıldı.";
    }
}
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build`
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add Core/GymAppApi.Application/Features/Auth/Exceptions/TooManyVerificationRequestsException.cs Core/GymAppApi.Application/Features/Auth/Exceptions/InvalidContactVerificationCodeException.cs
git commit -m "Add TooManyVerificationRequests and InvalidContactVerificationCode exceptions"
```

---

## Task 3: Application — Extract shared `AuthTokenResult` (unblocks deleting the old `Register` folder later)

**Files:**
- Create: `Core/GymAppApi.Application/Features/Auth/Common/AuthTokenResult.cs`
- Modify: `Core/GymAppApi.Application/Features/Auth/Commands/Login/LoginCommandHandler.cs`
- Modify: `Core/GymAppApi.Application/Features/Auth/Commands/Refresh/RefreshCommandHandler.cs`

This is a pure rename/relocation with no behavior change — `LoginCommandHandlerTests.cs`/`RefreshCommandHandlerTests.cs` assert on `result.AccessToken` etc. without naming the type, so they keep passing unmodified and serve as the "test" for this task.

- [ ] **Step 1: Create the shared `AuthTokenResult`**

```csharp
// Core/GymAppApi.Application/Features/Auth/Common/AuthTokenResult.cs
namespace GymAppApi.Application.Features.Auth.Common;

// Shared response shape for every auth flow that ends in a fresh token pair:
// login, refresh, and (from Task 5 onward) register/complete. Extracted from
// the old Register-only RegisterCommandResult, which LoginCommandHandler and
// RefreshCommandHandler were already reusing before this rename.
public class AuthTokenResult
{
    public string AccessToken { get; set; } = null!;
    public DateTime ExpiresAtUtc { get; set; }
    public string RefreshToken { get; set; } = null!;
}
```

- [ ] **Step 2: Point `LoginCommandHandler` at the new type**

Open `Core/GymAppApi.Application/Features/Auth/Commands/Login/LoginCommandHandler.cs`. Replace:

```csharp
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Auth.Commands.Register;
using GymAppApi.Application.Features.Auth.Exceptions;
using GymAppApi.Domain.Entities;
using MediatR;

namespace GymAppApi.Application.Features.Auth.Commands.Login;

public class LoginCommandHandler : IRequestHandler<LoginCommand, RegisterCommandResult>
```

with:

```csharp
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Auth.Common;
using GymAppApi.Application.Features.Auth.Exceptions;
using GymAppApi.Domain.Entities;
using MediatR;

namespace GymAppApi.Application.Features.Auth.Commands.Login;

public class LoginCommandHandler : IRequestHandler<LoginCommand, AuthTokenResult>
```

Then replace:

```csharp
    public async Task<RegisterCommandResult> Handle(LoginCommand request, CancellationToken cancellationToken)
```

with:

```csharp
    public async Task<AuthTokenResult> Handle(LoginCommand request, CancellationToken cancellationToken)
```

Then replace:

```csharp
        return new RegisterCommandResult
        {
            AccessToken = access.Token,
            ExpiresAtUtc = access.ExpiresAtUtc,
            RefreshToken = rawRefreshToken,
        };
```

with:

```csharp
        return new AuthTokenResult
        {
            AccessToken = access.Token,
            ExpiresAtUtc = access.ExpiresAtUtc,
            RefreshToken = rawRefreshToken,
        };
```

- [ ] **Step 3: Point `RefreshCommandHandler` at the new type**

Open `Core/GymAppApi.Application/Features/Auth/Commands/Refresh/RefreshCommandHandler.cs`. Replace:

```csharp
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Auth.Commands.Register;
using GymAppApi.Application.Features.Auth.Exceptions;
using GymAppApi.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Features.Auth.Commands.Refresh;

public class RefreshCommandHandler : IRequestHandler<RefreshCommand, RegisterCommandResult>
```

with:

```csharp
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Auth.Common;
using GymAppApi.Application.Features.Auth.Exceptions;
using GymAppApi.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Features.Auth.Commands.Refresh;

public class RefreshCommandHandler : IRequestHandler<RefreshCommand, AuthTokenResult>
```

Then replace:

```csharp
    public async Task<RegisterCommandResult> Handle(RefreshCommand request, CancellationToken cancellationToken)
```

with:

```csharp
    public async Task<AuthTokenResult> Handle(RefreshCommand request, CancellationToken cancellationToken)
```

Then replace:

```csharp
        return new RegisterCommandResult
        {
            AccessToken = access.Token,
            ExpiresAtUtc = access.ExpiresAtUtc,
            RefreshToken = newRawRefreshToken,
        };
```

with:

```csharp
        return new AuthTokenResult
        {
            AccessToken = access.Token,
            ExpiresAtUtc = access.ExpiresAtUtc,
            RefreshToken = newRawRefreshToken,
        };
```

- [ ] **Step 4: Build and run the existing Login/Refresh tests to confirm no regression**

Run: `dotnet build`
Expected: `Build succeeded.` (the old `Register` folder still exists and still compiles at this point — it isn't touched until Task 6 — so this build includes both the old and new types side by side)

Run: `dotnet test Tests/GymAppApi.UnitTests --filter "FullyQualifiedName~LoginCommandHandlerTests|FullyQualifiedName~RefreshCommandHandlerTests"`
Expected: all tests PASS (unchanged behavior, only the type name changed)

- [ ] **Step 5: Commit**

```bash
git add Core/GymAppApi.Application/Features/Auth/Common/AuthTokenResult.cs Core/GymAppApi.Application/Features/Auth/Commands/Login/LoginCommandHandler.cs Core/GymAppApi.Application/Features/Auth/Commands/Refresh/RefreshCommandHandler.cs
git commit -m "Extract shared AuthTokenResult out of the Register-only result type"
```

---

## Task 4: Application — `RegisterRequestOtpCommand` (send codes, enforce per-channel limits)

**Files:**
- Create: `Core/GymAppApi.Application/Features/Auth/Commands/RegisterRequestOtp/RegisterRequestOtpCommand.cs`
- Create: `Core/GymAppApi.Application/Features/Auth/Commands/RegisterRequestOtp/RegisterRequestOtpCommandHandler.cs`
- Create: `Core/GymAppApi.Application/Features/Auth/Commands/RegisterRequestOtp/RegisterRequestOtpCommandValidator.cs`
- Test: `Tests/GymAppApi.UnitTests/Features/Auth/RegisterRequestOtpCommandHandlerTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// Tests/GymAppApi.UnitTests/Features/Auth/RegisterRequestOtpCommandHandlerTests.cs
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Auth.Commands.RegisterRequestOtp;
using GymAppApi.Application.Features.Auth.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Auth;

public class RegisterRequestOtpCommandHandlerTests
{
    private static (Mock<IUnitOfWork> uow, Mock<IReadRepository<User>> userReadRepo,
        Mock<IReadRepository<PendingContactVerification>> pendingReadRepo,
        Mock<IWriteRepository<PendingContactVerification>> pendingWriteRepo,
        Mock<ISmsSender> sms, Mock<IEmailSender> email) Wire(
            bool phoneRegistered = false, bool emailRegistered = false,
            PendingContactVerification? existingPhonePending = null,
            PendingContactVerification? existingEmailPending = null)
    {
        var userReadRepo = new Mock<IReadRepository<User>>();
        // AnyAsync is called twice (phone, then email) with different predicates - Moq can't
        // distinguish lambdas by content via It.Is, so instead we compile each call's actual
        // predicate and evaluate it against a probe User matching the scenario under test.
        userReadRepo.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), default))
            .ReturnsAsync((System.Linq.Expressions.Expression<System.Func<User, bool>> predicate, CancellationToken _) =>
            {
                var compiled = predicate.Compile();
                var phoneProbe = new User { Phone = "+905551112233", Email = "taken@test.com" };
                if (phoneRegistered && compiled(phoneProbe)) return true;
                var emailProbe = new User { Phone = "+905550000000", Email = "taken@test.com" };
                if (emailRegistered && compiled(emailProbe)) return true;
                return false;
            });

        var pendingReadRepo = new Mock<IReadRepository<PendingContactVerification>>();
        pendingReadRepo
            .Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<PendingContactVerification, bool>>>(), null, false, default))
            .ReturnsAsync((System.Linq.Expressions.Expression<System.Func<PendingContactVerification, bool>> predicate, object? _, bool __, CancellationToken ___) =>
            {
                var compiled = predicate.Compile();
                if (existingPhonePending is not null && compiled(existingPhonePending)) return existingPhonePending;
                if (existingEmailPending is not null && compiled(existingEmailPending)) return existingEmailPending;
                return null;
            });

        var pendingWriteRepo = new Mock<IWriteRepository<PendingContactVerification>>();

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<User>()).Returns(userReadRepo.Object);
        uow.Setup(u => u.GetReadRepository<PendingContactVerification>()).Returns(pendingReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<PendingContactVerification>()).Returns(pendingWriteRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        var sms = new Mock<ISmsSender>();
        var email = new Mock<IEmailSender>();

        return (uow, userReadRepo, pendingReadRepo, pendingWriteRepo, sms, email);
    }

    [Fact]
    public async Task Handle_WhenPhoneAlreadyRegistered_ThrowsPhoneAlreadyRegisteredException_AndSendsNothing()
    {
        var (uow, _, _, pendingWriteRepo, sms, email) = Wire(phoneRegistered: true);
        var handler = new RegisterRequestOtpCommandHandler(uow.Object, sms.Object, email.Object);
        var command = new RegisterRequestOtpCommand { Phone = "+905551112233", Email = null };

        await Assert.ThrowsAsync<PhoneAlreadyRegisteredException>(() => handler.Handle(command, CancellationToken.None));

        pendingWriteRepo.Verify(r => r.AddAsync(It.IsAny<PendingContactVerification>(), default), Times.Never);
        sms.Verify(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenEmailAlreadyRegistered_ThrowsEmailAlreadyRegisteredException_AndSendsNothing()
    {
        var (uow, _, _, pendingWriteRepo, sms, email) = Wire(emailRegistered: true);
        var handler = new RegisterRequestOtpCommandHandler(uow.Object, sms.Object, email.Object);
        var command = new RegisterRequestOtpCommand { Phone = "+905550000000", Email = "taken@test.com" };

        await Assert.ThrowsAsync<EmailAlreadyRegisteredException>(() => handler.Handle(command, CancellationToken.None));

        pendingWriteRepo.Verify(r => r.AddAsync(It.IsAny<PendingContactVerification>(), default), Times.Never);
        sms.Verify(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenPhoneOnly_CreatesPendingRowAndSendsSms_NotEmail()
    {
        var (uow, _, _, pendingWriteRepo, sms, email) = Wire();
        var handler = new RegisterRequestOtpCommandHandler(uow.Object, sms.Object, email.Object);
        var command = new RegisterRequestOtpCommand { Phone = "+905551112233", Email = null };

        await handler.Handle(command, CancellationToken.None);

        pendingWriteRepo.Verify(r => r.AddAsync(
            It.Is<PendingContactVerification>(p => p.Channel == ContactChannel.Phone && p.Target == "+905551112233" && p.Code.Length == 6 && p.SendCount == 1),
            default), Times.Once);
        sms.Verify(s => s.SendAsync("+905551112233", It.IsAny<string>(), default), Times.Once);
        email.Verify(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenPhoneAndEmail_CreatesBothPendingRowsAndSendsBoth()
    {
        var (uow, _, _, pendingWriteRepo, sms, email) = Wire();
        var handler = new RegisterRequestOtpCommandHandler(uow.Object, sms.Object, email.Object);
        var command = new RegisterRequestOtpCommand { Phone = "+905551112233", Email = "ayse@test.com" };

        await handler.Handle(command, CancellationToken.None);

        pendingWriteRepo.Verify(r => r.AddAsync(It.Is<PendingContactVerification>(p => p.Channel == ContactChannel.Phone), default), Times.Once);
        pendingWriteRepo.Verify(r => r.AddAsync(It.Is<PendingContactVerification>(p => p.Channel == ContactChannel.Email && p.Target == "ayse@test.com"), default), Times.Once);
        sms.Verify(s => s.SendAsync("+905551112233", It.IsAny<string>(), default), Times.Once);
        email.Verify(e => e.SendAsync("ayse@test.com", It.IsAny<string>(), It.IsAny<string>(), default), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenLastSentLessThan60SecondsAgo_ThrowsTooManyVerificationRequestsException()
    {
        var existingPhonePending = new PendingContactVerification
        {
            Channel = ContactChannel.Phone, Target = "+905551112233", Code = "111111",
            ExpiresAt = DateTime.UtcNow.AddMinutes(5), AttemptCount = 0,
            LastSentAt = DateTime.UtcNow.AddSeconds(-30), SendCount = 1, WindowStartAt = DateTime.UtcNow.AddSeconds(-30),
        };
        var (uow, _, _, pendingWriteRepo, sms, _) = Wire(existingPhonePending: existingPhonePending);
        var handler = new RegisterRequestOtpCommandHandler(uow.Object, sms.Object, new Mock<IEmailSender>().Object);
        var command = new RegisterRequestOtpCommand { Phone = "+905551112233", Email = null };

        await Assert.ThrowsAsync<TooManyVerificationRequestsException>(() => handler.Handle(command, CancellationToken.None));

        pendingWriteRepo.Verify(r => r.Update(It.IsAny<PendingContactVerification>()), Times.Never);
        sms.Verify(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenSendCountAtHourlyLimit_ThrowsTooManyVerificationRequestsException()
    {
        var existingPhonePending = new PendingContactVerification
        {
            Channel = ContactChannel.Phone, Target = "+905551112233", Code = "111111",
            ExpiresAt = DateTime.UtcNow.AddMinutes(5), AttemptCount = 0,
            LastSentAt = DateTime.UtcNow.AddMinutes(-5), SendCount = 5, WindowStartAt = DateTime.UtcNow.AddMinutes(-10),
        };
        var (uow, _, _, pendingWriteRepo, sms, _) = Wire(existingPhonePending: existingPhonePending);
        var handler = new RegisterRequestOtpCommandHandler(uow.Object, sms.Object, new Mock<IEmailSender>().Object);
        var command = new RegisterRequestOtpCommand { Phone = "+905551112233", Email = null };

        await Assert.ThrowsAsync<TooManyVerificationRequestsException>(() => handler.Handle(command, CancellationToken.None));

        pendingWriteRepo.Verify(r => r.Update(It.IsAny<PendingContactVerification>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenHourlyWindowExpired_ResetsSendCountAndSucceeds()
    {
        var existingPhonePending = new PendingContactVerification
        {
            Channel = ContactChannel.Phone, Target = "+905551112233", Code = "111111",
            ExpiresAt = DateTime.UtcNow.AddMinutes(-1), AttemptCount = 2,
            LastSentAt = DateTime.UtcNow.AddHours(-2), SendCount = 5, WindowStartAt = DateTime.UtcNow.AddHours(-2),
        };
        var (uow, _, _, pendingWriteRepo, sms, _) = Wire(existingPhonePending: existingPhonePending);
        var handler = new RegisterRequestOtpCommandHandler(uow.Object, sms.Object, new Mock<IEmailSender>().Object);
        var command = new RegisterRequestOtpCommand { Phone = "+905551112233", Email = null };

        await handler.Handle(command, CancellationToken.None);

        pendingWriteRepo.Verify(r => r.Update(It.Is<PendingContactVerification>(p => p.SendCount == 1 && p.AttemptCount == 0)), Times.Once);
        sms.Verify(s => s.SendAsync("+905551112233", It.IsAny<string>(), default), Times.Once);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test Tests/GymAppApi.UnitTests --filter "FullyQualifiedName~RegisterRequestOtpCommandHandlerTests"`
Expected: FAIL — `RegisterRequestOtpCommand`/`RegisterRequestOtpCommandHandler` do not exist yet (compile error)

- [ ] **Step 3: Create the command**

```csharp
// Core/GymAppApi.Application/Features/Auth/Commands/RegisterRequestOtp/RegisterRequestOtpCommand.cs
using MediatR;

namespace GymAppApi.Application.Features.Auth.Commands.RegisterRequestOtp;

public class RegisterRequestOtpCommand : IRequest
{
    public string Phone { get; set; } = null!;
    public string? Email { get; set; }
}
```

- [ ] **Step 4: Create the validator**

```csharp
// Core/GymAppApi.Application/Features/Auth/Commands/RegisterRequestOtp/RegisterRequestOtpCommandValidator.cs
using FluentValidation;

namespace GymAppApi.Application.Features.Auth.Commands.RegisterRequestOtp;

public class RegisterRequestOtpCommandValidator : AbstractValidator<RegisterRequestOtpCommand>
{
    public RegisterRequestOtpCommandValidator()
    {
        RuleFor(x => x.Phone).NotEmpty().Matches(@"^\+[1-9]\d{7,14}$");
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
    }
}
```

- [ ] **Step 5: Create the handler**

```csharp
// Core/GymAppApi.Application/Features/Auth/Commands/RegisterRequestOtp/RegisterRequestOtpCommandHandler.cs
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Auth.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.Auth.Commands.RegisterRequestOtp;

public class RegisterRequestOtpCommandHandler : IRequestHandler<RegisterRequestOtpCommand>
{
    private const int CodeExpiryMinutes = 10;
    private const int CooldownSeconds = 60;
    private const int MaxSendsPerWindow = 5;
    private static readonly TimeSpan SendWindow = TimeSpan.FromHours(1);

    private readonly IUnitOfWork _unitOfWork;
    private readonly ISmsSender _smsSender;
    private readonly IEmailSender _emailSender;

    public RegisterRequestOtpCommandHandler(IUnitOfWork unitOfWork, ISmsSender smsSender, IEmailSender emailSender)
    {
        _unitOfWork = unitOfWork;
        _smsSender = smsSender;
        _emailSender = emailSender;
    }

    public async Task Handle(RegisterRequestOtpCommand request, CancellationToken cancellationToken)
    {
        var userReadRepo = _unitOfWork.GetReadRepository<User>();

        if (await userReadRepo.AnyAsync(u => u.Phone == request.Phone, cancellationToken))
        {
            throw new PhoneAlreadyRegisteredException();
        }

        var hasEmail = !string.IsNullOrWhiteSpace(request.Email);
        if (hasEmail && await userReadRepo.AnyAsync(u => u.Email == request.Email, cancellationToken))
        {
            throw new EmailAlreadyRegisteredException();
        }

        var pendingReadRepo = _unitOfWork.GetReadRepository<PendingContactVerification>();
        var pendingWriteRepo = _unitOfWork.GetWriteRepository<PendingContactVerification>();
        var now = DateTime.UtcNow;

        var phonePending = await pendingReadRepo.GetAsync(
            p => p.Channel == ContactChannel.Phone && p.Target == request.Phone, cancellationToken: cancellationToken);
        EnsureWithinSendLimits(phonePending, now);
        var phoneCode = GenerateCode();
        await UpsertPendingAsync(phonePending, ContactChannel.Phone, request.Phone, phoneCode, now, pendingWriteRepo, cancellationToken);

        PendingContactVerification? emailPending = null;
        string? emailCode = null;
        if (hasEmail)
        {
            emailPending = await pendingReadRepo.GetAsync(
                p => p.Channel == ContactChannel.Email && p.Target == request.Email, cancellationToken: cancellationToken);
            EnsureWithinSendLimits(emailPending, now);
            emailCode = GenerateCode();
            await UpsertPendingAsync(emailPending, ContactChannel.Email, request.Email!, emailCode, now, pendingWriteRepo, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _smsSender.SendAsync(request.Phone, $"GymApp doğrulama kodunuz: {phoneCode}. Kod 10 dakika geçerlidir.", cancellationToken);
        if (hasEmail)
        {
            await _emailSender.SendAsync(request.Email!, "GymApp E-posta Doğrulama", $"GymApp doğrulama kodunuz: {emailCode}. Kod 10 dakika geçerlidir.", cancellationToken);
        }
    }

    private static void EnsureWithinSendLimits(PendingContactVerification? pending, DateTime now)
    {
        if (pending is null)
        {
            return;
        }

        if (now - pending.LastSentAt < TimeSpan.FromSeconds(CooldownSeconds))
        {
            throw new TooManyVerificationRequestsException();
        }

        var windowExpired = now - pending.WindowStartAt >= SendWindow;
        if (!windowExpired && pending.SendCount >= MaxSendsPerWindow)
        {
            throw new TooManyVerificationRequestsException();
        }
    }

    private static async Task UpsertPendingAsync(
        PendingContactVerification? existing, ContactChannel channel, string target, string code, DateTime now,
        IWriteRepository<PendingContactVerification> writeRepo, CancellationToken cancellationToken)
    {
        if (existing is null)
        {
            await writeRepo.AddAsync(new PendingContactVerification
            {
                Channel = channel,
                Target = target,
                Code = code,
                ExpiresAt = now.AddMinutes(CodeExpiryMinutes),
                AttemptCount = 0,
                LastSentAt = now,
                SendCount = 1,
                WindowStartAt = now,
            }, cancellationToken);
            return;
        }

        var windowExpired = now - existing.WindowStartAt >= SendWindow;
        existing.Code = code;
        existing.ExpiresAt = now.AddMinutes(CodeExpiryMinutes);
        existing.AttemptCount = 0;
        existing.LastSentAt = now;
        existing.SendCount = windowExpired ? 1 : existing.SendCount + 1;
        existing.WindowStartAt = windowExpired ? now : existing.WindowStartAt;
        writeRepo.Update(existing);
    }

    private static string GenerateCode() => Random.Shared.Next(100000, 999999).ToString();
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test Tests/GymAppApi.UnitTests --filter "FullyQualifiedName~RegisterRequestOtpCommandHandlerTests"`
Expected: all 7 tests PASS

- [ ] **Step 7: Commit**

```bash
git add Core/GymAppApi.Application/Features/Auth/Commands/RegisterRequestOtp/ Tests/GymAppApi.UnitTests/Features/Auth/RegisterRequestOtpCommandHandlerTests.cs
git commit -m "Add RegisterRequestOtp command: per-channel OTP send with rate limiting"
```

---

## Task 5: Application — `RegisterCompleteCommand` (verify codes, create account)

**Files:**
- Create: `Core/GymAppApi.Application/Features/Auth/Commands/RegisterComplete/RegisterCompleteCommand.cs`
- Create: `Core/GymAppApi.Application/Features/Auth/Commands/RegisterComplete/RegisterCompleteCommandHandler.cs`
- Create: `Core/GymAppApi.Application/Features/Auth/Commands/RegisterComplete/RegisterCompleteCommandValidator.cs`
- Test: `Tests/GymAppApi.UnitTests/Features/Auth/RegisterCompleteCommandHandlerTests.cs`

Re-read the "manual transaction" grounding fact at the top of this plan before writing the handler — `RegisterCompleteCommand` does **not** implement `ITransactionalRequest`.

- [ ] **Step 1: Write the failing tests**

```csharp
// Tests/GymAppApi.UnitTests/Features/Auth/RegisterCompleteCommandHandlerTests.cs
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Auth.Commands.RegisterComplete;
using GymAppApi.Application.Features.Auth.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using Moq;

namespace GymAppApi.UnitTests.Features.Auth;

public class RegisterCompleteCommandHandlerTests
{
    private static (Mock<IUnitOfWork> uow, Mock<IReadRepository<PendingContactVerification>> pendingReadRepo,
        Mock<IWriteRepository<PendingContactVerification>> pendingWriteRepo,
        Mock<IReadRepository<User>> userReadRepo, Mock<IWriteRepository<User>> userWriteRepo,
        Mock<IWriteRepository<RefreshToken>> refreshWriteRepo, Mock<IPasswordHasher> hasher, Mock<IJwtTokenService> jwt) Wire(
            PendingContactVerification? phonePending, PendingContactVerification? emailPending = null)
    {
        var pendingReadRepo = new Mock<IReadRepository<PendingContactVerification>>();
        pendingReadRepo
            .Setup(r => r.GetAsync(It.IsAny<System.Linq.Expressions.Expression<Func<PendingContactVerification, bool>>>(), null, false, default))
            .ReturnsAsync((System.Linq.Expressions.Expression<System.Func<PendingContactVerification, bool>> predicate, object? _, bool __, CancellationToken ___) =>
            {
                var compiled = predicate.Compile();
                if (phonePending is not null && compiled(phonePending)) return phonePending;
                if (emailPending is not null && compiled(emailPending)) return emailPending;
                return null;
            });
        var pendingWriteRepo = new Mock<IWriteRepository<PendingContactVerification>>();

        var userReadRepo = new Mock<IReadRepository<User>>();
        userReadRepo.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>(), default)).ReturnsAsync(false);
        var userWriteRepo = new Mock<IWriteRepository<User>>();
        var refreshWriteRepo = new Mock<IWriteRepository<RefreshToken>>();

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.GetReadRepository<PendingContactVerification>()).Returns(pendingReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<PendingContactVerification>()).Returns(pendingWriteRepo.Object);
        uow.Setup(u => u.GetReadRepository<User>()).Returns(userReadRepo.Object);
        uow.Setup(u => u.GetWriteRepository<User>()).Returns(userWriteRepo.Object);
        uow.Setup(u => u.GetWriteRepository<RefreshToken>()).Returns(refreshWriteRepo.Object);
        uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        uow.Setup(u => u.BeginTransactionAsync(default)).ReturnsAsync(Mock.Of<IAsyncDisposable>());
        uow.Setup(u => u.CommitTransactionAsync(default)).Returns(Task.CompletedTask);
        uow.Setup(u => u.RollbackTransactionAsync(default)).Returns(Task.CompletedTask);

        var hasher = new Mock<IPasswordHasher>();
        hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("hashed");

        var jwt = new Mock<IJwtTokenService>();
        jwt.Setup(j => j.GenerateAccessToken(It.IsAny<AccessTokenClaims>())).Returns(new AccessTokenResult("access-token", DateTime.UtcNow.AddHours(1)));
        jwt.Setup(j => j.GenerateRefreshTokenValue()).Returns("raw-refresh-token");

        return (uow, pendingReadRepo, pendingWriteRepo, userReadRepo, userWriteRepo, refreshWriteRepo, hasher, jwt);
    }

    private static PendingContactVerification MakePending(ContactChannel channel, string target, string code, int attemptCount = 0) => new()
    {
        Channel = channel, Target = target, Code = code, AttemptCount = attemptCount,
        ExpiresAt = DateTime.UtcNow.AddMinutes(5), LastSentAt = DateTime.UtcNow, SendCount = 1, WindowStartAt = DateTime.UtcNow,
    };

    [Fact]
    public async Task Handle_WhenPhoneCodeWrong_IncrementsAttemptCount_SavesImmediately_AndThrows_WithoutOpeningTransaction()
    {
        var phonePending = MakePending(ContactChannel.Phone, "+905551112233", "111111");
        var (uow, _, pendingWriteRepo, _, userWriteRepo, _, hasher, jwt) = Wire(phonePending);
        var handler = new RegisterCompleteCommandHandler(uow.Object, hasher.Object, jwt.Object);
        var command = new RegisterCompleteCommand
        {
            FullName = "Ayşe", Phone = "+905551112233", PhoneCode = "000000", Email = null, EmailCode = null, Password = "Sifre123!",
        };

        var exception = await Assert.ThrowsAsync<InvalidContactVerificationCodeException>(() => handler.Handle(command, CancellationToken.None));

        Assert.Equal("Telefon kodu hatalı, süresi dolmuş veya çok fazla deneme yapıldı.", exception.Message);
        Assert.Equal(1, phonePending.AttemptCount);
        pendingWriteRepo.Verify(r => r.Update(It.Is<PendingContactVerification>(p => p.AttemptCount == 1)), Times.Once);
        uow.Verify(u => u.SaveChangesAsync(default), Times.Once);
        uow.Verify(u => u.BeginTransactionAsync(default), Times.Never);
        userWriteRepo.Verify(r => r.AddAsync(It.IsAny<User>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenPendingRowMissing_ThrowsInvalidContactVerificationCodeException()
    {
        var (uow, _, _, _, _, _, hasher, jwt) = Wire(phonePending: null);
        var handler = new RegisterCompleteCommandHandler(uow.Object, hasher.Object, jwt.Object);
        var command = new RegisterCompleteCommand
        {
            FullName = "Ayşe", Phone = "+905559999999", PhoneCode = "123456", Email = null, EmailCode = null, Password = "Sifre123!",
        };

        await Assert.ThrowsAsync<InvalidContactVerificationCodeException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenAttemptCountAtMax_ThrowsWithoutCheckingCode()
    {
        var phonePending = MakePending(ContactChannel.Phone, "+905551112233", "123456", attemptCount: 5);
        var (uow, _, pendingWriteRepo, _, _, _, hasher, jwt) = Wire(phonePending);
        var handler = new RegisterCompleteCommandHandler(uow.Object, hasher.Object, jwt.Object);
        var command = new RegisterCompleteCommand
        {
            FullName = "Ayşe", Phone = "+905551112233", PhoneCode = "123456", Email = null, EmailCode = null, Password = "Sifre123!",
        };

        await Assert.ThrowsAsync<InvalidContactVerificationCodeException>(() => handler.Handle(command, CancellationToken.None));

        pendingWriteRepo.Verify(r => r.Update(It.IsAny<PendingContactVerification>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenPhoneOnlyAndCodeCorrect_CreatesUserInsideTransaction_ReturnsTokens_RemovesPendingRow()
    {
        var phonePending = MakePending(ContactChannel.Phone, "+905551112233", "123456");
        var (uow, _, pendingWriteRepo, _, userWriteRepo, refreshWriteRepo, hasher, jwt) = Wire(phonePending);
        var handler = new RegisterCompleteCommandHandler(uow.Object, hasher.Object, jwt.Object);
        var command = new RegisterCompleteCommand
        {
            FullName = "Ayşe Yılmaz", Phone = "+905551112233", PhoneCode = "123456", Email = null, EmailCode = null, Password = "Sifre123!",
        };

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.Equal("access-token", result.AccessToken);
        Assert.Equal("raw-refresh-token", result.RefreshToken);
        userWriteRepo.Verify(r => r.AddAsync(It.Is<User>(u => u.FullName == "Ayşe Yılmaz" && u.Phone == "+905551112233" && u.PhoneVerified && !u.EmailVerified), default), Times.Once);
        refreshWriteRepo.Verify(r => r.AddAsync(It.IsAny<RefreshToken>(), default), Times.Once);
        pendingWriteRepo.Verify(r => r.Remove(phonePending), Times.Once);
        uow.Verify(u => u.BeginTransactionAsync(default), Times.Once);
        uow.Verify(u => u.CommitTransactionAsync(default), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenPhoneAndEmailBothCorrect_SetsBothVerifiedFlags_RemovesBothPendingRows()
    {
        var phonePending = MakePending(ContactChannel.Phone, "+905551112233", "123456");
        var emailPending = MakePending(ContactChannel.Email, "ayse@test.com", "654321");
        var (uow, _, pendingWriteRepo, _, userWriteRepo, _, hasher, jwt) = Wire(phonePending, emailPending);
        var handler = new RegisterCompleteCommandHandler(uow.Object, hasher.Object, jwt.Object);
        var command = new RegisterCompleteCommand
        {
            FullName = "Ayşe", Phone = "+905551112233", PhoneCode = "123456", Email = "ayse@test.com", EmailCode = "654321", Password = "Sifre123!",
        };

        await handler.Handle(command, CancellationToken.None);

        userWriteRepo.Verify(r => r.AddAsync(It.Is<User>(u => u.PhoneVerified && u.EmailVerified), default), Times.Once);
        pendingWriteRepo.Verify(r => r.Remove(phonePending), Times.Once);
        pendingWriteRepo.Verify(r => r.Remove(emailPending), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenEmailCodeWrongButPhoneCorrect_DoesNotCreateUser_OnlyIncrementsEmailAttemptCount()
    {
        var phonePending = MakePending(ContactChannel.Phone, "+905551112233", "123456");
        var emailPending = MakePending(ContactChannel.Email, "ayse@test.com", "654321");
        var (uow, _, pendingWriteRepo, _, userWriteRepo, _, hasher, jwt) = Wire(phonePending, emailPending);
        var handler = new RegisterCompleteCommandHandler(uow.Object, hasher.Object, jwt.Object);
        var command = new RegisterCompleteCommand
        {
            FullName = "Ayşe", Phone = "+905551112233", PhoneCode = "123456", Email = "ayse@test.com", EmailCode = "000000", Password = "Sifre123!",
        };

        var exception = await Assert.ThrowsAsync<InvalidContactVerificationCodeException>(() => handler.Handle(command, CancellationToken.None));

        Assert.Equal("E-posta kodu hatalı, süresi dolmuş veya çok fazla deneme yapıldı.", exception.Message);
        Assert.Equal(0, phonePending.AttemptCount);
        Assert.Equal(1, emailPending.AttemptCount);
        userWriteRepo.Verify(r => r.AddAsync(It.IsAny<User>(), default), Times.Never);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test Tests/GymAppApi.UnitTests --filter "FullyQualifiedName~RegisterCompleteCommandHandlerTests"`
Expected: FAIL — types do not exist yet

- [ ] **Step 3: Create the command**

```csharp
// Core/GymAppApi.Application/Features/Auth/Commands/RegisterComplete/RegisterCompleteCommand.cs
using GymAppApi.Application.Features.Auth.Common;
using MediatR;

namespace GymAppApi.Application.Features.Auth.Commands.RegisterComplete;

// Deliberately does NOT implement ITransactionalRequest - see this plan's
// "manual transaction" grounding fact. The wrong-code failure path must
// persist its AttemptCount increment independently of the success path's
// atomic User+RefreshToken+pending-cleanup segment, which the handler opens
// its own transaction around instead.
public class RegisterCompleteCommand : IRequest<AuthTokenResult>
{
    public string FullName { get; set; } = null!;
    public string Phone { get; set; } = null!;
    public string PhoneCode { get; set; } = null!;
    public string? Email { get; set; }
    public string? EmailCode { get; set; }
    public string Password { get; set; } = null!;
}
```

- [ ] **Step 4: Create the validator**

```csharp
// Core/GymAppApi.Application/Features/Auth/Commands/RegisterComplete/RegisterCompleteCommandValidator.cs
using FluentValidation;

namespace GymAppApi.Application.Features.Auth.Commands.RegisterComplete;

public class RegisterCompleteCommandValidator : AbstractValidator<RegisterCompleteCommand>
{
    public RegisterCompleteCommandValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Phone).NotEmpty().Matches(@"^\+[1-9]\d{7,14}$");
        RuleFor(x => x.PhoneCode).NotEmpty().Matches(@"^\d{6}$");
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.EmailCode).NotEmpty().Matches(@"^\d{6}$").When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8);
    }
}
```

- [ ] **Step 5: Create the handler**

```csharp
// Core/GymAppApi.Application/Features/Auth/Commands/RegisterComplete/RegisterCompleteCommandHandler.cs
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Auth.Common;
using GymAppApi.Application.Features.Auth.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.Auth.Commands.RegisterComplete;

public class RegisterCompleteCommandHandler : IRequestHandler<RegisterCompleteCommand, AuthTokenResult>
{
    private const int MaxAttempts = 5;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;

    public RegisterCompleteCommandHandler(IUnitOfWork unitOfWork, IPasswordHasher passwordHasher, IJwtTokenService jwtTokenService)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
    }

    public async Task<AuthTokenResult> Handle(RegisterCompleteCommand request, CancellationToken cancellationToken)
    {
        var pendingReadRepo = _unitOfWork.GetReadRepository<PendingContactVerification>();
        var pendingWriteRepo = _unitOfWork.GetWriteRepository<PendingContactVerification>();
        var hasEmail = !string.IsNullOrWhiteSpace(request.Email);

        var phonePending = await pendingReadRepo.GetAsync(
            p => p.Channel == ContactChannel.Phone && p.Target == request.Phone, cancellationToken: cancellationToken);
        var phoneValid = TryConsumeAttempt(phonePending, request.PhoneCode, pendingWriteRepo);

        PendingContactVerification? emailPending = null;
        var emailValid = true;
        if (hasEmail)
        {
            emailPending = await pendingReadRepo.GetAsync(
                p => p.Channel == ContactChannel.Email && p.Target == request.Email, cancellationToken: cancellationToken);
            emailValid = TryConsumeAttempt(emailPending, request.EmailCode!, pendingWriteRepo);
        }

        if (!phoneValid || !emailValid)
        {
            // No ambient transaction here - this save commits immediately and
            // independently, so the AttemptCount increment(s) above survive
            // the throw right after. See this plan's "manual transaction"
            // grounding fact for why this command is not ITransactionalRequest.
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            throw new InvalidContactVerificationCodeException(phoneFailed: !phoneValid, emailFailed: hasEmail && !emailValid);
        }

        var userReadRepo = _unitOfWork.GetReadRepository<User>();
        if (await userReadRepo.AnyAsync(u => u.Phone == request.Phone, cancellationToken))
        {
            throw new PhoneAlreadyRegisteredException();
        }
        if (hasEmail && await userReadRepo.AnyAsync(u => u.Email == request.Email, cancellationToken))
        {
            throw new EmailAlreadyRegisteredException();
        }

        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var user = new User
            {
                FullName = request.FullName,
                Phone = request.Phone,
                Email = request.Email,
                PasswordHash = _passwordHasher.Hash(request.Password),
                PhoneVerified = true,
                EmailVerified = hasEmail,
            };
            await _unitOfWork.GetWriteRepository<User>().AddAsync(user, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken); // need user.Id before issuing tokens

            var access = _jwtTokenService.GenerateAccessToken(new AccessTokenClaims(user.Id, user.FullName, user.Email, user.Phone));
            var rawRefreshToken = _jwtTokenService.GenerateRefreshTokenValue();

            await _unitOfWork.GetWriteRepository<RefreshToken>().AddAsync(new RefreshToken
            {
                UserId = user.Id,
                TokenHash = _passwordHasher.Hash(rawRefreshToken),
                ExpiresAt = DateTime.UtcNow.AddDays(30),
            }, cancellationToken);

            pendingWriteRepo.Remove(phonePending!);
            if (emailPending is not null)
            {
                pendingWriteRepo.Remove(emailPending);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            return new AuthTokenResult
            {
                AccessToken = access.Token,
                ExpiresAtUtc = access.ExpiresAtUtc,
                RefreshToken = rawRefreshToken,
            };
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    private static bool TryConsumeAttempt(PendingContactVerification? pending, string code, IWriteRepository<PendingContactVerification> writeRepo)
    {
        if (pending is null || pending.ExpiresAt <= DateTime.UtcNow || pending.AttemptCount >= MaxAttempts)
        {
            return false;
        }

        if (pending.Code != code)
        {
            pending.AttemptCount += 1;
            writeRepo.Update(pending);
            return false;
        }

        return true;
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test Tests/GymAppApi.UnitTests --filter "FullyQualifiedName~RegisterCompleteCommandHandlerTests"`
Expected: all 6 tests PASS

- [ ] **Step 7: Commit**

```bash
git add Core/GymAppApi.Application/Features/Auth/Commands/RegisterComplete/ Tests/GymAppApi.UnitTests/Features/Auth/RegisterCompleteCommandHandlerTests.cs
git commit -m "Add RegisterComplete command: verify codes then create the account"
```

---

## Task 6: Presentation — Wire the controller, delete the old `Register` command

**Files:**
- Modify: `Presentation/GymAppApi.WebApi/Controllers/AuthController.cs`
- Delete: `Core/GymAppApi.Application/Features/Auth/Commands/Register/RegisterCommand.cs`
- Delete: `Core/GymAppApi.Application/Features/Auth/Commands/Register/RegisterCommandHandler.cs`
- Delete: `Core/GymAppApi.Application/Features/Auth/Commands/Register/RegisterCommandValidator.cs`
- Delete: `Core/GymAppApi.Application/Features/Auth/Commands/Register/RegisterCommandResult.cs`
- Delete: `Tests/GymAppApi.UnitTests/Features/Auth/RegisterCommandHandlerTests.cs`

- [ ] **Step 1: Update the controller**

Open `Presentation/GymAppApi.WebApi/Controllers/AuthController.cs`. Replace:

```csharp
using GymAppApi.Application.Features.Auth.Commands.DeleteMe;
using GymAppApi.Application.Features.Auth.Commands.ForgotPassword;
using GymAppApi.Application.Features.Auth.Commands.Login;
using GymAppApi.Application.Features.Auth.Commands.Refresh;
using GymAppApi.Application.Features.Auth.Commands.Register;
using GymAppApi.Application.Features.Auth.Commands.ResetPassword;
```

with:

```csharp
using GymAppApi.Application.Features.Auth.Commands.DeleteMe;
using GymAppApi.Application.Features.Auth.Commands.ForgotPassword;
using GymAppApi.Application.Features.Auth.Commands.Login;
using GymAppApi.Application.Features.Auth.Commands.Refresh;
using GymAppApi.Application.Features.Auth.Commands.RegisterComplete;
using GymAppApi.Application.Features.Auth.Commands.RegisterRequestOtp;
using GymAppApi.Application.Features.Auth.Commands.ResetPassword;
```

Then replace:

```csharp
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }
```

with:

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

- [ ] **Step 2: Delete the old Register command and its test**

```bash
git rm Core/GymAppApi.Application/Features/Auth/Commands/Register/RegisterCommand.cs Core/GymAppApi.Application/Features/Auth/Commands/Register/RegisterCommandHandler.cs Core/GymAppApi.Application/Features/Auth/Commands/Register/RegisterCommandValidator.cs Core/GymAppApi.Application/Features/Auth/Commands/Register/RegisterCommandResult.cs Tests/GymAppApi.UnitTests/Features/Auth/RegisterCommandHandlerTests.cs
```

(The `Register` folder itself is left empty and can be removed by your OS/IDE automatically — git does not track empty directories, so no further action is needed there.)

- [ ] **Step 3: Build and run the full unit test suite**

Run: `dotnet build`
Expected: `Build succeeded.` (0 errors — if you see `CS0246: The type or namespace name 'RegisterCommand' could not be found`, you missed a reference; search the whole solution for `Commands.Register` and fix any remaining usage)

Run: `dotnet test Tests/GymAppApi.UnitTests`
Expected: all tests PASS, and the old `RegisterCommandHandlerTests` no longer appears in the list (it was deleted, not just skipped)

- [ ] **Step 4: Commit**

```bash
git add Presentation/GymAppApi.WebApi/Controllers/AuthController.cs
git commit -m "Wire register/request-otp and register/complete endpoints, remove old register"
```

---

## Task 7: Integration test — real EF Core stack proves the transaction-scoping decision actually works

**Files:**
- Test: `Tests/GymAppApi.IntegrationTests/RegisterCompleteAttemptPersistenceTests.cs`

This is the same class of regression test as `Tests/GymAppApi.IntegrationTests/RefreshTokenConcurrencyTests.cs` — Moq-based tests from Task 5 prove the handler *calls* `SaveChangesAsync`/`BeginTransactionAsync` in the right order, but only a real `DbContext` can prove that an `AttemptCount` increment saved outside a transaction genuinely survives, and that a genuine `DbContext`-level rollback (forced by throwing inside the transaction block) genuinely discards the User it just added. Read `Tests/GymAppApi.IntegrationTests/RefreshTokenConcurrencyTests.cs` and `Tests/GymAppApi.IntegrationTests/CustomWebApplicationFactory.cs` before writing this file to match existing conventions (`FakeTenantContext`, `ReadRepository<T>`/`WriteRepository<T>` from `GymAppApi.Persistence.Repositories`, `UseInMemoryDatabase`).

- [ ] **Step 1: Write the failing tests**

```csharp
// Tests/GymAppApi.IntegrationTests/RegisterCompleteAttemptPersistenceTests.cs
using GymAppApi.Application.Features.Auth.Commands.RegisterComplete;
using GymAppApi.Application.Features.Auth.Commands.RegisterRequestOtp;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.Infrastructure.Auth;
using GymAppApi.Persistence.Context;
using GymAppApi.Persistence.Repositories;
using GymAppApi.Persistence.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GymAppApi.IntegrationTests;

// Regression coverage for the deliberate "no ITransactionalRequest, manual
// transaction only around the success path" design in
// RegisterCompleteCommandHandler (see the backend plan's grounding facts).
// A Moq-based test can only prove the handler CALLS SaveChangesAsync/
// BeginTransactionAsync in the right order; it cannot prove that a save
// outside a transaction genuinely persists past a later throw, or that a
// genuine EF rollback genuinely discards a just-added User. This test uses
// the real GymAppApiDbContext + real ReadRepository/WriteRepository/
// UnitOfWork stack (EF InMemory) to prove both.
public class RegisterCompleteAttemptPersistenceTests
{
    private static (GymAppApiDbContext context, UnitOfWork uow) CreateStack(string dbName)
    {
        var options = new DbContextOptionsBuilder<GymAppApiDbContext>().UseInMemoryDatabase(dbName).Options;
        var context = new GymAppApiDbContext(options, new FakeTenantContext { IsSuperAdmin = true });
        var uow = new UnitOfWork(context);
        return (context, uow);
    }

    private sealed class StubPasswordHasher : IApplicationPasswordHasher
    {
        public string Hash(string input) => "hashed:" + input;
        public bool Verify(string hash, string input) => hash == "hashed:" + input;
    }

    private sealed class StubJwtTokenService : IApplicationJwtTokenService
    {
        public GymAppApi.Application.Common.Interfaces.AccessTokenResult GenerateAccessToken(GymAppApi.Application.Common.Interfaces.AccessTokenClaims claims)
            => new("access-token", DateTime.UtcNow.AddHours(1));
        public string GenerateRefreshTokenValue() => "raw-refresh-token";
    }

    [Fact]
    public async Task WrongCode_AttemptCountIncrementSurvives_EvenThoughOverallRequestThrows()
    {
        var dbName = Guid.NewGuid().ToString();
        var (seedContext, seedUow) = CreateStack(dbName);
        await using (seedContext)
        {
            await seedUow.GetWriteRepository<PendingContactVerification>().AddAsync(new PendingContactVerification
            {
                Channel = ContactChannel.Phone, Target = "+905551112233", Code = "123456",
                ExpiresAt = DateTime.UtcNow.AddMinutes(5), AttemptCount = 0,
                LastSentAt = DateTime.UtcNow, SendCount = 1, WindowStartAt = DateTime.UtcNow,
            });
            await seedUow.SaveChangesAsync();
        }

        var (context, uow) = CreateStack(dbName);
        await using (context)
        {
            var handler = new RegisterCompleteCommandHandler(uow, new StubPasswordHasher(), new StubJwtTokenService());
            var command = new RegisterCompleteCommand
            {
                FullName = "Ayşe", Phone = "+905551112233", PhoneCode = "000000", Email = null, EmailCode = null, Password = "Sifre123!",
            };

            await Assert.ThrowsAsync<GymAppApi.Application.Features.Auth.Exceptions.InvalidContactVerificationCodeException>(
                () => handler.Handle(command, CancellationToken.None));
        }

        var (verifyContext, verifyUow) = CreateStack(dbName);
        await using (verifyContext)
        {
            var pending = await verifyUow.GetReadRepository<PendingContactVerification>()
                .GetAsync(p => p.Channel == ContactChannel.Phone && p.Target == "+905551112233");
            Assert.NotNull(pending);
            Assert.Equal(1, pending!.AttemptCount);
        }
    }

    [Fact]
    public async Task CorrectCode_CreatesUser_RemovesPendingRow_InOneAtomicTransaction()
    {
        var dbName = Guid.NewGuid().ToString();
        var (seedContext, seedUow) = CreateStack(dbName);
        await using (seedContext)
        {
            await seedUow.GetWriteRepository<PendingContactVerification>().AddAsync(new PendingContactVerification
            {
                Channel = ContactChannel.Phone, Target = "+905559998877", Code = "654321",
                ExpiresAt = DateTime.UtcNow.AddMinutes(5), AttemptCount = 0,
                LastSentAt = DateTime.UtcNow, SendCount = 1, WindowStartAt = DateTime.UtcNow,
            });
            await seedUow.SaveChangesAsync();
        }

        var (context, uow) = CreateStack(dbName);
        await using (context)
        {
            var handler = new RegisterCompleteCommandHandler(uow, new StubPasswordHasher(), new StubJwtTokenService());
            var command = new RegisterCompleteCommand
            {
                FullName = "Mert", Phone = "+905559998877", PhoneCode = "654321", Email = null, EmailCode = null, Password = "Sifre123!",
            };

            var result = await handler.Handle(command, CancellationToken.None);
            Assert.Equal("access-token", result.AccessToken);
        }

        var (verifyContext, verifyUow) = CreateStack(dbName);
        await using (verifyContext)
        {
            var user = await verifyUow.GetReadRepository<User>().GetAsync(u => u.Phone == "+905559998877");
            Assert.NotNull(user);
            Assert.True(user!.PhoneVerified);

            var pending = await verifyUow.GetReadRepository<PendingContactVerification>()
                .GetAsync(p => p.Channel == ContactChannel.Phone && p.Target == "+905559998877");
            Assert.Null(pending);
        }
    }
}
```

**Before running:** check the actual namespace/interface names for the password hasher and JWT service implementations used by `RefreshTokenConcurrencyTests.cs`'s neighbors in `Tests/GymAppApi.IntegrationTests/` (search for how `CustomWebApplicationFactory.cs` or any existing integration test constructs `IPasswordHasher`/`IJwtTokenService` stand-ins) — the `IApplicationPasswordHasher`/`IApplicationJwtTokenService` names above are placeholders for "whatever this repo's actual `IPasswordHasher`/`IJwtTokenService` interfaces are called" (they are `GymAppApi.Application.Common.Interfaces.IPasswordHasher` / `IJwtTokenService` per this plan's own Task 5 — use those exact names, this note exists only because the stub class needs `using GymAppApi.Application.Common.Interfaces;` and to implement those two interfaces directly, not a nonexistent `IApplication*` pair). Fix the `using` and base interface names in the stub classes above to `IPasswordHasher`/`IJwtTokenService` before running.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test Tests/GymAppApi.IntegrationTests --filter "FullyQualifiedName~RegisterCompleteAttemptPersistenceTests"`
Expected: FAIL initially with a compile error (fix the stub interface names per the note above), then FAIL because the file is new but the production code already exists from Task 5 — re-run once the stub names are corrected and confirm it actually exercises real behavior, not a trivial always-pass.

- [ ] **Step 3: Run the tests to verify they pass**

Run: `dotnet test Tests/GymAppApi.IntegrationTests --filter "FullyQualifiedName~RegisterCompleteAttemptPersistenceTests"`
Expected: both tests PASS

- [ ] **Step 4: Run the whole solution's test suite**

Run: `dotnet test`
Expected: all unit + integration tests PASS, no regressions

- [ ] **Step 5: Commit**

```bash
git add Tests/GymAppApi.IntegrationTests/RegisterCompleteAttemptPersistenceTests.cs
git commit -m "Add EF-backed regression test for RegisterComplete's transaction scoping"
```

---

## Task 8: Manual smoke test against a real running Postgres instance

**Files:** none (verification only, no code changes)

- [ ] **Step 1: Start Postgres and the API**

If not already running from a prior session: start Docker Desktop, confirm the `postgres` container is up (`docker ps`), confirm `gymapp_dev` exists, run `dotnet ef database update --project Infrastructure/GymAppApi.Persistence --startup-project Presentation/GymAppApi.WebApi` to apply this plan's new migration, then `dotnet run --project Presentation/GymAppApi.WebApi --urls http://localhost:5195`.

- [ ] **Step 2: Walk through the full flow with real HTTP calls**

Request an OTP for a fresh phone number (replace with a number not already in `gymapp_dev`):

```bash
curl -i -X POST http://localhost:5195/api/auth/register/request-otp -H "Content-Type: application/json" -d "{\"phone\":\"+905551234567\"}"
```

Expected: `204 No Content`. Check the running API's console output for a `[FAKE SMS]` log line containing a 6-digit code.

Complete registration with that code:

```bash
curl -i -X POST http://localhost:5195/api/auth/register/complete -H "Content-Type: application/json" -d "{\"fullName\":\"Test Kullanici\",\"phone\":\"+905551234567\",\"phoneCode\":\"<code from log>\",\"password\":\"Sifre123!\"}"
```

Expected: `201 Created` with `{accessToken, expiresAtUtc, refreshToken}`.

- [ ] **Step 3: Verify the rate limit works for real**

Immediately repeat Step 2's request-otp call for the same (now-verified) phone number.
Expected: `409 Conflict` (`"Bu telefon numarasıyla zaten bir hesap var."`) — confirms request-otp checks the `User` table, not just the pending table.

Request an OTP for a second, different fresh phone number twice in a row within 60 seconds.
Expected: first call `204`, second call `429 Too Many Requests`.

- [ ] **Step 4: Verify email verification, if you have a way to read the `[FAKE EMAIL]` log line**

```bash
curl -i -X POST http://localhost:5195/api/auth/register/request-otp -H "Content-Type: application/json" -d "{\"phone\":\"+905557654321\",\"email\":\"test@example.com\"}"
```

Expected: `204`, two log lines (`[FAKE SMS]` and `[FAKE EMAIL]`) each with their own 6-digit code.

```bash
curl -i -X POST http://localhost:5195/api/auth/register/complete -H "Content-Type: application/json" -d "{\"fullName\":\"Test Iki\",\"phone\":\"+905557654321\",\"phoneCode\":\"<phone code>\",\"email\":\"test@example.com\",\"emailCode\":\"<email code>\",\"password\":\"Sifre123!\"}"
```

Expected: `201 Created`.

- [ ] **Step 5: Confirm no regressions in the untouched auth endpoints**

Log in with the account created in Step 2 (`POST /api/auth/login` with `identifier`/`password`) — expect `200`. This confirms `AuthTokenResult`'s Task 3 rename didn't break Login.

- [ ] **Step 6: Stop the API, leave Postgres running or stopped per your own preference (it's the shared container noted in `[[project-backend-foundation-progress]]` — do not remove it)**

No commit for this task (verification only).

---

## Self-review notes (for whoever executes this plan)

- **Spec coverage:** two-step flow ✅ (Tasks 4–6), per-channel 60s/hourly-5 limit ✅ (Task 4), verify-before-create ✅ (Task 5), E.164 phone format validation ✅ (Tasks 4–5 validators), 6-digit code format validation ✅ (Task 5 validator), phone/email-specific error messages ✅ (Task 2 + Task 5's exception call), same-number-retry-allowed-if-unverified ✅ (Task 4's upsert-on-existing-pending-row logic — no explicit "already pending" error exists, matching the spec).
- **Known follow-up NOT in this plan** (matches the spec's own "Açık Notlar"): no cleanup job for expired `PendingContactVerification` rows; no concurrency token on that table (two racing `request-otp` calls for the same target could send two messages, cosmetic only).
