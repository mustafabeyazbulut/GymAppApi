using GymAppApi.Application.Common.Interfaces;
using MediatR;

namespace GymAppApi.Application.Common.Behaviors;

// Aynı "notnull" kısıt düzeltmesi ValidationBehavior'da da var - buraya
// bakış gerekçesi için o dosyanın başındaki yorum.
public class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
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

        // Transaction'i dogrudan degil ExecuteWithRetryAsync icinden aciyoruz:
        // DbContext'in EnableRetryOnFailure execution strategy'si, kullanici
        // tarafindan baslatilan bir transaction'i ancak begin/commit/rollback'in
        // TAMAMI kendi ExecuteAsync delegate'inin icindeyse yeniden deneyebiliyor
        // - aksi halde EF Core calisma zamaninda "does not support
        // user-initiated transactions" firlatiyor.
        return await _unitOfWork.ExecuteWithRetryAsync(async () =>
        {
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
        });
    }
}
