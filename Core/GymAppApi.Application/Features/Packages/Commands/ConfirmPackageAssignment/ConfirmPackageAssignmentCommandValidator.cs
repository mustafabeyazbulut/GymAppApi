using FluentValidation;

namespace GymAppApi.Application.Features.Packages.Commands.ConfirmPackageAssignment;

public class ConfirmPackageAssignmentCommandValidator : AbstractValidator<ConfirmPackageAssignmentCommand>
{
    public ConfirmPackageAssignmentCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().Length(6);
    }
}
