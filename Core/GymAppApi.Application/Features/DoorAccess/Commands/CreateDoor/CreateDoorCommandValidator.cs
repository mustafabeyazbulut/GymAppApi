using FluentValidation;

namespace GymAppApi.Application.Features.DoorAccess.Commands.CreateDoor;

public class CreateDoorCommandValidator : AbstractValidator<CreateDoorCommand>
{
    public CreateDoorCommandValidator()
    {
        RuleFor(x => x.ZoneId).GreaterThan(0);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}
