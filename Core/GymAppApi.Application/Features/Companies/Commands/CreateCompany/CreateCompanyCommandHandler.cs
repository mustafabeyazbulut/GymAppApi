using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Entities;
using GymAppApi.Domain.Enums;
using MediatR;

namespace GymAppApi.Application.Features.Companies.Commands.CreateCompany;

public class CreateCompanyCommandHandler : IRequestHandler<CreateCompanyCommand, CreateCompanyCommandResult>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ISmsSender _smsSender;

    public CreateCompanyCommandHandler(IUnitOfWork unitOfWork, IPasswordHasher passwordHasher, ISmsSender smsSender)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _smsSender = smsSender;
    }

    public async Task<CreateCompanyCommandResult> Handle(CreateCompanyCommand request, CancellationToken cancellationToken)
    {
        // No [Authorize(Policy = "SuperAdminOnly")]-level re-check needed here
        // unlike CreateAssignmentCommandHandler's GymAdmin case - a SuperAdmin
        // has no per-company scope to violate, so the policy's own fresh
        // per-request Assignment re-query (AssignmentRoleAuthorizationHandler)
        // is already the complete check.
        var existingGymAdmin = await _unitOfWork.GetReadRepository<User>()
            .GetAsync(u => u.Phone == request.GymAdminPhone, cancellationToken: cancellationToken);

        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var company = new Company { Name = request.CompanyName, IsActive = true };
            await _unitOfWork.GetWriteRepository<Company>().AddAsync(company, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken); // need company.Id for the branch

            var branch = new Branch
            {
                CompanyId = company.Id,
                Name = request.BranchName,
                Address = request.BranchAddress,
                IsActive = true,
            };
            await _unitOfWork.GetWriteRepository<Branch>().AddAsync(branch, cancellationToken);

            var gymAdminUser = existingGymAdmin;
            if (gymAdminUser is null)
            {
                gymAdminUser = new User
                {
                    FullName = request.GymAdminFullName,
                    Phone = request.GymAdminPhone,
                    Email = request.GymAdminEmail,
                    PasswordHash = _passwordHasher.Hash(Guid.NewGuid().ToString()),
                    PhoneVerified = false,
                };
                await _unitOfWork.GetWriteRepository<User>().AddAsync(gymAdminUser, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken); // need gymAdminUser.Id for the assignment
            }

            // GymAdmin's BranchId is null by design (Assignment.cs's own
            // comment: a GymAdmin assignment has CompanyId set, BranchId
            // null, meaning "all branches of this company").
            await _unitOfWork.GetWriteRepository<Assignment>().AddAsync(new Assignment
            {
                UserId = gymAdminUser.Id,
                CompanyId = company.Id,
                BranchId = null,
                Role = AssignmentRole.GymAdmin,
                IsActive = true,
            }, cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            await _smsSender.SendAsync(
                request.GymAdminPhone,
                $"GymApp hesabınız oluşturuldu. Şifrenizi belirlemek için 'Şifremi Unuttum' akışını kullanın.",
                cancellationToken);

            return new CreateCompanyCommandResult
            {
                CompanyId = company.Id,
                BranchId = branch.Id,
                GymAdminUserId = gymAdminUser.Id,
                GymAdminPhone = gymAdminUser.Phone,
            };
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }
}
