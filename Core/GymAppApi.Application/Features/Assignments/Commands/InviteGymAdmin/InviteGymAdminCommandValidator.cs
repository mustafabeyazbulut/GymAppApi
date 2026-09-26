using FluentValidation;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Application.Common.Validation;

namespace GymAppApi.Application.Features.Assignments.Commands.InviteGymAdmin;

public class InviteGymAdminCommandValidator : AbstractValidator<InviteGymAdminCommand>
{
    public InviteGymAdminCommandValidator(IPhoneNumberNormalizer phoneNumberNormalizer)
    {
        RuleFor(x => x.CompanyId).GreaterThan(0);
        RuleFor(x => x.Phone).NotEmpty().ValidPhoneNumber(phoneNumberNormalizer);
    }
}
