using FluentValidation;

namespace GymAppApi.Application.Features.Packages.Commands.CreatePackageAssignment;

public class CreatePackageAssignmentCommandValidator : AbstractValidator<CreatePackageAssignmentCommand>
{
    public CreatePackageAssignmentCommandValidator()
    {
        RuleFor(x => x.PackageId).GreaterThan(0);
        RuleFor(x => x.MemberPhone).NotEmpty().Matches(@"^\+[1-9]\d{7,14}$");
    }
}
