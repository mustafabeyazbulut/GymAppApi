using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.Security;
using GymAppApi.Application.Features.ClassScheduling.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Features.ClassScheduling.Commands.CancelClassEnrollment;

public class CancelClassEnrollmentCommandHandler : IRequestHandler<CancelClassEnrollmentCommand>
{
    // Master spec (2026-09-09-gym-yonetim-sistemi-design.md): "Tüm şubelerin
    // aynı saat diliminde (Türkiye) olduğu varsayılır; ClassSession ve benzeri
    // zaman alanları timezone bilgisi taşımaz." ClassSession.Date/StartTime bu
    // yüzden TR yerel saatini (UTC+3, DST yok) temsil eder - cutoff
    // karşılaştırması da aynı saat diliminde yapılmalı.
    public const int TurkeyUtcOffsetHours = GymAppApi.Application.Common.Time.TurkeyCalendar.UtcOffsetHours;

    private readonly IUnitOfWork _unitOfWork;

    public CancelClassEnrollmentCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task Handle(CancelClassEnrollmentCommand request, CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters: Member-callable (kendi kaydını iptal ediyor) -
        // CancelReservationCommandHandler'daki standing rule ile aynı.
        var enrollment = await _unitOfWork.GetReadRepository<ClassEnrollment>().GetAsync(
            e => e.Id == request.ClassEnrollmentId,
            include: q => q.IgnoreQueryFilters()
                .Include(e => e.ClassSession)
                .Include(e => e.PackageAssignment),
            cancellationToken: cancellationToken);
        if (enrollment is null)
        {
            throw new NotFoundException("ClassEnrollmentNotFound", request.ClassEnrollmentId);
        }

        bool callerIsAuthorized;
        if (enrollment.MemberUserId == request.RequestedByUserId)
        {
            callerIsAuthorized = true;
        }
        else
        {
            var callerAssignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
                a => a.UserId == request.RequestedByUserId && a.IsActive, cancellationToken: cancellationToken);
            callerIsAuthorized = callerAssignments.Any(a =>
                (a.Role == AssignmentRole.GymAdmin && a.CompanyId == enrollment.CompanyId) ||
                (a.Role == AssignmentRole.BranchManager && a.BranchId == enrollment.BranchId));
        }
        if (!callerIsAuthorized)
        {
            throw new ForbiddenException("ForbiddenCancelClassEnrollment");
        }

        await CompanyStatusGuard.EnsureActiveAsync(_unitOfWork, enrollment.CompanyId, cancellationToken);

        // Yarış güvenliği - kilit sırası katılımla aynı (ders -> kayıt -> paket
        // ataması; paket ataması hep en sonda, deadlock olmasın). Durum ve hak
        // GÜNCEL (kilitli) satırlar üzerinden: aynı kayıt iki kez iade edilemez,
        // iade eşzamanlı bir check-in/katılımın düşümünü ezmez (lost update).
        var classSession = await _unitOfWork.GetForUpdateAsync<ClassSession>(enrollment.ClassSessionId, cancellationToken)
            ?? throw new NotFoundException("ClassSessionNotFound", enrollment.ClassSessionId);
        enrollment = await _unitOfWork.GetForUpdateAsync<ClassEnrollment>(enrollment.Id, cancellationToken)
            ?? throw new NotFoundException("ClassEnrollmentNotFound", request.ClassEnrollmentId);

        if (enrollment.Status != ClassEnrollmentStatus.Reserved)
        {
            throw new ClassEnrollmentNotReservedException();
        }

        var turkeyNow = DateTime.UtcNow.AddHours(TurkeyUtcOffsetHours);
        var sessionStart = classSession.Date.ToDateTime(classSession.StartTime);
        var cutoff = sessionStart.AddHours(-classSession.CancellationCutoffHours);

        if (turkeyNow < cutoff)
        {
            // Cutoff'tan önce iptal - seans iade edilir.
            enrollment.Status = ClassEnrollmentStatus.Cancelled;

            var assignment = await _unitOfWork.GetForUpdateAsync<PackageAssignment>(enrollment.PackageAssignmentId, cancellationToken);
            if (assignment is not null && assignment.RemainingSessions is not null)
            {
                assignment.RemainingSessions += 1;
                _unitOfWork.GetWriteRepository<PackageAssignment>().Update(assignment);
            }
        }
        else
        {
            // Cutoff geçtikten sonra iptal (veya hiç gelmeme, personel
            // tarafından geriye dönük iptal edildiğinde) - NoShow, iade yok.
            enrollment.Status = ClassEnrollmentStatus.NoShow;
        }

        enrollment.CancelledAt = DateTime.UtcNow;
        _unitOfWork.GetWriteRepository<ClassEnrollment>().Update(enrollment);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
