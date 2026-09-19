using GymAppApi.Application.Common.Behaviors;
using MediatR;

namespace GymAppApi.Application.Features.ClassScheduling.Commands.EnrollInClassSession;

// ITransactionalRequest: TransactionBehavior bu komutu gerçek bir DB
// transaction'ı içinde çalıştırır. Handler, transaction içindeyken
// ClassSession satırını IUnitOfWork.GetForUpdateAsync (SELECT ... FOR UPDATE)
// ile kilitler - kapasite kontrolü + kayıt oluşturma + seans düşümü tek bir
// transaction'da, satır kilidiyle yapılır. Bu olmadan iki üye aynı anda son
// boş yere kayıt olabilir ve kapasite aşılabilir (bkz.
// docs/superpowers/specs/2026-09-20-group-class-scheduling-design.md).
public class EnrollInClassSessionCommand : IRequest<EnrollInClassSessionCommandResult>, ITransactionalRequest
{
    // Set by the controller from the route segment.
    public int ClassSessionId { get; set; }
    public int PackageAssignmentId { get; set; }

    // Set by the controller from the caller's own JWT sub claim.
    public int RequestedByUserId { get; set; }
}
