using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.PackageAssignments;
using GymAppApi.Application.Common.Security;
using GymAppApi.Application.Features.ClassScheduling.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Features.ClassScheduling.Commands.EnrollInClassSession;

public class EnrollInClassSessionCommandHandler : IRequestHandler<EnrollInClassSessionCommand, EnrollInClassSessionCommandResult>
{
    private readonly IUnitOfWork _unitOfWork;

    public EnrollInClassSessionCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<EnrollInClassSessionCommandResult> Handle(EnrollInClassSessionCommand request, CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters: Member-callable (kendi paketiyle kayıt oluyor) -
        // CreateReservationCommandHandler'daki standing rule ile aynı: bir
        // Member'ın ambient CompanyId'si her zaman null'dır.
        var assignment = await _unitOfWork.GetReadRepository<PackageAssignment>().GetAsync(
            a => a.Id == request.PackageAssignmentId,
            include: q => q.IgnoreQueryFilters().Include(a => a.Package),
            cancellationToken: cancellationToken);
        if (assignment is null)
        {
            throw new NotFoundException("PackageAssignmentNotFound", request.PackageAssignmentId);
        }

        // Spec sadece "Member" diyor - Reservation'ın aksine burada staff'ın
        // başka bir üye adına kayıt yapması yok, sadece kendi paketiyle.
        if (assignment.MemberUserId != request.RequestedByUserId)
        {
            throw new ForbiddenException("ForbiddenEnrollInClassSession");
        }

        await CompanyStatusGuard.EnsureActiveAsync(_unitOfWork, assignment.CompanyId, cancellationToken);

        // ClassSession satırını FOR UPDATE ile kilitle - TransactionBehavior'ın
        // açtığı transaction içindeyiz (ITransactionalRequest), bu yüzden
        // kilit commit/rollback'e kadar tutulur. Aynı ClassSession'a
        // eşzamanlı kayıt olmaya çalışan ikinci istek burada bloklanır ve
        // ilk istek commit olduktan sonra GÜNCEL doluluk sayısını görür -
        // aksi halde iki istek de "kapasite dolu değil" okuyup ikisi de
        // kayıt oluşturabilir (race condition).
        var classSession = await _unitOfWork.GetForUpdateAsync<ClassSession>(request.ClassSessionId, cancellationToken);
        if (classSession is null)
        {
            throw new NotFoundException("ClassSessionNotFound", request.ClassSessionId);
        }

        // Seans hakkı yarışı: paket ataması da FOR UPDATE ile kilitlenir (kilit
        // sırası hep ders -> paket ataması, deadlock olmasın). Geçerlilik ve
        // hak kontrolü kilitli GÜNCEL satır üzerinden; paket şablonu (tip,
        // kategori) ilk okumadan.
        var package = assignment.Package;
        assignment = await _unitOfWork.GetForUpdateAsync<PackageAssignment>(assignment.Id, cancellationToken)
            ?? throw new NotFoundException("PackageAssignmentNotFound", request.PackageAssignmentId);

        // Ortak "geçerli paket" tanımı (PackageAssignmentValidity: aktif,
        // süresi dolmamış, hakkı kalmış) + bu modüle özgü şartlar: paket
        // kategorisi dersin kategorisiyle eşleşmeli ve seans bazlı pakette
        // hak sayısı tanımlı olmalı.
        var now = DateTime.UtcNow;
        var isEligible = PackageAssignmentValidity.IsUsable(assignment, now) &&
            package is not null &&
            package.Category == classSession.Category &&
            (package.Type == PackageType.Duration || assignment.RemainingSessions is > 0);
        if (!isEligible)
        {
            throw new PackageAssignmentNotEligibleForClassException();
        }

        // Aynı sorguda hem "zaten kayıtlı mı" hem "kapasite dolu mu" kontrolü
        // yapılıyor - N+1 yok, tek bir GetAllAsync.
        var activeEnrollments = await _unitOfWork.GetReadRepository<ClassEnrollment>().GetAllAsync(
            e => e.ClassSessionId == classSession.Id &&
                 (e.Status == ClassEnrollmentStatus.Reserved || e.Status == ClassEnrollmentStatus.Attended),
            include: q => q.IgnoreQueryFilters().Include(e => e.PackageAssignment),
            cancellationToken: cancellationToken);

        if (activeEnrollments.Any(e => e.PackageAssignmentId == assignment.Id))
        {
            throw new AlreadyEnrolledInClassSessionException();
        }

        if (activeEnrollments.Count >= classSession.Capacity)
        {
            throw new ClassSessionFullException();
        }

        // SessionBased paketlerde kayıt anında RemainingSessions 1 azalır -
        // mevcut CheckInReservationCommandHandler'daki düşüm deseniyle birebir
        // aynı (Duration paketlerde RemainingSessions zaten hep null'dır).
        if (package!.Type == PackageType.SessionBased)
        {
            assignment.RemainingSessions -= 1;
            _unitOfWork.GetWriteRepository<PackageAssignment>().Update(assignment);
        }

        var enrollment = new ClassEnrollment
        {
            ClassSessionId = classSession.Id,
            PackageAssignmentId = assignment.Id,
            MemberUserId = assignment.MemberUserId,
            CompanyId = classSession.CompanyId,
            BranchId = classSession.BranchId,
            Status = ClassEnrollmentStatus.Reserved,
            ReservedAt = now,
        };
        await _unitOfWork.GetWriteRepository<ClassEnrollment>().AddAsync(enrollment, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new EnrollInClassSessionCommandResult { Id = enrollment.Id, ReservedAt = enrollment.ReservedAt };
    }
}
