using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Application.Features.Auth.Queries.GetMe;

public class GetMeQueryHandler : IRequestHandler<GetMeQuery, MeResultDto>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetMeQueryHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<MeResultDto> Handle(GetMeQuery request, CancellationToken cancellationToken)
    {
        var user = await _unitOfWork.GetReadRepository<User>().GetAsync(
            u => u.Id == request.UserId,
            include: q => q.Include(u => u.Assignments).ThenInclude(a => a.Company)
                .Include(u => u.PackageAssignments).ThenInclude(pa => pa.Company)
                .Include(u => u.PackageAssignments).ThenInclude(pa => pa.Package),
            cancellationToken: cancellationToken);

        if (user is null)
        {
            throw new NotFoundException($"Kullanıcı {request.UserId} bulunamadı.");
        }

        return new MeResultDto
        {
            Id = user.Id,
            FullName = user.FullName,
            Phone = user.Phone,
            Email = user.Email,
            PreferredLanguage = user.PreferredLanguage,
            IsAccountFrozen = user.IsAccountFrozen,
            Assignments = user.Assignments
                .Where(a => a.IsActive)
                .Select(a => new MeAssignmentDto
                {
                    CompanyId = a.CompanyId,
                    CompanyName = a.Company?.Name,
                    BranchId = a.BranchId,
                    Role = a.Role.ToString(),
                })
                .ToList(),
            PackageAssignments = user.PackageAssignments
                .Where(pa => pa.Status != PackageAssignmentStatus.Cancelled)
                .Select(pa => new MePackageAssignmentDto
                {
                    CompanyId = pa.CompanyId,
                    CompanyName = pa.Company?.Name,
                    BranchId = pa.BranchId,
                    PackageId = pa.PackageId,
                    PackageName = pa.Package?.Name,
                    Status = pa.Status.ToString(),
                    EndDate = pa.EndDate,
                })
                .ToList(),
        };
    }
}
