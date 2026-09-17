using GymAppApi.Application.Common.Exceptions;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Features.Assignments.Exceptions;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.Assignments.Commands.AddStaffMember;

public class AddStaffMemberCommandHandler : IRequestHandler<AddStaffMemberCommand, AddStaffMemberCommandResult>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ISmsSender _smsSender;

    public AddStaffMemberCommandHandler(IUnitOfWork unitOfWork, IPasswordHasher passwordHasher, ISmsSender smsSender)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _smsSender = smsSender;
    }

    public async Task<AddStaffMemberCommandResult> Handle(AddStaffMemberCommand request, CancellationToken cancellationToken)
    {
        var branch = await _unitOfWork.GetReadRepository<Branch>()
            .GetAsync(b => b.Id == request.BranchId, cancellationToken: cancellationToken);
        if (branch is null)
        {
            throw new NotFoundException($"Şube {request.BranchId} bulunamadı.");
        }

        // Same pattern as CreateAssignmentCommandHandler: the [Authorize]
        // policy only proves the caller holds SOME staff role somewhere -
        // re-check it's scoped to THIS branch's company (GymAdmin) or THIS
        // exact branch (BranchManager). SuperAdmin bypasses both checks.
        var callerAssignments = await _unitOfWork.GetReadRepository<Assignment>().GetAllAsync(
            a => a.UserId == request.RequestedByUserId && a.IsActive, cancellationToken: cancellationToken);
        var callerIsAuthorized = callerAssignments.Any(a =>
            a.Role == AssignmentRole.SuperAdmin ||
            (a.Role == AssignmentRole.GymAdmin && a.CompanyId == branch.CompanyId) ||
            (a.Role == AssignmentRole.BranchManager && a.BranchId == branch.Id));
        if (!callerIsAuthorized)
        {
            throw new ForbiddenException("Bu şubeye üye/antrenör ekleme yetkiniz yok.");
        }

        var userReadRepo = _unitOfWork.GetReadRepository<User>();
        var existingUser = await userReadRepo.GetAsync(u => u.Phone == request.Phone, cancellationToken: cancellationToken);

        var alreadyAssignedInCompany = existingUser is not null && await _unitOfWork.GetReadRepository<Assignment>().AnyAsync(
            a => a.UserId == existingUser.Id && a.CompanyId == branch.CompanyId && a.IsActive, cancellationToken);
        if (alreadyAssignedInCompany)
        {
            throw new UserAlreadyAssignedException();
        }

        var user = existingUser;
        if (user is null)
        {
            user = new User
            {
                FullName = request.FullName,
                Phone = request.Phone,
                Email = request.Email,
                PasswordHash = _passwordHasher.Hash(Guid.NewGuid().ToString()),
                PhoneVerified = false,
            };
            await _unitOfWork.GetWriteRepository<User>().AddAsync(user, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken); // need user.Id for the assignment
        }

        var assignment = new Assignment
        {
            UserId = user.Id,
            CompanyId = branch.CompanyId,
            BranchId = branch.Id,
            Role = request.Role,
            IsActive = true,
        };
        await _unitOfWork.GetWriteRepository<Assignment>().AddAsync(assignment, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _smsSender.SendAsync(
            request.Phone,
            "GymApp hesabınız oluşturuldu. Şifrenizi belirlemek için 'Şifremi Unuttum' akışını kullanın.",
            cancellationToken);

        return new AddStaffMemberCommandResult
        {
            AssignmentId = assignment.Id,
            UserId = user.Id,
            CompanyId = branch.CompanyId,
            BranchId = branch.Id,
            Role = assignment.Role.ToString(),
        };
    }
}
