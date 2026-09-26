using FluentValidation;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.Validation;

namespace GymAppApi.Application.Features.Packages.Commands.CreatePackageAssignment;

public class CreatePackageAssignmentCommandValidator : AbstractValidator<CreatePackageAssignmentCommand>
{
    public CreatePackageAssignmentCommandValidator(IPhoneNumberNormalizer phoneNumberNormalizer)
    {
        RuleFor(x => x.PackageId).GreaterThan(0);
        RuleFor(x => x.MemberPhone).NotEmpty().ValidPhoneNumber(phoneNumberNormalizer);
    }
}
