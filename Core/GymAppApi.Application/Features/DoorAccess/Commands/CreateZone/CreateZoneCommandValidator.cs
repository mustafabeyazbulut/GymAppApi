using FluentValidation;

namespace GymAppApi.Application.Features.DoorAccess.Commands.CreateZone;

public class CreateZoneCommandValidator : AbstractValidator<CreateZoneCommand>
{
    public CreateZoneCommandValidator()
    {
        RuleFor(x => x.BranchId).GreaterThan(0);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}
