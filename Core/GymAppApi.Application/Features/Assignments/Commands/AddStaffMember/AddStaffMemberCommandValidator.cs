using FluentValidation;
using GymAppApi.Domain.Enums;

namespace GymAppApi.Application.Features.Assignments.Commands.AddStaffMember;

public class AddStaffMemberCommandValidator : AbstractValidator<AddStaffMemberCommand>
{
    public AddStaffMemberCommandValidator()
    {
        RuleFor(x => x.Phone).NotEmpty().Matches(@"^\+[1-9]\d{7,14}$");
        RuleFor(x => x.BranchId).GreaterThan(0);
        // Member is deliberately NOT allowed here yet - the product model
        // requires a Package/PackageAssignment to back a real membership,
        // and that module doesn't exist yet. Member-adding returns once it
        // does; don't add it back ad hoc.
        RuleFor(x => x.Role).Must(r => r is AssignmentRole.Trainer or AssignmentRole.BranchManager)
            .WithMessage("Role must be Trainer or BranchManager.");
    }
}
