using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.Transactions;
using MediatR;
using Microsoft.Extensions.Logging;

namespace GymAppApi.Application.Common.Behaviors;

// Aynı "notnull" kısıt düzeltmesi ValidationBehavior'da da var - buraya
// bakış gerekçesi için o dosyanın başındaki yorum.
public class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAfterCommitActions _afterCommitActions;
    private readonly ILogger<TransactionBehavior<TRequest, TResponse>> _logger;

    public TransactionBehavior(
        IUnitOfWork unitOfWork, IAfterCommitActions afterCommitActions, ILogger<TransactionBehavior<TRequest, TResponse>> logger)
    {
        _unitOfWork = unitOfWork;
        _afterCommitActions = afterCommitActions;
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is not ITransactionalRequest)
        {
            return await next();
        }

        // Transaction'i dogrudan degil ExecuteWithRetryAsync icinden aciyoruz:
        // DbContext'in EnableRetryOnFailure execution strategy'si, kullanici
        // tarafindan baslatilan bir transaction'i ancak begin/commit/rollback'in
        // TAMAMI kendi ExecuteAsync delegate'inin icindeyse yeniden deneyebiliyor
        // - aksi halde EF Core calisma zamaninda "does not support
        // user-initiated transactions" firlatiyor.
        var response = await _unitOfWork.ExecuteWithRetryAsync(async () =>
        {
            // Her denemede sıfırdan: yeniden denenen blok dış çağrıları
            // tekrar kuyruğa koyar, önceki denemeninkiler atılır.
            _afterCommitActions.BeginDeferring();
            await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);
            try
            {
                var result = await next();
                await _unitOfWork.CommitTransactionAsync(cancellationToken);
                return result;
            }
            catch
            {
                _afterCommitActions.DiscardPending();
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                throw;
            }
        });

        // Commit kesinleşti - dış çağrılar (push/SMS/e-posta) şimdi, kilitler
        // bırakılmışken. Biri başarısız olursa istek başarısız sayılmaz (veri
        // zaten kaydedildi); loglanır ve diğerleri devam eder.
        foreach (var action in _afterCommitActions.TakePending())
        {
            try
            {
                await action(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError(exception, "Commit sonrası dış çağrı başarısız oldu ({Request}).", typeof(TRequest).Name);
            }
        }

        return response;
    }
}
