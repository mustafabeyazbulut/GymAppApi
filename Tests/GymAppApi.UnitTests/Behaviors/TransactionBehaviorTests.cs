using GymAppApi.Application.Common.Behaviors;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.Transactions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GymAppApi.UnitTests.Behaviors;

public class TransactionBehaviorTests
{
    public record NonTransactionalPing(string Name) : IRequest<string>;
    public record TransactionalPing(string Name) : IRequest<string>, ITransactionalRequest;

    private static TransactionBehavior<TRequest, string> Behavior<TRequest>(IUnitOfWork unitOfWork, IAfterCommitActions? afterCommit = null)
        where TRequest : notnull =>
        new(unitOfWork, afterCommit ?? new AfterCommitActions(), NullLogger<TransactionBehavior<TRequest, string>>.Instance);

    private static Mock<IUnitOfWork> TransactionalUnitOfWork()
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new Mock<IAsyncDisposable>().Object);
        // ExecuteWithRetryAsync gercek Npgsql execution strategy'sinin
        // yerini tutuyor - burada sadece verilen delegate'i CAGIRIYOR, aksi
        // halde BeginTransactionAsync/CommitTransactionAsync cagrilari hic
        // gerceklesmez (Moq, ayarlanmamis bir Task<T> donen metodu delegate'i
        // hic calistirmadan varsayilan degerle donuyor).
        unitOfWork.Setup(u => u.ExecuteWithRetryAsync(It.IsAny<Func<Task<string>>>()))
            .Returns<Func<Task<string>>>(operation => operation());
        return unitOfWork;
    }

    [Fact]
    public async Task Handle_WhenRequestIsNotTransactional_DoesNotOpenTransaction()
    {
        var unitOfWork = new Mock<IUnitOfWork>();

        var result = await Behavior<NonTransactionalPing>(unitOfWork.Object).Handle(new NonTransactionalPing("x"), _ => Task.FromResult("ok"), CancellationToken.None);

        Assert.Equal("ok", result);
        unitOfWork.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenTransactionalAndSucceeds_CommitsTransaction()
    {
        var unitOfWork = TransactionalUnitOfWork();

        var result = await Behavior<TransactionalPing>(unitOfWork.Object).Handle(new TransactionalPing("x"), _ => Task.FromResult("ok"), CancellationToken.None);

        Assert.Equal("ok", result);
        unitOfWork.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        unitOfWork.Verify(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenTransactionalAndNextThrows_RollsBackAndRethrows()
    {
        var unitOfWork = TransactionalUnitOfWork();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Behavior<TransactionalPing>(unitOfWork.Object).Handle(new TransactionalPing("x"), _ => throw new InvalidOperationException("boom"), CancellationToken.None));

        unitOfWork.Verify(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        unitOfWork.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // İnceleme bulgusu: dış çağrılar (push/SMS) transaction + retry içinde
    // çalışıyordu; retry'da iki kez gidebilir, kilit dış çağrı bitene kadar
    // tutulurdu. Artık commit'ten SONRA çalışırlar.
    [Fact]
    public async Task ExternalCallsInsideATransaction_RunOnlyAfterCommit()
    {
        var unitOfWork = TransactionalUnitOfWork();
        var afterCommit = new AfterCommitActions();
        var events = new List<string>();
        unitOfWork.Setup(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>())).Callback(() => events.Add("commit")).Returns(Task.CompletedTask);

        await Behavior<TransactionalPing>(unitOfWork.Object, afterCommit).Handle(new TransactionalPing("x"), async _ =>
        {
            await afterCommit.RunOrDeferAsync(_ => { events.Add("push"); return Task.CompletedTask; }, CancellationToken.None);
            events.Add("handler done");
            return "ok";
        }, CancellationToken.None);

        Assert.Equal(new[] { "handler done", "commit", "push" }, events);
    }

    [Fact]
    public async Task ExternalCalls_AreDiscardedWhenTheTransactionRollsBack()
    {
        var unitOfWork = TransactionalUnitOfWork();
        var afterCommit = new AfterCommitActions();
        var pushed = false;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Behavior<TransactionalPing>(unitOfWork.Object, afterCommit).Handle(new TransactionalPing("x"), async _ =>
        {
            await afterCommit.RunOrDeferAsync(_ => { pushed = true; return Task.CompletedTask; }, CancellationToken.None);
            throw new InvalidOperationException("boom");
        }, CancellationToken.None));

        Assert.False(pushed);
    }

    [Fact]
    public async Task WhenTheWholeTransactionIsRetried_ExternalCallsRunOnlyOnce()
    {
        var unitOfWork = TransactionalUnitOfWork();
        // Execution strategy'nin commit sırasında kopan bağlantı sonrası tüm
        // bloğu yeniden çalıştırmasını taklit eder.
        unitOfWork.Setup(u => u.ExecuteWithRetryAsync(It.IsAny<Func<Task<string>>>()))
            .Returns<Func<Task<string>>>(async operation => { await operation(); return await operation(); });
        var afterCommit = new AfterCommitActions();
        var pushCount = 0;

        await Behavior<TransactionalPing>(unitOfWork.Object, afterCommit).Handle(new TransactionalPing("x"), async _ =>
        {
            await afterCommit.RunOrDeferAsync(_ => { pushCount++; return Task.CompletedTask; }, CancellationToken.None);
            return "ok";
        }, CancellationToken.None);

        Assert.Equal(1, pushCount);
    }

    [Fact]
    public async Task AFailingExternalCallAfterCommit_IsLogged_AndDoesNotFailTheRequest()
    {
        var unitOfWork = TransactionalUnitOfWork();
        var afterCommit = new AfterCommitActions();

        var result = await Behavior<TransactionalPing>(unitOfWork.Object, afterCommit).Handle(new TransactionalPing("x"), async _ =>
        {
            await afterCommit.RunOrDeferAsync(_ => throw new HttpRequestException("push sağlayıcısı yanıt vermedi"), CancellationToken.None);
            return "ok";
        }, CancellationToken.None);

        Assert.Equal("ok", result);
    }

    [Fact]
    public async Task OutsideATransaction_ExternalCallsRunImmediately()
    {
        var afterCommit = new AfterCommitActions();
        var pushed = false;

        await afterCommit.RunOrDeferAsync(_ => { pushed = true; return Task.CompletedTask; }, CancellationToken.None);

        Assert.True(pushed);
    }
}
