namespace GymAppApi.Application.Common.Transactions;

// Dış servis çağrılarının (push, SMS, e-posta) transaction'a göre zamanlanması.
// Transaction içindeyken çağrı kuyruğa alınır ve sadece başarılı commit'ten
// SONRA çalışır: rollback'te hiç gitmez, execution strategy tüm bloğu yeniden
// denerse iki kez gitmez, satır kilitleri dış çağrı bitene kadar tutulmaz.
// Transaction dışında çağrı hemen çalışır. Scoped (istek başına).
public interface IAfterCommitActions
{
    Task RunOrDeferAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken);

    // Transaction sahibi (TransactionBehavior) için: her deneme başında
    // kuyruk sıfırlanır, rollback'te atılır, commit'ten sonra alınıp çalıştırılır.
    void BeginDeferring();
    void DiscardPending();
    IReadOnlyList<Func<CancellationToken, Task>> TakePending();
}

public sealed class AfterCommitActions : IAfterCommitActions
{
    // null = transaction yok, çağrılar hemen çalışır.
    private List<Func<CancellationToken, Task>>? _pending;

    public Task RunOrDeferAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        if (_pending is null)
        {
            return action(cancellationToken);
        }

        _pending.Add(action);
        return Task.CompletedTask;
    }

    public void BeginDeferring() => _pending = new List<Func<CancellationToken, Task>>();

    public void DiscardPending() => _pending = null;

    public IReadOnlyList<Func<CancellationToken, Task>> TakePending()
    {
        var pending = _pending ?? new List<Func<CancellationToken, Task>>();
        _pending = null;
        return pending;
    }
}
