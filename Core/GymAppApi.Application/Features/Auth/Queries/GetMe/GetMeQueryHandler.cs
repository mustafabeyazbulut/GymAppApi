using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
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
            include: q => q.Include(u => u.Assignments).ThenInclude(a => a.Company),
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
            Assignments = user.Assignments
                .Where(a => a.IsActive)
                .Select(a => new MeAssignmentDto
                {
                    CompanyId = a.CompanyId ?? 0,
                    CompanyName = a.Company?.Name ?? string.Empty,
                    BranchId = a.BranchId,
                    Role = a.Role.ToString(),
                })
                .ToList(),
        };
    }
}
