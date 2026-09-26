using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Auth.Commands.RegisterComplete;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using GymAppApi.Persistence.Context;
using GymAppApi.Persistence.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace GymAppApi.IntegrationTests;

// Regression coverage for the deliberate "no ITransactionalRequest, manual
// transaction only around the success path" design in
// RegisterCompleteCommandHandler (see the backend plan's grounding facts).
// A Moq-based test can only prove the handler CALLS SaveChangesAsync/
// BeginTransactionAsync in the right order; it cannot prove that a save
// outside a transaction genuinely persists past a later throw. This file
// uses the real GymAppApiDbContext + real ReadRepository/WriteRepository/
// UnitOfWork stack (EF InMemory) to prove exactly that: WrongCode_... proves
// the failure path's AttemptCount save survives independently of the later
// throw, and CorrectCode_... proves the success path creates the User and
// removes the pending row atomically, as observed from a fresh DbContext.
// This does NOT prove genuine rollback-on-failure for the success path - EF
// Core's InMemory provider has no real transaction semantics (see the
// ConfigureWarnings call below), so no test here attempts one. Rollback-on-
// failure is instead covered by Task 5's Moq-based
// RegisterCompleteCommandHandlerTests.Handle_WhenSuccessPathThrowsInsideTransaction_RollsBackAndRethrows,
// which verifies the RollbackTransactionAsync/CommitTransactionAsync call
// pattern even though it can't prove real DB-level atomicity the way this
// file's tests prove real persistence.
public class RegisterCompleteAttemptPersistenceTests
{
    private static (GymAppApiDbContext context, UnitOfWork uow) CreateStack(string dbName)
    {
        // EF Core's InMemory provider doesn't implement real transactions, and
        // by default throws on Database.BeginTransactionAsync() to flag that
        // (InMemoryEventId.TransactionIgnoredWarning). The handler under test
        // genuinely calls BeginTransactionAsync/CommitTransactionAsync around
        // its success path (see the plan's "manual transaction" design), so
        // this warning must be downgraded to a no-op, matching how every
        // other provider actually behaves, to exercise that code path here.
        var options = new DbContextOptionsBuilder<GymAppApiDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var context = new GymAppApiDbContext(options, new FakeTenantContext { IsSuperAdmin = true });
        var uow = new UnitOfWork(context);
        return (context, uow);
    }

    private sealed class StubPasswordHasher : IPasswordHasher
    {
        public string Hash(string input) => "hashed:" + input;
        public bool Verify(string hash, string input) => hash == "hashed:" + input;
    }

    private sealed class StubJwtTokenService : IJwtTokenService
    {
        public AccessTokenResult GenerateAccessToken(AccessTokenClaims claims)
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
            var handler = new RegisterCompleteCommandHandler(uow, new StubPasswordHasher(), new StubJwtTokenService(), new GymAppApi.Infrastructure.Security.PhoneNumberNormalizer());
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
            var handler = new RegisterCompleteCommandHandler(uow, new StubPasswordHasher(), new StubJwtTokenService(), new GymAppApi.Infrastructure.Security.PhoneNumberNormalizer());
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
