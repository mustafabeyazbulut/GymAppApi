using FluentValidation;

namespace GymAppApi.Application.Features.Assignments.Commands.InviteGymAdmin;

public class InviteGymAdminCommandValidator : AbstractValidator<InviteGymAdminCommand>
{
    public InviteGymAdminCommandValidator()
    {
        RuleFor(x => x.CompanyId).GreaterThan(0);
        RuleFor(x => x.Phone).NotEmpty().Matches(@"^\+[1-9]\d{7,14}$");
    }
}
