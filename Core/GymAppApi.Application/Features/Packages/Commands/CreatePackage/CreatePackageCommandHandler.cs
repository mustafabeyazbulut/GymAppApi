using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Features.Packages.Commands.CreatePackage;

public class CreatePackageCommandHandler : IRequestHandler<CreatePackageCommand, CreatePackageCommandResult>
{
    private readonly IUnitOfWork _unitOfWork;

    public CreatePackageCommandHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<CreatePackageCommandResult> Handle(CreatePackageCommand request, CancellationToken cancellationToken)
    {
        var company = await _unitOfWork.GetReadRepository<Company>()
            .GetAsync(c => c.Id == request.CompanyId, cancellationToken: cancellationToken);
        if (company is null)
        {
            throw new NotFoundException("CompanyNotFound", request.CompanyId);
        }

        // Senaryo §10.5: her paket bir şubeye ait (validator şubesiz isteği zaten
        // reddediyor) ve o şube bu firmaya ait olmalı - aksi hâlde bir GymAdmin
        // başka bir firmanın şube Id'siyle paket oluşturabilirdi.
        var branch = request.BranchId is int branchId
            ? await _unitOfWork.GetReadRepository<Branch>().GetAsync(
                b => b.Id == branchId && b.CompanyId == request.CompanyId, cancellationToken: cancellationToken)
            : null;
        if (branch is null)
        {
            throw new NotFoundException("BranchNotFound", request.BranchId ?? 0);
        }

        var callerAssignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
            a => a.UserId == request.RequestedByUserId && a.IsActive, cancellationToken: cancellationToken);

        // GymAdmin firmanın her şubesine, BranchManager sadece kendi şubesine paket tanımlar.
        var callerIsAuthorized = callerAssignments.Any(a =>
            (a.Role == AssignmentRole.GymAdmin && a.CompanyId == request.CompanyId) ||
            (a.Role == AssignmentRole.BranchManager && a.BranchId == branch.Id));
        if (!callerIsAuthorized)
        {
            throw new ForbiddenException("ForbiddenCreatePackage");
        }

        // Hizmetler paketin şubesine ait ve aktif olmalı. Filtresiz okunur (pasif
        // hizmet "yok" değil "pasif" hatası alsın); izlenen okuma çünkü pakete
        // bağlanacaklar (izlenmeyen örnek bağlanırsa yeni satır sanılırdı).
        var serviceIds = request.ServiceIds!.Distinct().ToList();
        var services = await _unitOfWork.GetReadRepository<Service>().GetAllAsync(
            s => serviceIds.Contains(s.Id) && s.BranchId == branch.Id,
            include: q => q.IgnoreQueryFilters().Include(s => s.Branch),
            enableTracking: true,
            cancellationToken: cancellationToken);
        var missing = serviceIds.FirstOrDefault(id => services.All(s => s.Id != id));
        if (missing != 0)
        {
            throw new NotFoundException("ServiceNotFound", missing);
        }
        if (services.Any(s => !s.IsActive))
        {
            throw new ConflictException("ServiceInactive");
        }

        var package = new Package
        {
            CompanyId = request.CompanyId,
            BranchId = request.BranchId,
            Name = request.Name,
            Description = request.Description,
            Type = request.Type,
            DurationDays = request.DurationDays,
            SessionCount = request.SessionCount,
            Price = request.Price,
            MaxFreezeDays = request.MaxFreezeDays,
            Services = services.ToList(),
        };

        await _unitOfWork.GetWriteRepository<Package>().AddAsync(package, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CreatePackageCommandResult { Id = package.Id, Name = package.Name };
    }
}
