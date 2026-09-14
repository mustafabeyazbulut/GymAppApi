using GymAppApi.Application.Common.Behaviors;
using GymAppApi.Application.Common.Interfaces;
using MediatR;
using Moq;
using Xunit;

namespace GymAppApi.UnitTests.Behaviors;

public class TransactionBehaviorTests
{
    public record NonTransactionalPing(string Name) : IRequest<string>;
    public record TransactionalPing(string Name) : IRequest<string>, ITransactionalRequest;

    [Fact]
    public async Task Handle_WhenRequestIsNotTransactional_DoesNotOpenTransaction()
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        var behavior = new TransactionBehavior<NonTransactionalPing, string>(unitOfWork.Object);

        var result = await behavior.Handle(new NonTransactionalPing("x"), _ => Task.FromResult("ok"), CancellationToken.None);

        Assert.Equal("ok", result);
        unitOfWork.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenTransactionalAndSucceeds_CommitsTransaction()
    {
        var transaction = new Mock<IAsyncDisposable>();
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(transaction.Object);

        var behavior = new TransactionBehavior<TransactionalPing, string>(unitOfWork.Object);

        var result = await behavior.Handle(new TransactionalPing("x"), _ => Task.FromResult("ok"), CancellationToken.None);

        Assert.Equal("ok", result);
        unitOfWork.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        unitOfWork.Verify(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenTransactionalAndNextThrows_RollsBackAndRethrows()
    {
        var transaction = new Mock<IAsyncDisposable>();
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(transaction.Object);

        var behavior = new TransactionBehavior<TransactionalPing, string>(unitOfWork.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(new TransactionalPing("x"), _ => throw new InvalidOperationException("boom"), CancellationToken.None));

        unitOfWork.Verify(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        unitOfWork.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
