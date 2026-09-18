using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

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
            throw new NotFoundException($"Firma {request.CompanyId} bulunamadı.");
        }

        var callerAssignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
            a => a.UserId == request.RequestedByUserId && a.IsActive, cancellationToken: cancellationToken);

        // A company-wide package (BranchId == null) is a GymAdmin/SuperAdmin-only
        // decision, same as creating the company's own resources - a BranchManager
        // may only create a package scoped to their own exact branch.
        var callerIsAuthorized = request.BranchId is null
            ? callerAssignments.Any(a =>
                a.Role == AssignmentRole.SuperAdmin ||
                (a.Role == AssignmentRole.GymAdmin && a.CompanyId == request.CompanyId))
            : callerAssignments.Any(a =>
                a.Role == AssignmentRole.SuperAdmin ||
                (a.Role == AssignmentRole.GymAdmin && a.CompanyId == request.CompanyId) ||
                (a.Role == AssignmentRole.BranchManager && a.BranchId == request.BranchId));
        if (!callerIsAuthorized)
        {
            throw new ForbiddenException("Bu firma/şube için paket oluşturma yetkiniz yok.");
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
        };

        await _unitOfWork.GetWriteRepository<Package>().AddAsync(package, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CreatePackageCommandResult { Id = package.Id, Name = package.Name };
    }
}
