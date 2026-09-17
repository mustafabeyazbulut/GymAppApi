using FluentValidation;
using GymAppApi.Domain.Enums;

namespace GymAppApi.Application.Features.Assignments.Commands.AddStaffMember;

public class AddStaffMemberCommandValidator : AbstractValidator<AddStaffMemberCommand>
{
    public AddStaffMemberCommandValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Phone).NotEmpty().Matches(@"^\+[1-9]\d{7,14}$");
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.BranchId).GreaterThan(0);
        RuleFor(x => x.Role).Must(r => r is AssignmentRole.Member or AssignmentRole.Trainer)
            .WithMessage("Role must be Member or Trainer.");
    }
}
